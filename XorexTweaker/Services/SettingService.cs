using System.IO;
using System.Text.Json;
using XorexTweaker.Models;

namespace XorexTweaker.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    private readonly string _path;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        var dir = PathService.GetDataDir();
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
        }
        catch { Current = new(); }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, Opt));
        }
        catch { }
    }

    public void Reset()
    {
        Current = new AppSettings();
        Save();
    }
}