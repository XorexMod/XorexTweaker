namespace XorexTweaker.Models;

public enum TweakType { Registry, Command, Service }

public class Tweak
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public TweakType Type { get; set; } = TweakType.Registry;

    public string? RegistryPath { get; set; }
    public string? RegistryValue { get; set; }
    public string? ValueType { get; set; } = "DWord";
    public string? OnValue { get; set; }
    public string? OffValue { get; set; }

    public string? OnCommand { get; set; }
    public string? OffCommand { get; set; }

    public bool RequiresReboot { get; set; }
    public bool Risky { get; set; }

    public bool IsEnabled { get; set; }
}

public class TweakCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "⚙";
    public List<Tweak> Tweaks { get; set; } = new();
}

public class TweakConfig
{
    public List<TweakCategory> Categories { get; set; } = new();
}