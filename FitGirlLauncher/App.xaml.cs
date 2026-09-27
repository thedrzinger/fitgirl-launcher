using System.IO;
using System.Windows;
using FitGirlLauncher.Services;
using Velopack;

namespace FitGirlLauncher;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Custom entry point. Velopack must run before WPF starts: it intercepts
    /// the install/update/uninstall startup arguments, and when this launch is
    /// part of an in-progress update cycle it simply exits without showing the app.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A silent death is undiagnosable — log any unhandled exception so the
        // next "the app just closed" has an answer.
        DispatcherUnhandledException += (_, args) => LogCrash("UI thread", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => LogCrash("unobserved task", args.Exception);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FitGirlLauncher");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"\n{DateTime.UtcNow:O} [{source}]\n{ex}\n");
        }
        catch { }
    }
}
