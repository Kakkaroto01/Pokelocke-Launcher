using System;
using System.IO;
using System.Linq;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Localiza el guardado REAL de un juego de 3DS en Azahar/Citra. Estos emuladores no guardan
/// junto a la ROM: usan <c>%APPDATA%\{Azahar|Citra}\sdmc\Nintendo 3DS\&lt;ID0&gt;\&lt;ID1&gt;\title\&lt;alta&gt;\&lt;baja&gt;\data\00000001\main</c>.
/// El title id se saca de la cabecera de la ROM (NCSD/NCSD o NCCH), así se elige el main correcto
/// y no un volcado manual del mismo juego (que era el fallo: se editaba el volcado, no el save vivo).
/// </summary>
public static class Guardado3ds
{
    /// <summary>Lee el title id (0x00040000XXXXXXXX) de un .3ds/.cci/.cxi/.app. null si no se puede.</summary>
    public static ulong? LeerTitleId(string rutaRom)
    {
        try
        {
            using var fs = File.OpenRead(rutaRom);
            var cabecera = new byte[0x200];
            if (fs.Read(cabecera, 0, cabecera.Length) < 0x200) return null;

            // NCCH: "NCCH" en 0x100, program id en 0x118
            if (cabecera[0x100] == (byte)'N' && cabecera[0x101] == (byte)'C' && cabecera[0x102] == (byte)'C' && cabecera[0x103] == (byte)'H')
                return BitConverter.ToUInt64(cabecera, 0x118);

            // NCSD: "NCSD" en 0x100, primer NCCH con title id en 0x120 + 8
            if (cabecera[0x100] == (byte)'N' && cabecera[0x101] == (byte)'C' && cabecera[0x102] == (byte)'S' && cabecera[0x103] == (byte)'D')
                return BitConverter.ToUInt64(cabecera, 0x120 + 8);

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Busca el <c>main</c> vivo de ese title id en Azahar y Citra (el más reciente).</summary>
    public static string? BuscarMainDeRom(string rutaRom)
    {
        var titulo = LeerTitleId(rutaRom);
        if (titulo is null) return null;
        var alta = (uint)(titulo.Value >> 32);
        var baja = (uint)(titulo.Value & 0xFFFFFFFF);

        var raices = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Azahar", "sdmc"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "azahar", "sdmc"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Citra", "sdmc"),
        };

        // IMPORTANTE: se prueba AZAHAR primero y solo si no hay nada se cae a Citra. Antes se
        // elegía el «main» más reciente de ambos y, como el launcher escribía en Citra, ese
        // archivo siempre era el más nuevo y nunca se llegaba al de Azahar (el que usa el juego).
        var candidatos = new System.Collections.Generic.List<string>();
        foreach (var raiz in raices)
        {
            var baseNintendo = Path.Combine(raiz, "Nintendo 3DS");
            if (!Directory.Exists(baseNintendo)) continue;
            try
            {
                foreach (var id0 in Directory.GetDirectories(baseNintendo))
                foreach (var id1 in Directory.GetDirectories(id0))
                {
                    var main = Path.Combine(id1, "title", alta.ToString("x8"), baja.ToString("x8"), "data", "00000001", "main");
                    if (File.Exists(main)) candidatos.Add(main);
                }
            }
            catch { }
            if (candidatos.Count > 0)
                return candidatos.OrderByDescending(File.GetLastWriteTime).First();
        }
        return null;
    }

    /// <summary>Ruta del `main` vivo que usa Azahar para este juego (aunque aún no exista).</summary>
    public static string? RutaViva(string rutaRom)
    {
        var titulo = LeerTitleId(rutaRom);
        if (titulo is null) return null;
        var alta = (uint)(titulo.Value >> 32);
        var baja = (uint)(titulo.Value & 0xFFFFFFFF);
        var baseNintendo = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Azahar", "sdmc", "Nintendo 3DS");
        var id0 = Path.Combine(baseNintendo, new string('0', 32));
        var id1 = Path.Combine(id0, new string('0', 32));
        return Path.Combine(id1, "title", alta.ToString("x8"), baja.ToString("x8"), "data", "00000001", "main");
    }

    /// <summary>
    /// Copia una partida (`.sav`, `main`, volcado…) a la carpeta donde Azahar guarda ese juego,
    /// con copia de seguridad del `main` actual.
    /// </summary>
    public static (bool Ok, string Mensaje, string? Destino) ImportarPartida(string rutaRom, string archivoOrigen)
    {
        try
        {
            if (!File.Exists(archivoOrigen)) return (false, "no existe el archivo de partida elegido", null);
            var destino = RutaViva(rutaRom);
            if (destino is null) return (false, "no pude leer el título de la ROM 3DS para saber dónde guarda Azahar", null);

            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            if (File.Exists(destino))
            {
                var copia = destino + ".bak-locke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(destino, copia, overwrite: true);
            }
            File.Copy(archivoOrigen, destino, overwrite: true);
            return (true, "partida copiada a la carpeta de Azahar", destino);
        }
        catch (Exception ex)
        {
            return (false, "no se pudo copiar: " + ex.Message, null);
        }
    }
}
