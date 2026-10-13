using System.IO;
using System.Text.Json;

namespace XorexTweaker.Services;

public class TweakProfile
{
    public string Name { get; set; } = "Профиль";
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<string> EnabledTweaks { get; set; } = new();
}

public class ProfileService
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public void Export(string path, string name, IEnumerable<string> enabledIds)
    {
        var profile = new TweakProfile
        {
            Name = name,
            EnabledTweaks = enabledIds.ToList()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(profile, Opt));
    }

    public TweakProfile Import(string path)
    {
        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<TweakProfile>(json, Opt);
        if (profile == null) throw new InvalidDataException("Пустой файл профиля");
        return profile;
    }
}