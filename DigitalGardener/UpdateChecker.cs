using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DigitalGardener
{
    public static class UpdateChecker
    {
        private const string GitHubApiUrl =
            "https://api.github.com/repos/Boleznen/DigitalGardener/releases/latest";

        public static string CurrentVersion => "1.2.0";

        public class UpdateInfo
        {
            public bool HasUpdate { get; set; }
            public string LatestVersion { get; set; } = "";
            public string ReleaseNotes { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string HtmlUrl { get; set; } = "";
        }

        public static async Task<UpdateInfo> CheckAsync()
        {   
            var result = new UpdateInfo();

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "DigitalGardener-UpdateChecker");
                http.Timeout = TimeSpan.FromSeconds(10);

                var response = await http.GetStringAsync(GitHubApiUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? "";
                string latest = tag.TrimStart('v', 'V');

                result.LatestVersion = latest;
                result.HtmlUrl = root.GetProperty("html_url").GetString() ?? "";
                result.ReleaseNotes = root.TryGetProperty("body", out var body)
                    ? body.GetString() ?? ""
                    : "";

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            result.DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                result.HasUpdate = IsNewer(latest, CurrentVersion);
            }
            catch (Exception ex)
            {
                LoggerService.LogError("Ошибка проверки обновлений",
                    nameof(CheckAsync), ex);
            }

            return result;
        }

        private static bool IsNewer(string latest, string current)
        {
            try
            {
                var l = Version.Parse(latest);
                var c = Version.Parse(current);
                return l > c;
            }
            catch
            {
                return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}