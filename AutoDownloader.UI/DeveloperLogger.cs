using System;
using System.Collections.Concurrent;

namespace AutoDownloader.UI
{
 internal static class DeveloperLogger
 {
 private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
 public static event Action<string>? OnLogReceived;

 public static void Append(string line)
 {
 try
 {
 var timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}";
 _queue.Enqueue(timestamped);
 OnLogReceived?.Invoke(timestamped);
 }
 catch { }
 }

 public static string[] DrainAll()
 {
 var list = new System.Collections.Generic.List<string>();
 while (_queue.TryDequeue(out var v)) list.Add(v);
 return list.ToArray();
 }
 }
}
