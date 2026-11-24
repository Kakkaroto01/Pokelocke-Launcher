using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Localiza la partida de **Pokémon Añil**. El juego guarda en
/// <c>%APPDATA%\Pokemon Anil\Partida N.rxdata</c>; también se acepta una ruta configurada
/// explícitamente (por ejemplo el .rxdata elegido a mano).
/// </summary>
public static class GuardadoAnil
{
    public static string? Buscar(string? rutaJuego, string? rutaConfigurada = null)
    {
        if (!string.IsNullOrWhiteSpace(rutaConfigurada) && rutaConfigurada!.EndsWith(".rxdata", StringComparison.OrdinalIgnoreCase) && File.Exists(rutaConfigurada))
            return rutaConfigurada;

        var lista = new List<string>();
        try
        {
            var appdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pokemon Anil");
            if (Directory.Exists(appdata))
                lista.AddRange(Directory.GetFiles(appdata, "*.rxdata", SearchOption.TopDirectoryOnly));
        }
        catch { }
        try
        {
            if (!string.IsNullOrWhiteSpace(rutaJuego))
            {
                var dir = File.Exists(rutaJuego) ? Path.GetDirectoryName(rutaJuego)! : rutaJuego!;
                foreach (var sub in new[] { "Save", "Saves", "" })
                {
                    var d = string.IsNullOrEmpty(sub) ? dir : Path.Combine(dir, sub);
                    if (Directory.Exists(d)) lista.AddRange(Directory.GetFiles(d, "*.rxdata", SearchOption.TopDirectoryOnly));
                }
            }
        }
        catch { }

        return lista.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    /// <summary>
    /// Un fangame para el launcher es cualquier **.exe** (Pokémon Añil y futuros): su nombre puede
    /// ser «Game.exe», así que no se puede exigir que lleve «anil» en el nombre.
    /// </summary>
    public static bool EsJuegoAnil(string? ruta) =>
        !string.IsNullOrWhiteSpace(ruta) &&
        Path.GetExtension(ruta).Equals(".exe", StringComparison.OrdinalIgnoreCase);
}
