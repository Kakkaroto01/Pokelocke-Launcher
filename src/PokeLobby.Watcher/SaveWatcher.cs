using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace PokeLobby.Watcher;

/// <summary>Cambio detectado en un archivo de partida.</summary>
/// <param name="FilePath">Ruta del archivo.</param>
/// <param name="Sha256">Huella SHA-256 del contenido (hex minúsculas).</param>
/// <param name="Size">Tamaño en bytes.</param>
/// <param name="LastWriteTicks">Última escritura UTC (ticks de .NET, igual que el original).</param>
/// <param name="Motivo">añadida / cambio / sin-cambio.</param>
/// <param name="Resumen">Resumen PKHeX de la partida (solo si se activó <c>conResumen</c> y el archivo es una partida válida).</param>
public sealed record SaveChange(string FilePath, string? Sha256, long Size, long LastWriteTicks, string Motivo, ResumenPartida? Resumen = null);

/// <summary>
/// Vigilante de partidas: observa cambios de <c>.sav/.dsv/.sa1/.sa2/main</c> en un
/// directorio (los mismos patrones que el launcher original), calcula la huella
/// SHA-256 con antirrebote y la registra en el almacén de huellas. Es el
/// equivalente funcional del <c>SaveWatcherService</c> original, con el resumen
/// PKHeX opcional por cambio (pipeline de subida completo).
/// </summary>
public sealed class SaveWatcher : IDisposable
{
    public static readonly string[] PatronesGuardado = ["*.sav", "*.dsv", "*.sa1", "*.sa2", "main"];

    private readonly HuellasStore _store;
    private readonly Dictionary<string, HuellaRom> _huellas;
    private readonly Dictionary<string, Timer> _pendientes = [];
    private readonly object _gate = new();
    private FileSystemWatcher? _fsw;
    private readonly bool _conResumen;
    private bool _disposed;

    public event EventHandler<SaveChange>? GuardadoCambiado;

    public SaveWatcher(HuellasStore? store = null, bool conResumen = false)
    {
        _store = store ?? new HuellasStore();
        _huellas = _store.Load();
        _conResumen = conResumen;
    }

    public void Start(string directorio, bool incluirSubcarpetas = true)
    {
        if (!Directory.Exists(directorio)) throw new DirectoryNotFoundException(directorio);
        FileSystemWatcher? previo;
        lock (_gate) { previo = _fsw; _fsw = null; }
        previo?.Dispose();

        var fsw = new FileSystemWatcher(directorio)
        {
            IncludeSubdirectories = incluirSubcarpetas,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        fsw.Changed += OnEvento;
        fsw.Created += OnEvento;
        fsw.Renamed += OnEvento;
        lock (_gate) { _fsw = fsw; }
        fsw.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        FileSystemWatcher? previo;
        lock (_gate)
        {
            previo = _fsw;
            _fsw = null;
            foreach (var timer in _pendientes.Values) timer.Dispose();
            _pendientes.Clear();
        }
        previo?.Dispose();
    }

    private void OnEvento(object? sender, FileSystemEventArgs e) => Programar(e.FullPath);

    private void Programar(string ruta)
    {
        if (!EsGuardado(ruta)) return;
        lock (_gate)
        {
            if (_disposed) return;
            if (_pendientes.TryGetValue(ruta, out var previo)) previo.Dispose();
            _pendientes[ruta] = new Timer(_ => Procesar(ruta), null, 700, Timeout.Infinite);
        }
    }

    /// <summary>Indica si la ruta es un archivo de partida vigiable (mismos patrones que el original).</summary>
    public static bool EsGuardado(string ruta)
    {
        var nombre = Path.GetFileName(ruta).ToLowerInvariant();
        return nombre == "main"
            || nombre.EndsWith(".sav", StringComparison.Ordinal)
            || nombre.EndsWith(".dsv", StringComparison.Ordinal)
            || nombre.EndsWith(".sa1", StringComparison.Ordinal)
            || nombre.EndsWith(".sa2", StringComparison.Ordinal);
    }

    private void Procesar(string ruta)
    {
        try
        {
            string hash;
            long largo;
            long ticks;
            using (var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                largo = fs.Length;
                ticks = File.GetLastWriteTimeUtc(ruta).Ticks;
                using var sha = SHA256.Create();
                hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            }

            ResumenPartida? resumen = null;
            if (_conResumen)
            {
                try { resumen = ResumenPartidaService.Extraer(ruta); }
                catch { /* el archivo no es una partida reconocible por PKHeX */ }
            }

            var clave = HuellasStore.Clave(ruta);
            string motivo;
            lock (_gate)
            {
                _huellas.TryGetValue(clave, out var previa);
                motivo = previa?.RomHash is null ? "añadida"
                       : previa.RomHash == hash ? "sin-cambio"
                       : "cambio";
                _huellas[clave] = new HuellaRom
                {
                    RomHash = hash,
                    RomSize = largo,
                    RomMtime = ticks,
                    RomMotivo = motivo,
                    RomFecha = DateTime.UtcNow.ToString("O"),
                    SavePath = ruta,
                };
                _store.Save(_huellas);
            }
            GuardadoCambiado?.Invoke(this, new SaveChange(ruta, hash, largo, ticks, motivo, resumen));
        }
        catch (IOException)
        {
            Programar(ruta); // el emulador sigue escribiendo el archivo; reintentar
        }
        catch (UnauthorizedAccessException)
        {
            // sin permisos: no reintentar
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _fsw?.Dispose();
            _fsw = null;
            foreach (var timer in _pendientes.Values) timer.Dispose();
            _pendientes.Clear();
        }
    }
}
