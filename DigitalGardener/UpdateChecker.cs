using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace DigitalGardener
{
    public static class UpdateChecker
    {
        private const string GitHubOwner = "Boleznen";
        private const string GitHubRepo = "DigitalGardener";

        private const string GitHubApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        public const string ReleasesPageUrl =
            "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases";

        /// <summary>
        /// Текущая версия приложения. Читается автоматически из AssemblyVersion,
        /// которая задаётся в .csproj в теге Version.
        /// </summary>
        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    if (v != null)
                    {
                        if (v.Build >= 0 && v.Revision > 0)
                            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                        if (v.Build >= 0)
                            return $"{v.Major}.{v.Minor}.{v.Build}";
                        return $"{v.Major}.{v.Minor}";
                    }
                }
                catch { }
                return "0.0.0";
            }
        }

        public class UpdateInfo
        {
            public bool HasUpdate { get; set; }
            public bool CheckFailed { get; set; }
            public string LatestVersion { get; set; } = "";
            public string ReleaseNotes { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string HtmlUrl { get; set; } = ReleasesPageUrl;
        }

        public static async Task<UpdateInfo> CheckAsync()
        {
            var result = new UpdateInfo
            {
                HtmlUrl = ReleasesPageUrl
            };

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "DigitalGardener-UpdateChecker");
                http.Timeout = TimeSpan.FromSeconds(10);

                var response = await http.GetStringAsync(GitHubApiUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string tag = root.TryGetProperty("tag_name", out var tagProp)
                    ? tagProp.GetString() ?? ""
                    : "";

                string latest = tag.TrimStart('v', 'V');

                if (string.IsNullOrEmpty(latest))
                {
                    result.CheckFailed = true;
                    return result;
                }

                result.LatestVersion = latest;
                result.HtmlUrl = root.TryGetProperty("html_url", out var htmlProp)
                    ? htmlProp.GetString() ?? ReleasesPageUrl
                    : ReleasesPageUrl;

                result.ReleaseNotes = root.TryGetProperty("body", out var body)
                    ? body.GetString() ?? ""
                    : "";

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n)
                            ? n.GetString() ?? ""
                            : "";

                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            result.DownloadUrl = asset.TryGetProperty("browser_download_url", out var d)
                                ? d.GetString() ?? ""
                                : "";
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
                result.CheckFailed = true;
            }

            return result;
        }

        private static bool IsNewer(string latest, string current)
        {
            try
            {
                var l = NormalizeVersion(latest);
                var c = NormalizeVersion(current);

                var lv = Version.Parse(l);
                var cv = Version.Parse(c);
                return lv > cv;
            }
            catch
            {
                return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeVersion(string v)
        {
            int dotCount = v.Count(c => c == '.');
            while (dotCount < 2)
            {
                v += ".0";
                dotCount++;
            }
            return v;
        }
    }
}