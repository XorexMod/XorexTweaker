using System.IO;
using System.Reflection;

namespace XorexTweaker.Services;

/// <summary>
/// Читает JSON-данные: сначала пробует внешний файл (рядом с .exe),
/// если его нет — читает встроенный ресурс из .exe.
/// 
/// Это даёт возможность:
/// 1. Запустить один .exe без каких-либо файлов рядом.
/// 2. При необходимости подменить tweaks.json на кастомный.
/// </summary>
public static class ResourceReader
{
    public static string ReadText(string relativePath)
    {
        // 1. Внешний файл рядом с .exe
        var externalPath = Path.Combine(PathService.GetExeDirectory(), relativePath);
        if (File.Exists(externalPath))
            return File.ReadAllText(externalPath);

        // 2. Внешний файл рядом с приложением (для dotnet run)
        var basePath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(basePath))
            return File.ReadAllText(basePath);

        // 3. Встроенный ресурс
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = "XorexTweaker." + relativePath.Replace('/', '.').Replace('\\', '.');

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            var available = string.Join(", ", asm.GetManifestResourceNames());
            throw new FileNotFoundException(
                $"Ресурс '{resourceName}' не найден в сборке.\n" +
                $"Доступные ресурсы: {available}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static bool Exists(string relativePath)
    {
        var externalPath = Path.Combine(PathService.GetExeDirectory(), relativePath);
        if (File.Exists(externalPath)) return true;

        var basePath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(basePath)) return true;

        var asm = Assembly.GetExecutingAssembly();
        var resourceName = "XorexTweaker." + relativePath.Replace('/', '.').Replace('\\', '.');
        return asm.GetManifestResourceStream(resourceName) != null;
    }
}