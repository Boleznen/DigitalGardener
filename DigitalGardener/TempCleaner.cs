using System;
using System.Collections.Generic;
using System.IO;

namespace DigitalGardener
{
    public static class TempCleaner
    {
        public static List<SystemFileItem> Scan()
        {
            var result = new List<SystemFileItem>();

            var dirs = new[]
            {
                Path.GetTempPath(),                                              // %TEMP%
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
            };

            foreach (var dir in dirs.Distinct())
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in SafeEnumerate(dir))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length == 0) continue;
                        result.Add(new SystemFileItem
                        {
                            FullPath = fi.FullName,
                            Name = fi.Name,
                            SizeBytes = fi.Length,
                            LastAccessTime = fi.LastAccessTime,
                            Reason = "Временный файл",
                            Recommendation = "Удалить",
                            Source = "Temp"
                        });
                    }
                    catch { }
                }
            }
            return result;
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