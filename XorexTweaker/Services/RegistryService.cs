using Microsoft.Win32;

namespace XorexTweaker.Services;

public class RegistryService
{
    public bool SetValue(string path, string name, object? value)
    {
        try
        {
            var (hive, sub) = ParsePath(path);
            using var key = hive.CreateSubKey(sub, true);
            if (key == null) return false;
            if (value == null) key.DeleteValue(name, false);
            else key.SetValue(name, value);
            return true;
        }
        catch { return false; }
    }

    public object? GetValue(string path, string name)
    {
        try
        {
            var (hive, sub) = ParsePath(path);
            using var key = hive.OpenSubKey(sub);
            return key?.GetValue(name);
        }
        catch { return null; }
    }

    private (RegistryKey hive, string sub) ParsePath(string path)
    {
        var parts = path.Split('\\', 2);
        var hive = parts[0].ToUpper() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER"  => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT"  => Registry.ClassesRoot,
            "HKU"  or "HKEY_USERS"         => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG"=> Registry.CurrentConfig,
            _ => throw new ArgumentException($"Неизвестный hive: {parts[0]}")
        };
        return (hive, parts.Length > 1 ? parts[1] : "");
    }

    public static object? ConvertValue(string? value, string valueType)
    {
        if (value == null) return null;
        return valueType switch
        {
            "DWord" => int.TryParse(value, out var i) ? i : 0,
            "QWord" => long.TryParse(value, out var l) ? l : 0L,
            "String" or "ExpandString" => value,
            _ => value
        };
    }
}