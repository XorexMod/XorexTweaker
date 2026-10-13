using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace XorexTweaker;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) => Log("Task", e.Exception);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("UI", e.Exception);
        MessageBox.Show(e.Exception.ToString(), "XorexTweaker — ошибка");
        e.Handled = true;
        Shutdown(1);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log("Domain", e.ExceptionObject as Exception);
    }

    private static void Log(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XorexTweaker");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "crash.log");
            File.AppendAllText(file,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n");
        }
        catch { }
    }
}