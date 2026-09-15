using System;
using System.Collections.Generic;
using System.IO;

namespace DigitalGardener
{
    public static class BrowserCacheCleaner
    {
        public static List<SystemFileItem> Scan()
        {
            var result = new List<SystemFileItem>();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Chrome
            Add(result, Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"), "Chrome");
            Add(result, Path.Combine(local, @"Google\Chrome\User Data\Default\Code Cache"), "Chrome");

            // Edge
            Add(result, Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"), "Edge");
            Add(result, Path.Combine(local, @"Microsoft\Edge\User Data\Default\Code Cache"), "Edge");

            // Firefox
            string ffProfiles = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ffProfiles))
            {
                foreach (var prof in Directory.GetDirectories(ffProfiles))
                    Add(result, Path.Combine(prof, "cache2"), "Firefox");
            }

            // Opera
            Add(result, Path.Combine(local, @"Opera Software\Opera Stable\Cache"), "Opera");
            Add(result, Path.Combine(local, @"Opera Software\Opera Stable\Code Cache"), "Opera");

            return result;
        }

        private static void Add(List<SystemFileItem> list, string dir, string browser)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in SafeEnumerate(dir))
            {
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length == 0) continue;
                    list.Add(new SystemFileItem
                    {
                        FullPath = fi.FullName,
                        Name = fi.Name,
                        SizeBytes = fi.Length,
                        LastAccessTime = fi.LastAccessTime,
                        Reason = $"Кэш {browser}",
                        Recommendation = "Очистить",
                        Source = "BrowserCache"
                    });
                }
                catch { }
            }
        }

        private static IEnumerable<string> SafeEnumerate(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] files = Array.Empty<string>();
                string[] subdirs = Array.Empty<string>();
                try { files = Directory.GetFiles(dir); } catch { }
                try { subdirs = Directory.GetDirectories(dir); } catch { }
                foreach (var f in files) yield return f;
                foreach (var d in subdirs) stack.Push(d);
            }
        }
    }
}