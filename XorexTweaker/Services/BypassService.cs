using System.Diagnostics;
using System.IO;

namespace XorexTweaker.Services;

public class BypassService
{
    private Process? _process;

    public bool IsRunning => Process.GetProcessesByName("winws").Length > 0;

    public string ToolsDir => Path.Combine(AppContext.BaseDirectory, "Tools", "zapret");
    public string WinwsPath => Path.Combine(ToolsDir, "winws.exe");

    public bool IsInstalled => File.Exists(WinwsPath);

    /// <summary>
    /// Возвращает список доступных .bat-пресетов из папки zapret.
    /// </summary>
    public IEnumerable<string> GetPresets()
    {
        if (!Directory.Exists(ToolsDir)) return Enumerable.Empty<string>();
        return Directory.GetFiles(ToolsDir, "*.bat")
            .Select(Path.GetFileName)
            .Where(x => x != null)
            .OrderBy(x => x)
            .Select(x => x!);
    }

    /// <summary>
    /// Запускает выбранный .bat-пресет.
    /// </summary>
    public void Start(string batFileName)
    {
        if (IsRunning) return;
        if (!IsInstalled) throw new FileNotFoundException("winws.exe не найден", WinwsPath);

        var batPath = Path.Combine(ToolsDir, batFileName);
        if (!File.Exists(batPath))
            throw new FileNotFoundException($"Пресет не найден: {batPath}");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            WorkingDirectory = ToolsDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _process = Process.Start(psi);
    }

    /// <summary>
    /// Останавливает zapret: убивает cmd и все winws.exe.
    /// </summary>
    public void Stop()
    {
        if (_process != null)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
            _process = null;
        }

        foreach (var p in Process.GetProcessesByName("winws"))
        {
            try { p.Kill(); } catch { }
        }
    }
}