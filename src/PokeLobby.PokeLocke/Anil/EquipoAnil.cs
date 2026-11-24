using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Construye el nodo <c>myTeam</c> para «Buildeo → Mi Equipo» a partir del equipo de una partida
/// de Pokémon Añil. El id nacional se deduce del nombre visible inglés del PBS (coincide con la
/// tabla de especies de PKHeX) y los tipos/stats base se leen del propio <c>pokemon.txt</c>.
/// </summary>
public static class EquipoAnil
{
    private static readonly string[] OrdenStats = ["hp", "atk", "def", "spa", "spd", "spe"];

    private static readonly Dictionary<string, string> Tipos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NORMAL"] = "Normal", ["FIRE"] = "Fire", ["WATER"] = "Water", ["ELECTRIC"] = "Electric",
        ["GRASS"] = "Grass", ["ICE"] = "Ice", ["FIGHTING"] = "Fighting", ["POISON"] = "Poison",
        ["GROUND"] = "Ground", ["FLYING"] = "Flying", ["PSYCHIC"] = "Psychic", ["BUG"] = "Bug",
        ["ROCK"] = "Rock", ["GHOST"] = "Ghost", ["DRAGON"] = "Dragon", ["DARK"] = "Dark",
        ["STEEL"] = "Steel", ["FAIRY"] = "Fairy", ["COSMIC"] = "Cosmic", ["SOUND"] = "Sound",
    };

    public static Dictionary<string, object?> Construir(PartidaAnil partida)
    {
        var tipos = LeerTipos(partida);
        var stats = LeerStats(partida);
        var slots = new List<object?>();

        for (var i = 0; i < 6; i++)
        {
            var mon = i < partida.Equipo.Count ? partida.Equipo[i] : null;
            if (mon is null)
            {
                slots.Add(null);
                continue;
            }

            var interno = mon.InternalSpecies ?? "";
            var id = IdNacional(mon.Species);
            var movs = mon.Moves.Where(m => !string.IsNullOrWhiteSpace(m.Name)).Select(m => m.Name).ToList();
            var t = tipos.TryGetValue(interno, out var tt) ? tt
                  : tipos.TryGetValue((mon.Species ?? "").ToUpperInvariant().Replace(" ", ""), out var t2) ? t2
                  : new List<string>();
            var b = stats.TryGetValue(interno, out var bb) ? bb : new Dictionary<string, int>();
            slots.Add(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = mon.Species ?? interno,
                ["level"] = mon.Level,
                ["nature"] = (mon.InternalNature ?? "HARDY").ToUpperInvariant(),
                ["ability"] = mon.Ability ?? mon.InternalAbility,
                ["moves"] = movs,
                ["ivs"] = Stats(mon.IVs),
                ["evs"] = Stats(mon.EVs),
                ["stages"] = Stats(new[] { 0, 0, 0, 0, 0, 0 }),
                ["baseStats"] = b,
                ["types"] = t,
                ["spriteUrl"] = id > 0
                    ? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/{id}.png"
                    : "",
                ["isShiny"] = mon.IsShiny,
            });
        }

        return new Dictionary<string, object?> { ["slots"] = slots };
    }

    private static Dictionary<string, int> Stats(int[] v)
    {
        var d = new Dictionary<string, int>();
        for (var i = 0; i < OrdenStats.Length; i++) d[OrdenStats[i]] = i < v.Length ? v[i] : 0;
        return d;
    }

    /// <summary>Id nacional aproximado: por nombre inglés del PBS (tabla de PKHeX).</summary>
    private static int IdNacional(string? nombre)
    {
        try
        {
            var en = PKHeX.Core.GameInfo.GetStrings("en").Species;
            var objetivo = (nombre ?? "").ToUpperInvariant().Replace(" ", "").Replace("-", "");
            for (var i = 1; i < en.Count; i++)
                if (en[i].ToUpperInvariant().Replace(" ", "").Replace("-", "") == objetivo) return i;
        }
        catch { }
        return 0;
    }

    // ─────────────────────── lectura de PBS (tipos y stats base) ───────────────────────

    private static string? _pbsCache;
    private static Dictionary<string, List<string>>? _tiposCache;
    private static Dictionary<string, Dictionary<string, int>>? _statsCache;

    private static void Cargar(PartidaAnil partida)
    {
        if (_tiposCache is not null) return;
        _tiposCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _statsCache = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var carpeta = PartidaAnil.PbsDe(PartidaAnil.PbsDe(null)) ?? null;
            // el PBS ya está cargado en el PartidaAnil: usamos su tabla de especies para localizar el archivo
            var ruta = BuscarPokemonTxt(partida);
            if (ruta is null) return;
            string? actual = null;
            foreach (var linea in File.ReadLines(ruta))
            {
                var l = linea.Trim();
                if (l.StartsWith("[") && l.EndsWith("]")) { actual = l[1..^1]; continue; }
                if (actual is null) continue;
                if (l.StartsWith("Types", StringComparison.OrdinalIgnoreCase))
                {
                    var vals = l.Split('=', 2).ElementAtOrDefault(1) ?? "";
                    _tiposCache[actual] = vals.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => Tipos.TryGetValue(x.Trim(), out var n) ? n : x.Trim())
                        .ToList();
                }
                else if (l.StartsWith("BaseStats", StringComparison.OrdinalIgnoreCase))
                {
                    var vals = l.Split('=', 2).ElementAtOrDefault(1) ?? "";
                    var nums = vals.Split(',').Select(x => int.TryParse(x.Trim(), out var n) ? n : 0).ToArray();
                    if (nums.Length >= 6)
                    {
                        // Essentials: HP, ATK, DEF, SPEED, SPATK, SPDEF
                        _statsCache[actual] = new Dictionary<string, int>
                        {
                            ["hp"] = nums[0], ["atk"] = nums[1], ["def"] = nums[2],
                            ["spe"] = nums[3], ["spa"] = nums[4], ["spd"] = nums[5],
                        };
                    }
                }
            }
        }
        catch { }
    }

    private static string? BuscarPokemonTxt(PartidaAnil partida)
    {
        // Usa la MISMA carpeta PBS desde la que se cargó la partida (la fija el launcher con la
        // ruta del juego); si no, se intenta el APPDATA del fangame.
        var candidatos = new List<string>();
        if (!string.IsNullOrWhiteSpace(partida.CarpetaPbs))
            candidatos.Add(Path.Combine(partida.CarpetaPbs!, "pokemon.txt"));
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        candidatos.Add(Path.Combine(appdata, "Pokemon Anil", "PBS", "pokemon.txt"));
        return candidatos.FirstOrDefault(File.Exists);
    }

    private static Dictionary<string, List<string>> LeerTipos(PartidaAnil p) { Cargar(p); return _tiposCache!; }
    private static Dictionary<string, Dictionary<string, int>> LeerStats(PartidaAnil p) { Cargar(p); return _statsCache!; }
}
