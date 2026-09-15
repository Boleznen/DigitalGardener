using System;
using System.Collections.Generic;
using System.IO;

namespace DigitalGardener
{
    public static class GameCacheCleaner
    {
        public static List<SystemFileItem> Scan()
        {
            var result = new List<SystemFileItem>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            Add(result, Path.Combine(local, "D3DSCache"), "DirectX Shader Cache");
            Add(result, Path.Combine(local, "NVIDIA", "DXCache"), "NVIDIA DXCache");
            Add(result, Path.Combine(local, "NVIDIA", "GLCache"), "NVIDIA GLCache");
            Add(result, Path.Combine(local, "NVIDIA Corporation", "NV_Cache"), "NVIDIA NV_Cache");
            Add(result, Path.Combine(local, "AMD", "DxCache"), "AMD DxCache");
            Add(result, Path.Combine(local, "AMD", "DxcCache"), "AMD DxcCache");
            Add(result, Path.Combine(local, "AMD", "GLCache"), "AMD GLCache");
            Add(result, Path.Combine(local, "Intel", "ShaderCache"), "Intel ShaderCache");
            Add(result, Path.Combine(local, "Microsoft", "DirectX Shader Cache"), "DirectX Shader Cache (MS)");

            FindSteamCaches(result);

            Add(result, Path.Combine(local, "EpicGamesLauncher", "Saved", "webcache"), "Epic webcache");
            Add(result, Path.Combine(local, "EpicGamesLauncher", "Saved", "Logs"), "Epic Logs");
            Add(result, Path.Combine(local, "Battle.net", "Cache"), "Battle.net Cache");
            Add(result, Path.Combine(appData, "Battle.net", "Cache"), "Battle.net Cache (Roaming)");
            Add(result, Path.Combine(local, "Riot Games", "Riot Client", "Cache"), "Riot Cache");
            Add(result, Path.Combine(local, "Ubisoft Game Launcher", "cache"), "Ubisoft Cache");
            Add(result, Path.Combine(local, "Origin", "DownloadCache"), "EA Origin Cache");

            return result;
        }

        private static void FindSteamCaches(List<SystemFileItem> result)
        {
            var candidates = new List<string>();

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var path = key?.GetValue("SteamPath")?.ToString();
                if (!string.IsNullOrEmpty(path)) candidates.Add(path);
            }
            catch { }

            candidates.Add(@"C:\Program Files (x86)\Steam");
            candidates.Add(@"C:\Program Files\Steam");
            candidates.Add(@"D:\Steam");
            candidates.Add(@"E:\Steam");

            foreach (var steamDir in candidates)
            {
                if (!Directory.Exists(steamDir)) continue;

                Add(result, Path.Combine(steamDir, "steamapps", "shadercache"), "Steam Shader Cache");
                Add(result, Path.Combine(steamDir, "appcache"), "Steam App Cache");
                Add(result, Path.Combine(steamDir, "config", "htmlcache"), "Steam HTML Cache");
                Add(result, Path.Combine(steamDir, "depotcache"), "Steam Depot Cache");
                break;
            }
        }

        private static void Add(List<SystemFileItem> list, string dir, string source)
        {
            if (!Directory.Exists(dir)) return;

            long totalSize = 0;
            DateTime lastWrite = DateTime.MinValue;
            int fileCount = 0;

            foreach (var file in SafeEnumerate(dir))
            {
                try
                {
                    var fi = new FileInfo(file);
                    totalSize += fi.Length;
                    if (fi.LastWriteTime > lastWrite) lastWrite = fi.LastWriteTime;
                    fileCount++;
                }
                catch { }
            }

            if (fileCount == 0 || totalSize == 0) return;

            list.Add(new SystemFileItem
            {
                FullPath = dir,
                Name = source,
                SizeBytes = totalSize,
                LastAccessTime = lastWrite == DateTime.MinValue ? DateTime.Now : lastWrite,
                Reason = $"{fileCount} файлов",
                Recommendation = "Очистить",
                Source = source
            });
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

        public static (int ok, int failed, long freedBytes) CleanCache(SystemFileItem item)
        {
            int ok = 0, failed = 0;
            long freed = 0;

            if (!Directory.Exists(item.FullPath)) return (0, 0, 0);

            long beforeSize = 0;
            try
            {
                foreach (var f in SafeEnumerate(item.FullPath))
                {
                    try { beforeSize += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }

            try
            {
                foreach (var file in Directory.GetFiles(item.FullPath, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); ok++; } catch { failed++; }
                }
                foreach (var subdir in Directory.GetDirectories(item.FullPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (Directory.GetFiles(subdir).Length == 0 &&
                            Directory.GetDirectories(subdir).Length == 0)
                            Directory.Delete(subdir);
                    }
                    catch { }
                }
            }
            catch { }

            freed = beforeSize;
            return (ok, failed, freed);
        }
    }
}