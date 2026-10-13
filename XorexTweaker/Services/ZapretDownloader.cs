using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace XorexTweaker.Services;

public class ZapretDownloader
{
    private const string ApiUrl =
        "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

    public async Task DownloadLatestAsync(string targetDir, IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("XorexTweaker/1.0");

        log?.Report("Запрашиваю последний релиз с GitHub...");
        var json = await http.GetStringAsync(ApiUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "unknown";
        log?.Report($"Последний релиз: {tag}");

        var assets = root.GetProperty("assets");
        string? downloadUrl = null;
        string? assetName = null;

        foreach (var a in assets.EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = a.GetProperty("browser_download_url").GetString();
                assetName = name;
                break;
            }
        }

        if (downloadUrl == null || assetName == null)
            throw new InvalidOperationException("В релизе не найден .zip-архив.");

        log?.Report($"Скачиваю {assetName}...");
        var tempZip = Path.Combine(Path.GetTempPath(), assetName);
        using (var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(tempZip);
            await resp.Content.CopyToAsync(fs, ct);
        }

        log?.Report("Распаковываю...");
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, true);
        Directory.CreateDirectory(targetDir);
        ZipFile.ExtractToDirectory(tempZip, targetDir, overwriteFiles: true);

        try { File.Delete(tempZip); } catch { }

        log?.Report($"✓ Zapret {tag} установлен в {targetDir}");
    }
}