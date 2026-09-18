using System;
using System.Collections.Generic;
using System.IO;

namespace DigitalGardener
{
    /// <summary>
    /// Ищет «забытые» архивы и установщики на дисках.
    /// Использует LastWriteTime — дата изменения файла, которая не обновляется при чтении.
    /// </summary>
    public static class ArchiveFinder
    {
        public static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz",
            ".iso", ".img", ".cab", ".wim", ".vhd", ".vmdk"
        };

        public static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".msix", ".appx"
        };

        /// <summary>
        /// Найти архивы и установщики, которые не изменялись > days дней.
        /// minSizeMb = 0 — без ограничения по размеру.
        /// </summary>
        public static List<SystemFileItem> Scan(
            IEnumerable<string> roots,
            int days,
            int minSizeMb)
        {
            var result = new List<SystemFileItem>();
            var threshold = DateTime.Now.AddDays(-days);
            long minBytes = (long)minSizeMb * 1024 * 1024;

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                ScanFolder(root, threshold, minBytes, days, result, depth: 0, maxDepth: 6);
            }

            return result;
        }

        private static void ScanFolder(
            string dir,
            DateTime threshold,
            long minBytes,
            int days,
            List<SystemFileItem> result,
            int depth,
            int maxDepth)
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

                    if (minBytes > 0 && fi.Length < minBytes) continue;

                    string ext = fi.Extension.ToLowerInvariant();
                    bool isArchive = ArchiveExtensions.Contains(ext);
                    bool isInstaller = InstallerExtensions.Contains(ext);

                    if (!isArchive && !isInstaller) continue;

                    // LastWriteTime — не обновляется при чтении, в отличие от LastAccessTime
                    DateTime lastUsed = fi.LastWriteTime;

                    if (lastUsed >= threshold) continue;

                    if (isInstaller)
                    {
                        var lower = fi.FullName.ToLowerInvariant();
                        if (lower.Contains(@"\program files\") ||
                            lower.Contains(@"\program files (x86)\") ||
                            lower.Contains(@"\windows\") ||
                            lower.Contains(@"\appdata\") ||
                            lower.Contains(@"\microsoft\") ||
                            lower.Contains(@"\windowsapps\"))
                            continue;
                    }

                    result.Add(new SystemFileItem
                    {
                        FullPath = fi.FullName,
                        Name = fi.Name,
                        SizeBytes = fi.Length,
                        LastAccessTime = lastUsed,
                        Reason = isArchive
                            ? $"Архив не изменялся > {days} дн."
                            : $"Установщик не изменялся > {days} дн.",
                        Recommendation = "Удалить или переместить",
                        Source = isArchive ? "Архив" : "Установщик"
                    });
                }
                catch { }
            }

            foreach (var sub in subdirs)
            {
                try
                {
                    if (IsSystemFolder(sub)) continue;
                    ScanFolder(sub, threshold, minBytes, days, result, depth + 1, maxDepth);
                }
                catch { }
            }
        }

        private static bool IsSystemFolder(string path)
        {
            var p = path.ToLowerInvariant();
            return p.EndsWith(@"\windows")
                || p.Contains(@"\windows\system32")
                || p.Contains(@"\windows\syswow64")
                || p.Contains(@"\windows\winsxs")
                || p.Contains(@"\program files")
                || p.Contains(@"\program files (x86)")
                || p.Contains(@"\programdata")
                || p.Contains(@"\$recycle.bin")
                || p.Contains(@"\system volume information")
                || p.Contains(@"\node_modules\")
                || p.Contains(@"\.git\")
                || p.Contains(@"\.vs\")
                || p.Contains(@"\bin\debug")
                || p.Contains(@"\bin\release")
                || p.Contains(@"\obj\")
                || p.Contains(@"\windowsapps");
        }
    }
}