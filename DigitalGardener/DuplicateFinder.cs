using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalGardener
{
    public static class DuplicateFinder
    {
        private const long MinFileSizeBytes = 1024 * 1024; // 1 МБ

        public static async Task<List<DuplicateItem>> FindDuplicatesAsync(
            List<string> paths,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var bySize = new Dictionary<long, List<string>>();

            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    if (!Directory.Exists(path)) continue;
                    foreach (var file in SafeEnumerateFiles(path))
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length < MinFileSizeBytes) continue;
                            if (!bySize.TryGetValue(fi.Length, out var list))
                            {
                                list = new List<string>();
                                bySize[fi.Length] = list;
                            }
                            list.Add(file);
                        }
                        catch { }
                    }
                }
            }, cancellationToken);

            progress?.Report(20);

            var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var candidates = bySize.Values.Where(g => g.Count > 1).SelectMany(g => g).ToList();
            int total = candidates.Count;
            int done = 0;

            await Task.Run(() =>
            {
                foreach (var file in candidates)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    try
                    {
                        string hash = ComputeHash(file);
                        if (string.IsNullOrEmpty(hash)) continue;

                        if (!byHash.TryGetValue(hash, out var list))
                        {
                            list = new List<string>();
                            byHash[hash] = list;
                        }
                        list.Add(file);
                    }
                    catch { }
                    done++;
                    if (total > 0)
                        progress?.Report(20 + (int)(70.0 * done / total));
                }
            }, cancellationToken);

            progress?.Report(95);

            var duplicates = new List<DuplicateItem>();
            int groupId = 0;
            foreach (var group in byHash.Values.Where(g => g.Count > 1))
            {
                groupId++;
                string originalPath = group.First();
                foreach (var dupPath in group.Skip(1))
                {
                    try
                    {
                        var fi = new FileInfo(dupPath);
                        duplicates.Add(new DuplicateItem
                        {
                            FullPath = dupPath,
                            Name = fi.Name,
                            SizeBytes = fi.Length,
                            OriginalPath = originalPath,
                            GroupId = groupId,
                            IsSelected = false
                        });
                    }
                    catch { }
                }
            }

            progress?.Report(100);
            return duplicates;
        }

        private static string ComputeHash(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite | FileShare.Delete);
                var hashBytes = sha256.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch { return string.Empty; }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                if (IsSystemFolder(dir)) continue;

                string[] files = Array.Empty<string>();
                string[] subdirs = Array.Empty<string>();
                try { files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly); } catch { }
                try { subdirs = Directory.GetDirectories(dir); } catch { }

                foreach (var f in files) yield return f;
                foreach (var d in subdirs) stack.Push(d);
            }
        }

        private static bool IsSystemFolder(string path)
        {
            var p = path.ToLowerInvariant();
            return p.Contains(@"\windows\")
                || p.Contains(@"\program files\")
                || p.Contains(@"\program files (x86)\")
                || p.Contains(@"\programdata\")
                || p.Contains(@"\$recycle.bin")
                || p.Contains(@"\system volume information")
                || p.Contains(@"\node_modules\")
                || p.Contains(@"\.git\")
                || p.Contains(@"\.vs\")
                || p.Contains(@"\bin\debug")
                || p.Contains(@"\bin\release")
                || p.Contains(@"\obj\");
        }
    }
}