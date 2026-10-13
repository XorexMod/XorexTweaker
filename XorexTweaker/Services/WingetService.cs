using System.Diagnostics;

namespace XorexTweaker.Services;

public class WingetService
{
    public Task<bool> InstallAsync(string id, IProgress<string>? log = null)
        => RunAsync(new[]
        {
            "install", "--id", id, "--silent",
            "--accept-package-agreements", "--accept-source-agreements"
        }, log);

    public Task<bool> UninstallAsync(string id, IProgress<string>? log = null)
        => RunAsync(new[] { "uninstall", "--id", id, "--silent" }, log);

    /// <summary>
    /// Проверяет, установлен ли пакет с указанным ID через winget list.
    /// </summary>
    public async Task<bool> IsInstalledAsync(string id)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            psi.ArgumentList.Add("list");
            psi.ArgumentList.Add("--id");
            psi.ArgumentList.Add(id);
            psi.ArgumentList.Add("--exact");
            psi.ArgumentList.Add("--accept-source-agreements");

            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();

            return output.Contains(id, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private async Task<bool> RunAsync(IEnumerable<string> args, IProgress<string>? log)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) log?.Report(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) log?.Report(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();
        return p.ExitCode == 0;
    }
}