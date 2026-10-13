using System.Diagnostics;
using System.IO;

namespace XorexTweaker.Services;

/// <summary>
/// Определяет, где хранить данные приложения и откуда читать встроенные ресурсы.
/// Работает и в single-file publish, и при обычной сборке.
/// 
/// Portable активируется, если рядом с .exe есть файл "portable.txt".
/// Иначе данные хранятся в %AppData%\XorexTweaker\.
/// </summary>
public static class PathService
{
    /// <summary>
    /// Возвращает папку, где реально лежит .exe.
    /// В single-file режиме AppContext.BaseDirectory = temp, поэтому используем MainModule.
    /// </summary>
    public static string GetExeDirectory()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                return Path.GetDirectoryName(exePath)!;
        }
        catch { }

        // Fallback — для обычной сборки это правильная директория
        return AppContext.BaseDirectory;
    }

    public static bool IsPortable
    {
        get
        {
            var marker = Path.Combine(GetExeDirectory(), "portable.txt");
            return File.Exists(marker);
        }
    }

    public static string GetDataDir()
    {
        string dir;

        if (IsPortable)
        {
            dir = Path.Combine(GetExeDirectory(), "UserData");
        }
        else
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XorexTweaker");
        }

        Directory.CreateDirectory(dir);
        return dir;
    }
}