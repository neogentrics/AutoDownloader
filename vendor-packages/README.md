# Vendored packages

`TvDbSharper.4.0.11.nupkg` is a local rebuild of
[HristoKolev/TvDbSharper](https://github.com/HristoKolev/TvDbSharper) at commit
`641be4c1a7f1b20278b5ca85844b703731c5671c`, retargeted to `netstandard2.1`.

It is **not published on nuget.org** (the public feed stops at 4.0.10, which targets
`netstandard1.1` and drags in the legacy `System.Net.Http` / `System.Text.RegularExpressions`
shim packages). It is committed here so the solution restores on a clean machine and in CI
without depending on a local folder outside the repository.

`NuGet.config` in the repository root registers this directory as a package source.
