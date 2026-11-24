using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PokeLobby.Randomizer;

/// <summary>Resultado de una randomización (exit codes del RandoBridge).</summary>
/// <param name="ExitCode">0 = SUCCESS; 1 uso, 2 ROM no soportada, 3 ROM no cargada, 4 excepción.</param>
/// <param name="Motivo">Motivo interpretado a partir del exit code (como el launcher original).</param>
/// <param name="Salida">Salida estándar del proceso (incluye «SUCCESS» si va bien).</param>
/// <param name="SalidaError">Salida de error del proceso.</param>
/// <param name="Ok">ExitCode 0 y «SUCCESS» en la salida.</param>
public sealed record ResultadoRandomizacion(int ExitCode, string Motivo, string Salida, string SalidaError, bool Ok);

/// <summary>
/// Servicio del randomizer, réplica del <c>RandomizerService</c> original: lanza el
/// <c>PokeRandoZX.jar</c> (Universal Pokémon Randomizer ZX) a través del puente
/// <c>RandoBridge</c> con el JRE del <c>vendor\jre</c>, -Xmx4608M y la configuración en un
/// <c>pokelobby_rando_N.properties</c>. Contrato verificado contra el fuente Java del
/// propio puente (<c>RandoBridge <config> <rom_entrada> <rom_salida></c>,
/// exit codes 1-4, «SUCCESS» en stdout).
/// </summary>
public sealed class RandomizerService
{
    public const string PrefijoProperties = "pokelobby_rando_";
    public const string HeapJava = "-Xmx4608M";

    private readonly string _vendorDir;

    public RandomizerService(string vendorDir) => _vendorDir = vendorDir;

    public string DirRandomizer => Path.Combine(_vendorDir, "SuperRandomizer");
    public string RutaJar => Path.Combine(DirRandomizer, "PokeRandoZX.jar");
    public string DirBridge => Path.Combine(DirRandomizer, "bridge");
    public string RutaRandoBridgeClass => Path.Combine(DirBridge, "RandoBridge.class");
    public string RutaJreJava => Path.Combine(_vendorDir, "jre", "bin", "java.exe");

    public bool JreDisponible => File.Exists(RutaJreJava);
    public bool InstalacionCompleta =>
        File.Exists(RutaJar) && File.Exists(RutaRandoBridgeClass) && JreDisponible;

    /// <summary>Presets incluidos en el vendor (full_random, solo_pokemon, …).</summary>
    public string[] Presets()
    {
        var dir = Path.Combine(DirRandomizer, "presets");
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.rnqs").Select(n => Path.GetFileNameWithoutExtension(n) ?? "").OrderBy(n => n).ToArray()
            : [];
    }

    /// <summary>Ruta de java: el JRE del vendor o, si falta, el del sistema (PATH).</summary>
    public string ResolverJava()
    {
        if (JreDisponible) return RutaJreJava;
        var delSistema = BuscarEnPath("java.exe");
        if (delSistema is null)
            throw new FileNotFoundException(
                "No se encontró Java. Falta la carpeta vendor/jre de la instalación: reinstala el launcher desde pokelocke.com.");
        return delSistema;
    }

    /// <summary>Nombre de ROM de salida con el patrón del original: «<nombre>_randomized_<yyyyMMdd-HHmmss>».</summary>
    public static string NombreRomSalida(string romEntrada)
        => $"{Path.GetFileNameWithoutExtension(romEntrada)}_randomized_{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(romEntrada)}";

    /// <summary>
    /// Randomiza una ROM con la configuración dada. Escribe el properties
    /// (<c>pokelobby_rando_<version>.properties</c>) y lanza
    /// <c>java -Xmx4608M -cp "jar;bridge" RandoBridge config romIn romOut</c>.
    /// </summary>
    public async Task<ResultadoRandomizacion> RandomizarAsync(
        string romEntrada,
        string romSalida,
        RandomizerSettings ajustes,
        int versionConfig = 1,
        string? rutaProperties = null,
        CancellationToken ct = default)
    {
        var java = ResolverJava();
        if (!File.Exists(RutaJar) || !File.Exists(RutaRandoBridgeClass))
            throw new FileNotFoundException(
                "Faltan PokeRandoZX.jar o RandoBridge.class: faltan archivos del randomizador en la instalación (vendor/SuperRandomizer).");
        if (!File.Exists(romEntrada))
            throw new FileNotFoundException($"ROM de entrada no encontrada: {romEntrada}");

        // Rutas absolutas SIEMPRE: Java resuelve el classpath y los argumentos relativos
        // contra SU directorio de trabajo (SuperRandomizer), no contra el proceso padre.
        var jar = Path.GetFullPath(RutaJar);
        var dirBridge = Path.GetFullPath(DirBridge);
        var dirTrabajo = Path.GetFullPath(DirRandomizer);
        var romIn = Path.GetFullPath(romEntrada);
        var romOut = Path.GetFullPath(romSalida);
        rutaProperties = Path.GetFullPath(rutaProperties
            ?? Path.Combine(DirRandomizer, $"{PrefijoProperties}{versionConfig}.properties"));
        ajustes.Guardar(rutaProperties);

        var psi = new ProcessStartInfo(Path.GetFullPath(java))
        {
            Arguments = $"{HeapJava} -cp \"{jar};{dirBridge}\" RandoBridge \"{rutaProperties}\" \"{romIn}\" \"{romOut}\"",
            WorkingDirectory = dirTrabajo,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var proceso = Process.Start(psi)
            ?? throw new InvalidOperationException("EXCEPCIÓN al arrancar Java.");

        var tareaSalida = proceso.StandardOutput.ReadToEndAsync(ct);
        var tareaError = proceso.StandardError.ReadToEndAsync(ct);
        try
        {
            await proceso.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!proceso.HasExited) proceso.Kill(entireProcessTree: true);
            throw;
        }
        var salida = await tareaSalida;
        var salidaError = await tareaError;

        var exitCode = proceso.ExitCode;
        var ok = exitCode == 0 && salida.Contains("SUCCESS", StringComparison.Ordinal);
        return new ResultadoRandomizacion(exitCode, Motivo(exitCode, ok), salida, salidaError, ok);
    }

    /// <summary>Motivo interpretado a partir del exit code (códigos documentados en RandoBridge.java).</summary>
    public static string Motivo(int exitCode, bool success) => exitCode switch
    {
        0 => success ? "Randomización OK" : "exit 0 sin «SUCCESS» en la salida",
        1 => "uso incorrecto: RandoBridge <config.properties> <rom_entrada> <rom_salida>",
        2 => "ROM no soportada o formato no reconocido",
        3 => "no se pudo cargar la ROM",
        4 => "excepción durante la randomización (ver salida de error)",
        _ => $"código de salida {exitCode}",
    };

    private static string? BuscarEnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidato = Path.Combine(dir, exe);
                if (File.Exists(candidato)) return candidato;
            }
            catch
            {
                // ruta inválida en PATH: ignorar
            }
        }
        return null;
    }

    /// <summary>
    /// Aplica un porcentaje a los niveles de TODOS los entrenadores (el motor UPR solo admite
    /// niveles absolutos). Se ejecuta DESPUÉS de randomizar, sobre el ROM de salida.
    /// </summary>
    public Task<ResultadoRandomizacion> AplicarPorcentajeNivelesAsync(string rom, int porcentaje, CancellationToken ct = default)
        => EjecutarAyudanteAsync(rom, "percent", porcentaje, ct);

    /// <summary>Pone a nivel 100 los <paramref name="cuantos"/> entrenadores de nivel más alto (la liga).</summary>
    public Task<ResultadoRandomizacion> LigaANivel100Async(string rom, int cuantos = 6, CancellationToken ct = default)
        => EjecutarAyudanteAsync(rom, "league", cuantos, ct);

    private async Task<ResultadoRandomizacion> EjecutarAyudanteAsync(string rom, string modo, int valor, CancellationToken ct)
    {
        var java = ResolverJava();
        var jar = Path.GetFullPath(RutaJar);
        var dirBridge = Path.GetFullPath(DirBridge);
        var psi = new ProcessStartInfo(Path.GetFullPath(java))
        {
            Arguments = $"-cp \"{jar};{dirBridge}\" LevelToolsBridge \"{Path.GetFullPath(rom)}\" {modo} {valor}",
            WorkingDirectory = Path.GetFullPath(DirRandomizer),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var proceso = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo arrancar Java (niveles).");
        var tareaSalida = proceso.StandardOutput.ReadToEndAsync(ct);
        var tareaError = proceso.StandardError.ReadToEndAsync(ct);
        try { await proceso.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { if (!proceso.HasExited) proceso.Kill(entireProcessTree: true); throw; }
        var salida = await tareaSalida;
        var salidaError = await tareaError;
        var exitCode = proceso.ExitCode;
        var ok = exitCode == 0 && salida.Contains("SUCCESS", StringComparison.Ordinal);
        return new ResultadoRandomizacion(exitCode, ok ? "Niveles aplicados" : $"error aplicando niveles (exit {exitCode})", salida, salidaError, ok);
    }
}
