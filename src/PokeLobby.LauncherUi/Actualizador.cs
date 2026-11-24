using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace PokeLobby.LauncherUi;

/// <summary>Comprueba e instala actualizaciones desde las Releases de GitHub.</summary>
internal static class Actualizador
{
    // Cambia esto por tu repositorio, sin ".git" al final.
    private const string RepositorioUrl = "https://github.com/Kakkaroto01/Pokelocke-Launcher";

    public static async Task ComprobarAsync(bool avisarSiNoHay = false)
    {
        if (RepositorioUrl.Contains("TU_USUARIO", StringComparison.Ordinal))
            return; // todavía sin configurar

        var mgr = new UpdateManager(new GithubSource(RepositorioUrl, null, prerelease: false));
        if (!mgr.IsInstalled)
            return; // se está ejecutando desde el ZIP: no hay instalación que actualizar

        try
        {
            // Si una actualización se descargó pero no llegó a aplicarse, se aplica ahora.
            if (mgr.UpdatePendingRestart is { } pendiente)
            {
                mgr.ApplyUpdatesAndRestart(pendiente);
                return;
            }

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                if (avisarSiNoHay)
                    MessageBox.Show("Ya tienes la última versión.", "PokeLocke Launcher",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var respuesta = MessageBox.Show(
                $"Hay una versión nueva: {info.TargetFullRelease.Version}\n" +
                $"Tienes instalada: {mgr.CurrentVersion}\n\n" +
                "¿Descargarla e instalarla ahora? El launcher se reiniciará.",
                "PokeLocke Launcher",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (respuesta != MessageBoxResult.Yes)
                return;

            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info.TargetFullRelease);
        }
        catch
        {
            // Sin red, GitHub caído o rate limit: no se molesta al jugador.
            if (avisarSiNoHay)
                MessageBox.Show("No se pudo comprobar si hay actualizaciones.",
                    "PokeLocke Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
