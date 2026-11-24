using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PokeLobby.LauncherUi.Uninstaller;

/// <summary>
/// Desinstalador de PokeLocke Launcher.
/// Borra todo lo relacionado con el launcher:
/// 1) La carpeta de datos de usuario: %APPDATA%\PokeLockeLauncher\ (biblioteca, carátulas,
///    huellas, ajustes y sesión).
/// 2) La carpeta de instalación del launcher (donde esté este ejecutable), tanto para la
///    versión instalada como para la versión portable (ZIP).
/// No borra ROMs, partidas .sav ni emuladores de terceros.
/// </summary>
internal static class Program
{
    private const string CarpetaDatos = "PokeLockeLauncher";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("========================================");
        Console.WriteLine("  PokeLocke Launcher - Desinstalador");
        Console.WriteLine("========================================");
        Console.WriteLine();

        // ── 1) Borrar carpeta de datos de usuario (%APPDATA%) ──
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var carpetaDatos = Path.Combine(roaming, CarpetaDatos);

        if (Directory.Exists(carpetaDatos))
        {
            Console.WriteLine($"Borrando datos de usuario: {carpetaDatos}");
            try
            {
                Directory.Delete(carpetaDatos, recursive: true);
                Console.WriteLine("  ✔ Datos de usuario borrados.");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("  ✗ Sin permisos para borrar los datos de usuario.");
                Console.WriteLine("    Ejecuta este desinstalador como administrador.");
                EsperarTecla();
                return 1;
            }
            catch (IOException ex)
            {
                Console.WriteLine($"  ✗ Error borrando datos de usuario: {ex.Message}");
                EsperarTecla();
                return 1;
            }
        }
        else
        {
            Console.WriteLine("No se encontró carpeta de datos de usuario (ya estaba limpia).");
        }

        // ── 2) Borrar carpeta de instalación del launcher ──
        // AppContext.BaseDirectory es la carpeta donde está este .exe (current\ en Velopack,
        // o la carpeta donde descomprimiste el ZIP portable).
        var carpetaInstalacion = AppContext.BaseDirectory;
        Console.WriteLine();
        Console.WriteLine($"Borrando carpeta de instalación: {carpetaInstalacion}");

        if (Directory.Exists(carpetaInstalacion))
        {
            // No podemos borrar la carpeta desde la que estamos ejecutándonos.
            // Creamos un script .bat en %TEMP% que:
            //   1. Espera a que este proceso termine.
            //   2. Borra la carpeta de instalación completa.
            //   3. Se borra a sí mismo.
            var miPid = Process.GetCurrentProcess().Id;
            var tempBat = Path.Combine(Path.GetTempPath(), $"PokeLockeUninstall_{miPid}.bat");
            var bat = new StringBuilder();
            bat.AppendLine("@echo off");
            bat.AppendLine("setlocal");
            bat.AppendLine($":espera");
            bat.AppendLine($"tasklist /FI \"PID eq {miPid}\" 2>nul | findstr /I \"{miPid}\" >nul");
            bat.AppendLine("if not errorlevel 1 goto espera");
            bat.AppendLine($"rmdir /s /q \"{carpetaInstalacion}\" 2>nul");
            bat.AppendLine($"del /f /q \"{tempBat}\" 2>nul");
            bat.AppendLine("endlocal");

            File.WriteAllText(tempBat, bat.ToString(), Encoding.UTF8);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempBat,
                    UseShellExecute = true,
                    Verb = "runas", // pedir administrador si hace falta para borrar la carpeta
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch
            {
                // Si no se puede pedir elevación, lanzar sin runas
                Process.Start(tempBat);
            }

            Console.WriteLine("  ✔ Se ha programado el borrado de la carpeta de instalación.");
            Console.WriteLine("    Se completará tras cerrar esta ventana.");
        }
        else
        {
            Console.WriteLine("No se encontró carpeta de instalación.");
        }

        Console.WriteLine();
        Console.WriteLine("El desinstalador ha terminado.");
        EsperarTecla();
        return 0;
    }

    private static void EsperarTecla()
    {
        Console.WriteLine();
        Console.Write("Presiona cualquier tecla para salir... ");
        Console.ReadKey(true);
    }
}
