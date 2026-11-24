using System;
using System.Collections.Generic;
using System.IO;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Detección de consola por extensión de ROM, replicada del AddRomDialog del original
/// (evidencia REA: PokeLobbyLauncher.Views.AddRomDialog.DetectConsole).
/// </summary>
public static class ConsolaDetector
{
    public static string Detectar(string ruta) => Path.GetExtension(ruta).ToLowerInvariant() switch
    {
        ".gb" or ".gbc" or ".gba" => "GBA",
        ".nds" => "DS",
        ".3ds" or ".cci" or ".cia" or ".cxi" => "3DS",
        ".xci" or ".xcz" or ".nsp" or ".nsz" => "Switch",
        ".exe" => "Añil",   // fangame con ejecutable propio (Pokémon Añil)
        _ => "GBA",
    };

    /// <summary>Extensiones admitidas en el diálogo «Añadir ROM».</summary>
    public static readonly string[] Extensiones =
        [".gba", ".gb", ".gbc", ".nds", ".3ds", ".cci", ".cia", ".cxi", ".xci", ".xcz", ".nsp", ".nsz", ".exe"];
}

/// <summary>
/// Configuración del emulador EXTERNO de Switch, replicada del original
/// (evidencia REA: PokeLobbyLauncher.Services.EmuladorExternoService): ruta del .exe,
/// plantilla de argumentos con <c>{rom}</c> (por defecto <c>"{rom}"</c>) y bloqueo de
/// seleccionar el propio launcher como emulador.
/// </summary>
public static class EmuladorSwitchConfig
{
    public const string ArgumentosPorDefecto = "\"{rom}\"";

    /// <summary>¿La ruta apunta al propio launcher? (el original no permite elegirlo).</summary>
    public static bool EsElLauncher(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return false;
        var nombre = Path.GetFileName(ruta);
        return string.Equals(nombre, "PokeLockeLauncher.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nombre, "PokeLobbyLauncher.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nombre, "LockeLauncher.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Normaliza la ruta (vacía si es el launcher o nula).</summary>
    public static string NormalizarRuta(string? ruta)
        => EsElLauncher(ruta) ? "" : (ruta ?? "");

    /// <summary>Normaliza los argumentos (plantilla por defecto si están vacíos).</summary>
    public static string NormalizarArgumentos(string? argumentos)
        => string.IsNullOrWhiteSpace(argumentos) ? ArgumentosPorDefecto : argumentos!;

    /// <summary>
    /// Replica <c>EmuladorExternoService.ArgumentosPara</c> del original: si la plantilla NO
    /// contiene <c>{rom}</c> se AÑADE la ruta entrecomillada al final; si la contiene, se
    /// sustituye tal cual (las comillas, si se quieren, van en la plantilla).
    /// </summary>
    public static string ConstruirArgumentos(string rom, string? plantilla = null)
    {
        var argumentos = NormalizarArgumentos(plantilla);
        return argumentos.Contains("{rom}", StringComparison.Ordinal)
            ? argumentos.Replace("{rom}", rom)
            : argumentos + " \"" + rom + "\"";
    }

    /// <summary>¿Está configurado y el ejecutable existe?</summary>
    public static bool Configurado(string? ruta)
        => !string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta);
}

/// <summary>
/// Argumentos de lanzamiento del emulador, replicados del original
/// (evidencia REA: <c>EmulatorService.Launch</c> → <c>Arguments = (consoleName == "Switch")
/// ? EmuladorExternoService.ArgumentosPara(romPath) : ("\"" + romPath + "\"")</c>).
/// </summary>
public static class LanzamientoEmulador
{
    /// <summary>
    /// GBA/DS/3DS: la ROM entrecomillada como ÚNICO argumento (melonDS/mGBA/azahar la abren así).
    /// Switch: la plantilla configurada del emulador externo.
    /// </summary>
    public static string Argumentos(string consola, string rom, string? switchArgs = null)
        => string.Equals(consola, "Switch", StringComparison.OrdinalIgnoreCase)
            ? EmuladorSwitchConfig.ConstruirArgumentos(rom, switchArgs)
            : "\"" + rom + "\"";
}

/// <summary>
/// Comprobaciones de permisos de escritura del launcher: avisa si la carpeta de datos
/// (o la del .sav) no es escribible, en vez de fallar con «Access to the path … denied».
/// </summary>
public static class PermisosCarpeta
{
    /// <summary>¿Se puede crear y borrar un archivo temporal en <paramref name="carpeta"/>?</summary>
    public static bool PuedeEscribir(string? carpeta)
    {
        if (string.IsNullOrWhiteSpace(carpeta)) return false;
        try
        {
            Directory.CreateDirectory(carpeta);
            var prueba = Path.Combine(carpeta, ".prueba-escritura-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var fs = new FileStream(prueba, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                fs.WriteByte(0);
            File.Delete(prueba);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>¿Es escribible la carpeta que contiene el archivo indicado?</summary>
    public static bool PuedeEscribirEnArchivo(string? archivo)
        => !string.IsNullOrWhiteSpace(archivo) && PuedeEscribir(Path.GetDirectoryName(archivo));

    /// <summary>
    /// Devuelve la PRIMERA carpeta escribible de las candidatas (creándola si hace falta), o null.
    /// Permite auto-reparar la carpeta de datos si AppData está bloqueado por permisos, OneDrive,
    /// antivirus o una política del equipo.
    /// </summary>
    public static string? ElegirEscribible(params string?[] candidatas)
    {
        foreach (var candidata in candidatas)
        {
            if (string.IsNullOrWhiteSpace(candidata)) continue;
            if (PuedeEscribir(candidata)) return candidata;
        }
        return null;
    }

    /// <summary>
    /// Candidatas por orden de preferencia: la preferida, Documentos, LOCALAPPDATA alternativo,
    /// junto al ejecutable y, como último recurso, TEMP.
    /// </summary>
    public static string[] CandidatasDeDatos(string? carpetaPreferida = null, string? carpetaExe = null)
    {
        var documentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var lista = new List<string>();
        if (!string.IsNullOrWhiteSpace(carpetaPreferida)) lista.Add(carpetaPreferida!);
        if (!string.IsNullOrWhiteSpace(documentos)) lista.Add(Path.Combine(documentos, "PokeLockeLauncher"));
        if (!string.IsNullOrWhiteSpace(perfil)) lista.Add(Path.Combine(perfil, "PokeLockeLauncher"));
        if (!string.IsNullOrWhiteSpace(local)) lista.Add(Path.Combine(local, "LockeLauncher"));
        if (!string.IsNullOrWhiteSpace(carpetaExe)) lista.Add(Path.Combine(carpetaExe!, "datos"));
        lista.Add(Path.Combine(Path.GetTempPath(), "PokeLockeLauncher"));
        return [.. lista];
    }
}
