using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PokeLobby.Emulator;

/// <summary>
/// Utilidades de rutas para los emuladores. melonDS recibe los argumentos por la API ANSI en
/// Windows: una ROM en una carpeta con acentos («estará aquí») llega corrupta y el emulador dice
/// que no encuentra la ROM. Con el nombre corto 8.3 (siempre ASCII) sí la abre.
/// </summary>
public static class RutasWindows
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string rutaLarga, StringBuilder rutaCorta, uint longitud);

    /// <summary>Devuelve la ruta 8.3 si existe y es ASCII; si no, la original.</summary>
    public static string RutaCompatible(string ruta)
    {
        try
        {
            if (ruta.All(c => c < 128)) return ruta;            // ya es ASCII
            var sb = new StringBuilder(1024);
            var n = GetShortPathNameW(ruta, sb, (uint)sb.Capacity);
            if (n == 0 || n >= sb.Capacity) return ruta;
            var corta = sb.ToString();
            return corta.All(c => c < 128) ? corta : ruta;
        }
        catch
        {
            return ruta;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string destino, string origen, IntPtr seguridad);

    /// <summary>
    /// melonDS deriva el nombre del save del nombre del ROM; si se lanza con la ruta 8.3 el save
    /// se llamaría «POKEMO~3.sav». Esto crea ese nombre como ENLACE DURO al save real, de modo que
    /// el emulador y el launcher usan el mismo archivo.
    /// </summary>
    public static void EnlazarSaveCorto(string rutaRomCorta, string rutaSaveReal)
    {
        try
        {
            if (!File.Exists(rutaSaveReal)) return;
            var carpeta = Path.GetDirectoryName(rutaRomCorta);
            if (carpeta is null) return;
            var saveCorto = Path.Combine(carpeta, Path.GetFileNameWithoutExtension(rutaRomCorta) + ".sav");
            if (string.Equals(saveCorto, rutaSaveReal, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(saveCorto)) File.Delete(saveCorto);
            CreateHardLinkW(saveCorto, rutaSaveReal, IntPtr.Zero);
        }
        catch { }
    }
}
