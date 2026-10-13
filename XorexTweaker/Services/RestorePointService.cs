using System.Diagnostics;

namespace XorexTweaker.Services;

public class RestorePointService
{
    /// <summary>
    /// Создаёт точку восстановления Windows.
    /// Требует включённой защиты системы — если она выключена, включает её автоматически.
    /// </summary>
    public async Task<(bool ok, string message)> CreateAsync(string description)
    {
        try
        {
            // 1. Проверяем/включаем System Protection на C:
            var enableCmd = "Enable-ComputerRestore -Drive 'C:\\' -ErrorAction SilentlyContinue";
            await RunPowerShellAsync(enableCmd);

            // 2. Создаём точку восстановления
            var safe = description.Replace("'", "''");
            var checkpointCmd = $"Checkpoint-Computer -Description '{safe}' -RestorePointType 'MODIFY_SETTINGS'";
            var (ok, output) = await RunPowerShellAsync(checkpointCmd);

            if (ok) return (true, "Точка восстановления создана.");
            return (false, "Не удалось создать точку.\n" + output);
        }
        catch (Exception ex)
        {
            return (false, "Ошибка: " + ex.Message);
        }
    }

    private static async Task<(bool ok, string output)> RunPowerShellAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        var output = (stdout + "\n" + stderr).Trim();
        return (p.ExitCode == 0, output);
    }
}