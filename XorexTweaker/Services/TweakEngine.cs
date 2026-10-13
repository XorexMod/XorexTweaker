using System.Diagnostics;
using XorexTweaker.Models;

namespace XorexTweaker.Services;

public class TweakEngine
{
    private readonly RegistryService _reg = new();
    private readonly BackupService _backup = new();
    public BackupService Backup => _backup;

    public async Task<bool> ApplyAsync(Tweak tweak, bool enable, CancellationToken ct = default)
    {
        try
        {
            return tweak.Type switch
            {
                TweakType.Registry => await Task.Run(() => ApplyRegistry(tweak, enable), ct),
                TweakType.Command  => await RunShellAsync(enable ? tweak.OnCommand : tweak.OffCommand, ct),
                _ => false
            };
        }
        catch { return false; }
    }

    private bool ApplyRegistry(Tweak t, bool enable)
    {
        if (string.IsNullOrWhiteSpace(t.RegistryPath) || string.IsNullOrWhiteSpace(t.RegistryValue))
            return false;

        _backup.BackupRegistry(t.Id, t.RegistryPath!, t.RegistryValue!);

        var raw = enable ? t.OnValue : t.OffValue;
        var value = RegistryService.ConvertValue(raw, t.ValueType ?? "DWord");
        return _reg.SetValue(t.RegistryPath!, t.RegistryValue!, value);
    }

    private async Task<bool> RunShellAsync(string? command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi)!;
        try
        {
            await p.WaitForExitAsync(ct);
            return p.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return false;
        }
    }

    public bool IsTweakEnabled(Tweak t)
    {
        if (t.Type != TweakType.Registry) return false;
        if (string.IsNullOrWhiteSpace(t.RegistryPath) || string.IsNullOrWhiteSpace(t.RegistryValue)) return false;
        var current = _reg.GetValue(t.RegistryPath!, t.RegistryValue!);
        if (current == null) return false;
        var target = RegistryService.ConvertValue(t.OnValue, t.ValueType ?? "DWord");
        return current.ToString() == target?.ToString();
    }
}