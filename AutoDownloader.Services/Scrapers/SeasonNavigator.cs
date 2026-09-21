using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Scrapers
{
    /// <summary>
    /// Works the season chooser on a show page.
    ///
    /// A show page renders one season at a time - usually the newest, sometimes the first -
    /// and the rest arrive only when the chooser is used. Reading the page as it loads
    /// therefore gets exactly one season and no indication that the others exist, which is
    /// how a nineteen-season show quietly indexes as twenty-five episodes.
    ///
    /// The chooser is almost never a real select element; it is a button that opens a list.
    /// So this drives it the way a person would: read what it offers, pick one, wait for the
    /// listing to catch up.
    /// </summary>
    public static class SeasonNavigator
    {
        /// <summary>Matches a season label, which is how both the button and its options read.</summary>
        private static readonly Regex SeasonLabel = new Regex(
            "^season[^0-9]{0,3}([0-9]{1,3})$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>How long to let the listing redraw after a season is picked.</summary>
        private const int SettleMs = 2500;

        public static event Action<string>? OnLog;

        /// <summary>
        /// Every season the page offers, in ascending order.
        ///
        /// Returns an empty list when the page has no chooser at all, which is the normal
        /// case for a single-season show and means "just read what is here".
        /// </summary>
        public static async Task<List<int>> DiscoverSeasonsAsync(IPage page)
        {
            var seasons = new HashSet<int>();

            try
            {
                // Opening the chooser is what reveals the rest; without it only the current
                // season's label is in the document.
                var chooser = await FindChooserAsync(page).ConfigureAwait(false);
                if (chooser == null) return new List<int>();

                foreach (var label in await ReadLabelsAsync(page).ConfigureAwait(false))
                {
                    var match = SeasonLabel.Match(label.Trim());
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int n)) seasons.Add(n);
                }

                try { await chooser.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 }); }
                catch { /* already open, or not clickable */ }

                await page.WaitForTimeoutAsync(1200).ConfigureAwait(false);

                foreach (var label in await ReadLabelsAsync(page).ConfigureAwait(false))
                {
                    var match = SeasonLabel.Match(label.Trim());
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int n)) seasons.Add(n);
                }

                // Close it again so it cannot sit over the listing we are about to read.
                try { await page.Keyboard.PressAsync("Escape"); } catch { }
                await page.WaitForTimeoutAsync(400).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"SeasonNavigator: could not read the season list: {ex.Message}");
            }

            return seasons.OrderBy(n => n).ToList();
        }

        /// <summary>
        /// Switches the page to a season.
        /// </summary>
        /// <returns>False when the season could not be selected; the caller should not then
        /// treat whatever is on screen as belonging to it.</returns>
        public static async Task<bool> SelectSeasonAsync(IPage page, int season)
        {
            try
            {
                var chooser = await FindChooserAsync(page).ConfigureAwait(false);
                if (chooser == null) return false;

                // Already showing it: nothing to do, and clicking would only close the list.
                string current = (await chooser.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                var currentMatch = SeasonLabel.Match(current);
                if (currentMatch.Success
                    && int.TryParse(currentMatch.Groups[1].Value, out int shown)
                    && shown == season)
                {
                    return true;
                }

                try { await chooser.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 }); }
                catch { }

                await page.WaitForTimeoutAsync(1000).ConfigureAwait(false);

                var option = await FindOptionAsync(page, season).ConfigureAwait(false);
                if (option == null)
                {
                    OnLog?.Invoke($"SeasonNavigator: season {season} was not offered by the chooser.");
                    try { await page.Keyboard.PressAsync("Escape"); } catch { }
                    return false;
                }

                await option.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 }).ConfigureAwait(false);

                // The listing is replaced in place, so wait for the traffic to stop rather
                // than for a navigation that never happens.
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                        new PageWaitForLoadStateOptions { Timeout = 15000 }).ConfigureAwait(false);
                }
                catch { }

                await page.WaitForTimeoutAsync(SettleMs).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"SeasonNavigator: could not switch to season {season}: {ex.Message}");
                return false;
            }
        }

        /// <summary>The control that opens the season list, if the page has one.</summary>
        private static async Task<IElementHandle?> FindChooserAsync(IPage page)
        {
            foreach (var selector in new[]
            {
                "button[aria-haspopup]", "[role='combobox']", "button", "[role='button']",
            })
            {
                foreach (var element in await page.QuerySelectorAllAsync(selector).ConfigureAwait(false))
                {
                    try
                    {
                        if (!await element.IsVisibleAsync().ConfigureAwait(false)) continue;

                        string text = (await element.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                        if (SeasonLabel.IsMatch(text)) return element;
                    }
                    catch { }
                }
            }

            return null;
        }

        /// <summary>The option for one season, once the list is open.</summary>
        private static async Task<IElementHandle?> FindOptionAsync(IPage page, int season)
        {
            foreach (var element in await page
                .QuerySelectorAllAsync("li,[role='option'],button,a,[role='menuitem']").ConfigureAwait(false))
            {
                try
                {
                    if (!await element.IsVisibleAsync().ConfigureAwait(false)) continue;

                    string text = (await element.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                    var match = SeasonLabel.Match(text);

                    if (match.Success
                        && int.TryParse(match.Groups[1].Value, out int n)
                        && n == season)
                    {
                        return element;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>Every visible label on the page that could be a season.</summary>
        private static async Task<string[]> ReadLabelsAsync(IPage page)
        {
            try
            {
                return await page.EvaluateAsync<string[]>(@"()=>{
                    const out=[];
                    document.querySelectorAll(""li,[role='option'],button,a,[role='menuitem'],[role='combobox']"")
                        .forEach(e=>{
                            const t=(e.innerText||'').trim().replace(/\s+/g,' ');
                            if(t && t.length<20) out.push(t);
                        });
                    return out;
                }").ConfigureAwait(false);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
