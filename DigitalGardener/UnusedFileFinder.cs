using System;
using System.Collections.Generic;
using System.IO;

namespace DigitalGardener
{
    public static class UnusedFileFinder
    {
        public static List<SystemFileItem> Scan(IEnumerable<string> roots, int daysUnused)
        {
            var result = new List<SystemFileItem>();
            var threshold = DateTime.Now.AddDays(-daysUnused);

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                ScanFolder(root, threshold, daysUnused, result, depth: 0, maxDepth: 4);
            }
            return result;
        }

        private static void ScanFolder(string dir, DateTime threshold, int days,
            List<SystemFileItem> result, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            if (IsSystemFolder(dir)) return;

            string[] files = Array.Empty<string>();
            string[] subdirs = Array.Empty<string>();
            try { files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly); } catch { }
            try { subdirs = Directory.GetDirectories(dir); } catch { }

            foreach (var file in files)
            {
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length < 512 * 1024) continue;

                    var lower = fi.FullName.ToLowerInvariant();
                    if (lower.Contains(@"\temp\") || lower.Contains(@"\tmp\")) continue;
                    if (IsSystemFile(lower)) continue;

                    if (fi.LastAccessTime < threshold)
                    {
                        bool isExe = fi.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
                        result.Add(new SystemFileItem
                        {
                            FullPath = fi.FullName,
                            Name = fi.Name,
                            SizeBytes = fi.Length,
                            LastAccessTime = fi.LastAccessTime,
                            Reason = isExe
                                ? $"Приложение не запускалось > {days} дн."
                                : $"Файл не открывался > {days} дн.",
                            Recommendation = "Проверить/Удалить",
                            Source = isExe ? "Приложение" : "Файл"
                        });
                    }
                }
                catch { }
            }

            foreach (var sub in subdirs)
            {
                try
                {
                    var di = new DirectoryInfo(sub);
                    if (IsSystemFolder(di.FullName)) continue;

                    DateTime last = di.LastAccessTime;
                    long size = 0;
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var ffi = new FileInfo(f);
                                size += ffi.Length;
                                if (ffi.LastAccessTime > last) last = ffi.LastAccessTime;
                            }
                            catch { }
                        }
                    }
                    catch { }

                    if (size > 5L * 1024 * 1024 && last < threshold)
                    {
                        result.Add(new SystemFileItem
                        {
                            FullPath = di.FullName,
                            Name = di.Name + " (папка)",
                            SizeBytes = size,
                            LastAccessTime = last,
                            Reason = $"Папка не использовалась > {days} дн.",
                            Recommendation = "Проверить/Удалить",
                            Source = "Папка"
                        });
                    }
                    else
                    {
                        ScanFolder(sub, threshold, days, result, depth + 1, maxDepth);
                    }
                }
                catch { }
            }
        }

        private static bool IsSystemFolder(string path)
        {
            var p = path.ToLowerInvariant();
            return p.Contains(@"\windows\")
                || p.Contains(@"\program files\")
                || p.Contains(@"\program files (x86)\")
                || p.Contains(@"\programdata\")
                || p.Contains(@"\appdata\")
                || p.Contains(@"\$recycle.bin")
                || p.Contains(@"\system volume information")
                || p.Contains(@"\node_modules")
                || p.Contains(@"\.git\")
                || p.Contains(@"\.vs\")
                || p.Contains(@"\bin\debug")
                || p.Contains(@"\bin\release")
                || p.Contains(@"\obj\");
        }

        private static bool IsSystemFile(string lower)
        {
            return lower.EndsWith(".sys") || lower.EndsWith(".dll") || lower.EndsWith(".log");
        }
    }
}