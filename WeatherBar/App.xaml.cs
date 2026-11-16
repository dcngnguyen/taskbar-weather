using System.Windows;
using Microsoft.Win32;

namespace WeatherBar;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Only one Weather Bar at a time (it also starts with Windows).
        _singleInstance = new Mutex(true, @"Local\WeatherBar.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unhandled error: " + args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("Unobserved task error: " + args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        RegisterStartWithWindows();
        new BarWindow().Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Adds Weather Bar to "start when I sign in" (turn off any time in Task Manager, Startup apps).</summary>
    private static void RegisterStartWithWindows()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            key.SetValue("WeatherBar", $"\"{exe}\"");
        }
        catch (Exception ex)
        {
            Log.Write("Could not set start with Windows: " + ex.Message);
        }
    }
}
