using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace XorexTweaker.Services;

public class UpdateInfo
{
    public Version LatestVersion { get; set; } = new(0, 0, 0);
    public Version CurrentVersion { get; set; } = new(0, 0, 0);
    public string TagName { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public bool IsNewer => LatestVersion > CurrentVersion;
}

public class UpdateService
{
    // ⚠️ ВАЖНО: поменяй на свой GitHub-репозиторий, когда создашь его
    private const string Owner = "XorexMod";
    private const string Repo  = "XorexTweaker";

    private static readonly string ApiUrl =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    public Version GetCurrentVersion()
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("XorexTweaker");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            http.Timeout = TimeSpan.FromSeconds(10);

            using var response = await http.GetAsync(ApiUrl, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t)
                ? t.GetString() ?? "0.0.0"
                : "0.0.0";

            var versionStr = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionStr, out var latest))
                latest = new Version(0, 0, 0);

            return new UpdateInfo
            {
                LatestVersion  = latest,
                CurrentVersion = GetCurrentVersion(),
                TagName        = tag,
                ReleaseName    = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                HtmlUrl        = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "",
                Body           = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                PublishedAt    = root.TryGetProperty("published_at", out var p)
                                 && DateTime.TryParse(p.GetString(), out var dt)
                                 ? dt : DateTime.MinValue
            };
        }
        catch
        {
            return null;
        }
    }
}