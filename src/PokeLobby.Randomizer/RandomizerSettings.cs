using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PokeLobby.Randomizer;

/// <summary>
/// Modelo de configuración del randomizer en formato <c>.rnqs</c> (Java properties,
/// <c>clave=valor</c>), el mismo formato que escribe <c>RandomizerSettings.ToPropertiesText()</c>
/// del launcher original y que lee <c>RandoBridge</c> (ver su fuente en
/// <c>vendor\SuperRandomizer\bridge\RandoBridge.java</c>). Las claves desconocidas se
/// preservan tal cual (paso completo al puente).
/// </summary>
public sealed class RandomizerSettings
{
    private readonly List<string> _lineas = [];   // se preservan líneas en blanco y comentarios
    private readonly Dictionary<string, int> _indice = new(StringComparer.OrdinalIgnoreCase);   // clave → índice en _lineas

    /// <summary>
    /// Nace con TODAS las claves y valores por defecto del Universal Pokémon Randomizer ZX
    /// (plantilla extraída del original con REA), igual que <c>new RandomizerSettings()</c>
    /// del launcher original, cuyo <c>ToPropertiesText()</c> devuelve el mapa completo.
    /// </summary>
    public RandomizerSettings()
    {
        foreach (var linea in AjustesDefecto.Plantilla.Split('\n'))
        {
            var limpia = linea.Trim();
            if (limpia.Length == 0) continue;
            var igual = limpia.IndexOf('=');
            if (igual <= 0) continue;
            Set(limpia[..igual].Trim(), limpia[(igual + 1)..].Trim());
        }
    }

    public string? Get(string clave) => _indice.TryGetValue(clave, out var i) ? Valor(_lineas[i]) : null;

    public void Set(string clave, string valor)
    {
        if (_indice.TryGetValue(clave, out var i))
        {
            _lineas[i] = clave + "=" + valor;
            return;
        }
        _indice[clave] = _lineas.Count;
        _lineas.Add(clave + "=" + valor);
    }

    public IReadOnlyDictionary<string, string> Valores
    {
        get
        {
            var resultado = new Dictionary<string, string>();
            foreach (var (clave, i) in _indice.Select(p => (p.Key, p.Value)))
                resultado[clave] = Valor(_lineas[i]) ?? "";
            return resultado;
        }
    }

    /// <summary>Carga un .rnqs/.properties (líneas «clave=valor»; «#» son comentarios).
    /// Los valores del archivo SOBRESCRIBEN los defectos; las claves desconocidas se añaden.</summary>
    public static RandomizerSettings Cargar(string ruta)
    {
        var ajustes = new RandomizerSettings();
        foreach (var linea in File.ReadAllLines(ruta))
        {
            var limpia = linea.TrimStart();
            if (limpia.Length == 0 || limpia.StartsWith('#'))
            {
                ajustes._lineas.Add(linea);
                continue;
            }
            var igual = linea.IndexOf('=');
            if (igual <= 0)
                continue;
            ajustes.Set(linea[..igual].Trim(), linea[(igual + 1)..].Trim());
        }
        return ajustes;
    }

    /// <summary>Carga uno de los presets del vendor (full_random, solo_pokemon, …).</summary>
    public static RandomizerSettings DesdePreset(string dirRandomizer, string nombrePreset)
    {
        var ruta = Path.Combine(dirRandomizer, "presets", nombrePreset.EndsWith(".rnqs", StringComparison.OrdinalIgnoreCase) ? nombrePreset : nombrePreset + ".rnqs");
        return Cargar(ruta);
    }

    public void Guardar(string ruta)
    {
        var dir = Path.GetDirectoryName(ruta);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(ruta, ToPropertiesText(), new UTF8Encoding(false));
    }

    /// <summary>Texto del properties (clave=valor, preservando estructura), como ToPropertiesText() del original.</summary>
    public string ToPropertiesText() => string.Join("\n", _lineas) + "\n";

    // ──────────────────── accesores útiles (sección.mod más usados) ────────────────────

    public string TypesMod { get => Get("types.mod") ?? "UNCHANGED"; set => Set("types.mod", value); }
    public string AbilitiesMod { get => Get("abilities.mod") ?? "UNCHANGED"; set => Set("abilities.mod", value); }
    public string BaseStatsMod { get => Get("basestats.mod") ?? "UNCHANGED"; set => Set("basestats.mod", value); }
    public string StartersMod { get => Get("starters.mod") ?? "UNCHANGED"; set => Set("starters.mod", value); }
    public string StaticsMod { get => Get("statics.mod") ?? "UNCHANGED"; set => Set("statics.mod", value); }
    public string TradesMod { get => Get("trades.mod") ?? "UNCHANGED"; set => Set("trades.mod", value); }
    public string EvolutionsMod { get => Get("evolutions.mod") ?? "UNCHANGED"; set => Set("evolutions.mod", value); }
    public string MoveDataMod { get => Get("movedata.mod") ?? "UNCHANGED"; set => Set("movedata.mod", value); }
    public string MovesetsMod { get => Get("movesets.mod") ?? "UNCHANGED"; set => Set("movesets.mod", value); }
    public string WildMod { get => Get("wild.mod") ?? "UNCHANGED"; set => Set("wild.mod", value); }
    public string TrainersMod { get => Get("trainers.mod") ?? "UNCHANGED"; set => Set("trainers.mod", value); }
    public string TotemsMod { get => Get("totems.mod") ?? "UNCHANGED"; set => Set("totems.mod", value); }
    public string TmMovesMod { get => Get("tmmoves.mod") ?? "UNCHANGED"; set => Set("tmmoves.mod", value); }
    public string TmCompatMod { get => Get("tmcompat.mod") ?? "UNCHANGED"; set => Set("tmcompat.mod", value); }
    public string TutorMovesMod { get => Get("tutormoves.mod") ?? "UNCHANGED"; set => Set("tutormoves.mod", value); }
    public string TutorCompatMod { get => Get("tutorcompat.mod") ?? "UNCHANGED"; set => Set("tutorcompat.mod", value); }
    public string FieldItemsMod { get => Get("fielditems.mod") ?? "UNCHANGED"; set => Set("fielditems.mod", value); }
    public string ShopsMod { get => Get("shops.mod") ?? "UNCHANGED"; set => Set("shops.mod", value); }
    public string PickupMod { get => Get("pickup.mod") ?? "UNCHANGED"; set => Set("pickup.mod", value); }
    public bool RaceMode { get => bool.TryParse(Get("misc.raceMode"), out var v) && v; set => Set("misc.raceMode", value ? "true" : "false"); }

    private static string? Valor(string linea)
    {
        var igual = linea.IndexOf('=');
        return igual < 0 ? null : linea[(igual + 1)..].Trim();
    }
}
