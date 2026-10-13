using System.IO;
using System.Text.Json;

namespace XorexTweaker.Services;

public class RegistryBackup
{
    public string TweakId { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public string? Value { get; set; }
    public string ValueType { get; set; } = "DWord";
    public DateTime Timestamp { get; set; }
}

public class BackupService
{
    private readonly string _dir;
    private readonly RegistryService _reg = new();

    public BackupService()
    {
        _dir = Path.Combine(PathService.GetDataDir(), "Backups");
        Directory.CreateDirectory(_dir);
    }

    public string BackupDir => _dir;

    public void BackupRegistry(string tweakId, string path, string name)
    {
        var existing = _reg.GetValue(path, name);
        var backup = new RegistryBackup
        {
            TweakId = tweakId,
            Path = path,
            Name = name,
            Existed = existing != null,
            Value = existing?.ToString(),
            ValueType = existing switch
            {
                int => "DWord",
                long => "QWord",
                _ => "String"
            },
            Timestamp = DateTime.UtcNow
        };
        var file = Path.Combine(_dir, $"{tweakId}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
    }

    public int RestoreAll()
    {
        var files = Directory.GetFiles(_dir, "*.json");
        var restored = 0;
        foreach (var file in files)
        {
            try
            {
                var b = JsonSerializer.Deserialize<RegistryBackup>(File.ReadAllText(file));
                if (b == null) continue;
                var value = b.Existed ? RegistryService.ConvertValue(b.Value, b.ValueType) : null;
                if (_reg.SetValue(b.Path, b.Name, value)) restored++;
            }
            catch { }
        }
        return restored;
    }
}