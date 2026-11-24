using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PokeLobby.Watcher;

/// <summary>Entrada de la biblioteca de ROMs (compatible con el library.json del launcher original).</summary>
public sealed class RomEntry
{
    public string Name { get; set; } = "";
    public string ConsoleName { get; set; } = "";
    public string RomPath { get; set; } = "";
    public string SavePath { get; set; } = "";
    public string RoomName { get; set; } = "";
    public int SalaId { get; set; }
    public int RandomSalaId { get; set; }
    public int RandomVersion { get; set; }
    public string? LastPlayed { get; set; }
    public string DetectedGame { get; set; } = "";
    public string? JuegoPorGuardado { get; set; }
    public bool GuardadoAutomatico { get; set; }
    public string? PkhexVariant { get; set; }
    public string CoverPath { get; set; } = "";
}

/// <summary>
/// Persistencia de la biblioteca (<c>library.json</c>), con el mismo formato PascalCase que
/// escribe el launcher original, de modo que ambos comparten la misma biblioteca.
/// </summary>
public sealed class LibraryStore
{
    private static readonly JsonSerializerOptions Json = new();   // PascalCase, como el original

    private readonly string _ruta;

    /// <summary>Ruta por defecto: %LOCALAPPDATA%\PokeLockeLauncher\datos\library.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLockeLauncher", "datos", "library.json");

    public string StorePath => _ruta;

    public LibraryStore(string? path = null) => _ruta = path ?? DefaultPath;

    public List<RomEntry> Load()
    {
        if (!File.Exists(_ruta)) return [];
        try
        {
            var datos = JsonSerializer.Deserialize<List<RomEntry>>(File.ReadAllText(_ruta), Json);
            return datos ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(List<RomEntry> biblioteca)
    {
        var dir = Path.GetDirectoryName(_ruta);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_ruta, JsonSerializer.Serialize(biblioteca, Json));
    }
}
