using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Localización de la partida guardada de una ROM. Convención del emulador (melonDS/mGBA):
/// el archivo se crea junto a la ROM con el MISMO nombre base y extensión <c>.sav</c>
/// (DeSmuME usa <c>.dsv</c>; algunos usan <c>.srm</c>). Si la carpeta de partidas está
/// configurada, se busca ahí; si aún no existe el archivo, el emulador lo creará al guardar.
/// </summary>
public static class LocalizadorPartidas
{
    public static readonly string[] Extensiones = [".sav", ".dsv", ".srm"];

    /// <summary>Nombre base de la ROM (sin extensión).</summary>
    public static string BaseDe(string rutaRom) => Path.GetFileNameWithoutExtension(rutaRom);

    /// <summary>
    /// Ruta donde el emulador guardará la partida si todavía no existe: carpeta configurada
    /// (o la de la ROM) + nombre base + <c>.sav</c>.
    /// </summary>
    public static string RutaPrevista(string rutaRom, string? carpetaConfigurada)
    {
        // 3DS: el save vive en el sdmc del emulador (por title id).
        if (Path.GetExtension(rutaRom).ToLowerInvariant() is ".3ds" or ".cci" or ".cxi" or ".app")
        {
            var vivo = Guardado3ds.BuscarMainDeRom(rutaRom);
            if (vivo is not null) return vivo;
        }
        var carpeta = !string.IsNullOrWhiteSpace(carpetaConfigurada) && Directory.Exists(carpetaConfigurada)
            ? carpetaConfigurada!
            : Path.GetDirectoryName(rutaRom) ?? "";
        return Path.Combine(carpeta, BaseDe(rutaRom) + ".sav");
    }

    /// <summary>Carpeta efectiva de partidas (configurada si existe, si no la de la ROM).</summary>
    public static string? CarpetaDe(string rutaRom, string? carpetaConfigurada)
    {
        if (!string.IsNullOrWhiteSpace(carpetaConfigurada) && Directory.Exists(carpetaConfigurada))
            return carpetaConfigurada;
        var dir = Path.GetDirectoryName(rutaRom);
        return dir is not null && Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Busca la partida de ESTA rom: primero un archivo con su mismo nombre base; después la
    /// huella exacta ya registrada; y solo si hay UNA única partida en la carpeta (sin otras ROMs
    /// que puedan reclamarla) se acepta como compatibilidad con bibliotecas antiguas.
    /// Nunca elige la partida «más reciente» a ciegas (eso asociaba partidas de otros juegos).
    /// </summary>
    public static string? Buscar(string rutaRom, string? carpetaConfigurada,
        IReadOnlyDictionary<string, string>? huellasPorRom = null)
    {
        // 0) Pokémon Añil (fangame RPG Maker): su partida es un .rxdata en el APPDATA del juego.
        if (GuardadoAnil.EsJuegoAnil(rutaRom))
        {
            var anil = GuardadoAnil.Buscar(rutaRom, carpetaConfigurada);
            if (anil is not null) return anil;
        }

        // 1) La carpeta configurada puede ser directamente un archivo de partida.
        if (!string.IsNullOrWhiteSpace(carpetaConfigurada) && File.Exists(carpetaConfigurada))
            return Extensiones.Contains(Path.GetExtension(carpetaConfigurada), StringComparer.OrdinalIgnoreCase)
                ? carpetaConfigurada
                : null;

        // 0) 3DS (Azahar/Citra): el save vive en su sdmc por title id, NO junto a la ROM.
        var es3ds = Path.GetExtension(rutaRom).ToLowerInvariant() is ".3ds" or ".cci" or ".cxi" or ".app";
        if (es3ds)
        {
            var vivo = Guardado3ds.BuscarMainDeRom(rutaRom);
            if (vivo is not null) return vivo;

            // Sin partida viva todavía: si el jugador eligió un ARCHIVO de partida, se copia a la
            // carpeta de Azahar para que el emulador la use desde ese momento.
            if (!string.IsNullOrWhiteSpace(carpetaConfigurada) && File.Exists(carpetaConfigurada))
            {
                var importado = Guardado3ds.ImportarPartida(rutaRom, carpetaConfigurada!);
                return importado.Ok ? importado.Destino : null;
            }
        }

        var carpeta = CarpetaDe(rutaRom, carpetaConfigurada);
        if (carpeta is null) return null;

        // 2) Mismo nombre base que la ROM (convención del emulador).
        var baseRom = BaseDe(rutaRom);
        foreach (var ext in Extensiones)
        {
            var candidato = Path.Combine(carpeta, baseRom + ext);
            if (File.Exists(candidato)) return candidato;
        }
        // 3) Un archivo cuyo nombre empiece por el nombre base (roms con sufijos).
        var archivos = Directory.GetFiles(carpeta)
            .Where(f => Extensiones.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();
        var porNombre = archivos.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).StartsWith(baseRom, StringComparison.OrdinalIgnoreCase));
        if (porNombre is not null) return porNombre;

        // 3.5) 3DS (Azahar/Citra): el guardado NO es .sav, es un archivo llamado «main» dentro de
        // la carpeta de títulos del emulador. Se busca (profundidad limitada) si el jugador dio
        // la carpeta de usuario o del título.
        var main3ds = BuscarMain3ds(carpeta);
        if (main3ds is not null) return main3ds;

        // 4) Huella exacta registrada para esta ROM (incluye carpeta de partidas del emulador).
        if (huellasPorRom is not null
            && huellasPorRom.TryGetValue(rutaRom, out var registrada)
            && !string.IsNullOrWhiteSpace(registrada)
            && File.Exists(registrada))
            return registrada;

        // 5) Compatibilidad con bibliotecas antiguas: una ÚNICA partida en la carpeta y
        //    ninguna otra ROM en esa carpeta (así no se asocia la partida de otro juego).
        if (archivos.Count == 1)
        {
            var otrasRoms = Directory.GetFiles(carpeta)
                .Count(f => ConsolaDetector.Extensiones.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                            && !string.Equals(Path.GetFileName(f), Path.GetFileName(rutaRom), StringComparison.OrdinalIgnoreCase));
            if (otrasRoms == 0) return archivos[0];
        }

        return null;
    }

    /// <summary>¿Hay partida existente para la ROM?</summary>
    public static bool Existe(string rutaRom, string? carpetaConfigurada,
        IReadOnlyDictionary<string, string>? huellasPorRom = null)
        => Buscar(rutaRom, carpetaConfigurada, huellasPorRom) is not null;
    /// <summary>Busca un guardado de 3DS («main») en la carpeta indicada (hasta 6 niveles).</summary>
    public static string? BuscarMain3ds(string? carpeta)
    {
        if (string.IsNullOrWhiteSpace(carpeta) || !Directory.Exists(carpeta)) return null;
        try
        {
            var raiz = Path.GetFullPath(carpeta);
            foreach (var archivo in Directory.EnumerateFiles(raiz, "main", SearchOption.AllDirectories))
            {
                var relativa = Path.GetRelativePath(raiz, archivo);
                if (relativa.Count(c => c == Path.DirectorySeparatorChar) > 6) continue;
                var info = new FileInfo(archivo);
                if (info.Length >= 0x10000) return archivo;   // un «main» de 3DS pesa cientos de KB
            }
        }
        catch { /* sin permisos o ruta enorme */ }
        return null;
    }
}
