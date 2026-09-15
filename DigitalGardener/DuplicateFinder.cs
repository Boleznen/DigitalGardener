using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DigitalGardener
{
    public static class DuplicateFinder
    {
        public static async Task<List<DuplicateItem>> FindDuplicatesAsync(
            List<string> paths,
            IProgress<int>? progress = null)
        {
            // Шаг 1: собираем файлы и группируем по размеру
            var bySize = new Dictionary<long, List<string>>();

            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    if (!Directory.Exists(path)) continue;
                    foreach (var file in SafeEnumerateFiles(path))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length == 0) continue;
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
            });

            progress?.Report(20);

            // Шаг 2: хешируем только файлы с одинаковым размером
            var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var candidates = bySize.Values.Where(g => g.Count > 1).SelectMany(g => g).ToList();
            int total = candidates.Count;
            int done = 0;

            await Task.Run(() =>
            {
                foreach (var file in candidates)
                {
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
            });

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
                string[] files = Array.Empty<string>();
                string[] subdirs = Array.Empty<string>();
                try { files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly); } catch { }
                try { subdirs = Directory.GetDirectories(dir); } catch { }

                foreach (var f in files) yield return f;
                foreach (var d in subdirs) stack.Push(d);
            }
        }
    }
}