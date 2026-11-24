using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace PokeLobby.LauncherUi;

public partial class App : Application
{
    private static readonly string RutaLog = Path.Combine(RutasLauncher.Base, "crash.log");

    [STAThread]
    private static void Main(string[] args)
    {
        // Debe ser lo primero: gestiona instalación, actualización y reinicio.
        // vpk comprueba que esta llamada exista al empaquetar.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            EscribirCrash("Dispatcher", e.Exception);
            MessageBox.Show($"Error interno: {e.Exception.Message}", "PokeLocke Launcher",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            EscribirCrash("AppDomain", e.ExceptionObject as Exception ?? new Exception("desconocida"));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            EscribirCrash("Task", e.Exception);
            e.SetObserved();
        };
    }

    private static void EscribirCrash(string origen, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaLog)!);
            File.AppendAllText(RutaLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({origen}) {ex.GetType().Name}: {ex.Message}\n{ex}\n\n");
        }
        catch { }
    }
}
