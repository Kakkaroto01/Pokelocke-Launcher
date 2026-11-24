using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PokeLobby.Watcher;

/// <summary>
/// Entrada del almacén de huellas, compatible campo a campo con el
/// <c>huellas.json</c> del launcher original (claves PascalCase).
/// </summary>
public sealed class HuellaRom
{
    public string? RomHash { get; set; }
    public long RomSize { get; set; }
    public long RomMtime { get; set; }
    public string? RomMotivo { get; set; }
    public string? RomHashVisto { get; set; }
    public long RomSizeVisto { get; set; }
    public long RomMtimeVisto { get; set; }
    public string? RomFecha { get; set; }
    public string? SaveHash { get; set; }
    public string? SavePath { get; set; }
    public string? SaveMotivo { get; set; }
    public string? SaveFecha { get; set; }
}

/// <summary>
/// Persistencia del almacén de huellas en <c>huellas.json</c>, con el mismo
/// formato que escribe el launcher original (Serialización PascalCase,
/// claves = rutas en minúsculas), de modo que ambos pueden leerse mutuamente.
/// </summary>
public sealed class HuellasStore
{
    // El launcher original serializa huellas.json SIN naming policy → PascalCase.
    private static readonly JsonSerializerOptions Json = new();

    private readonly string _ruta;

    /// <summary>Ruta por defecto: %LOCALAPPDATA%\PokeLockeLauncher\datos\huellas.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLockeLauncher", "datos", "huellas.json");

    public string StorePath => _ruta;

    public HuellasStore(string? path = null) => _ruta = path ?? DefaultPath;

    /// <summary>Clave canónica de un archivo (el original usa la ruta en minúsculas).</summary>
    public static string Clave(string ruta) => ruta.ToLowerInvariant();

    public Dictionary<string, HuellaRom> Load()
    {
        if (!File.Exists(_ruta)) return new Dictionary<string, HuellaRom>(StringComparer.Ordinal);
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, HuellaRom>>(File.ReadAllText(_ruta), Json);
            return data is null
                ? new Dictionary<string, HuellaRom>(StringComparer.Ordinal)
                : new Dictionary<string, HuellaRom>(data, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, HuellaRom>(StringComparer.Ordinal);
        }
    }

    public void Save(Dictionary<string, HuellaRom> huellas)
    {
        var dir = Path.GetDirectoryName(_ruta);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_ruta, JsonSerializer.Serialize(huellas, Json));
    }
}
