using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;
using PokeLobby.Client.Dtos;

namespace PokeLobby.Watcher;

/// <summary>Pokémon del resumen de partida (mismos campos que el SaveResumen del huellas.json original).</summary>
public sealed class ResumenPokemon
{
    public uint Pid { get; set; }
    public string Especie { get; set; } = "";
    public string? Mote { get; set; }
    public int Nivel { get; set; }
    public bool Shiny { get; set; }
    public string Ivs { get; set; } = "";
    public string Habilidad { get; set; } = "";
    public string Ataques { get; set; } = "";
    public string? Objeto { get; set; }
    public string Donde { get; set; } = "";
    /// <summary>Número de habilidad interno (para PartyAbilityNumbers de la subida).</summary>
    public int AbilityNumber { get; set; }
}

/// <summary>
/// Resumen de la partida en el mismo formato que el <c>SaveResumen</c> del
/// <c>huellas.json</c> del launcher original (Entrenador, TrainerId, Segundos,
/// Dinero, Medallas, Pokemon[], Objetos{}).
/// </summary>
public sealed class ResumenPartida
{
    public string Entrenador { get; set; } = "";
    public uint TrainerId { get; set; }
    public long Segundos { get; set; }
    public int Dinero { get; set; }
    public int Medallas { get; set; }
    public string JuegoVersion { get; set; } = "";
    public List<ResumenPokemon> Pokemon { get; set; } = [];
    public Dictionary<string, int> Objetos { get; set; } = [];
}

/// <summary>
/// Pipeline de resumen de partidas con PKHeX.Core (misma versión que el launcher
/// original: 26.5.5) + exportación Showdown + construcción del payload de subida.
/// Firmas verificadas contra el ensamblado real con REA (SaveUtil.GetSaveFile,
/// SaveFile.PartyData/GetBoxData/Inventory.Pouches, ShowdownParsing.GetShowdownText…).
/// </summary>
public static class ResumenPartidaService
{
    /// <summary>Abre y detecta un archivo de partida con PKHeX.</summary>
    public static SaveFile Abrir(string rutaSave)
        => SaveUtil.GetSaveFile(rutaSave)
           ?? throw new InvalidOperationException($"PKHeX no reconoce el archivo de partida: {rutaSave}");

    public static ResumenPartida Extraer(string rutaSave, string idioma = "es")
        => Extraer(Abrir(rutaSave), idioma);

    /// <summary>Extrae el resumen completo (entrenador, medallas, equipo, cajas, bolsa).</summary>
    public static ResumenPartida Extraer(SaveFile sav, string idioma = "es")
    {
        var strings = GameInfo.GetStrings(idioma);
        var resumen = new ResumenPartida
        {
            Entrenador = sav.OT,
            TrainerId = sav.ID32,
            Segundos = sav.PlayedHours * 3600L + sav.PlayedMinutes * 60L + sav.PlayedSeconds,
            Dinero = unchecked((int)sav.Money),
            Medallas = ContarMedallas(sav),
            JuegoVersion = sav.Version.ToString(),
        };

        for (var i = 0; i < sav.PartyCount && i < sav.PartyData.Count; i++)
            Agregar(resumen, sav.PartyData[i], strings, "equipo");

        for (var caja = 0; caja < sav.BoxCount; caja++)
            foreach (var pkm in sav.GetBoxData(caja))
                Agregar(resumen, pkm, strings, $"caja {caja + 1}");

        foreach (var pouch in sav.Inventory.Pouches)
            foreach (var item in pouch.Items)
            {
                if (item.Index <= 0 || item.Count <= 0) continue;
                var clave = item.Index.ToString();
                resumen.Objetos[clave] = resumen.Objetos.TryGetValue(clave, out var previo)
                    ? previo + item.Count
                    : item.Count;
            }

        return resumen;
    }

    /// <summary>Exporta el equipo actual en formato de texto Showdown (campo «Export» de la subida).</summary>
    public static string ExportarShowdown(SaveFile sav)
        => string.Join("\n\n", sav.PartyData.Where(EsValido).Select(ShowdownParsing.GetShowdownText));

    /// <summary>
    /// Construye el payload de <c>POST /api/saves/showdown</c> a partir del resumen.
    /// Los campos que la estática no pudo observar (Muertos, HaGanadoLiga, TotemItemIds,
    /// PartyRutas, Senales, RevisionPartida) van con valores neutros/documentados.
    /// </summary>
    public static ShowdownUpload ConstruirSubida(
        ResumenPartida resumen,
        string exportShowdown,
        int salaId,
        string saveHash,
        string launcherVersion = "PokeLobby.Recreado/0.1",
        string randomizerVersion = "")
    {
        var equipo = resumen.Pokemon.Where(p => p.Donde == "equipo").ToList();
        return new ShowdownUpload
        {
            Export = exportShowdown,
            PartyPids = equipo.Select(p => p.Pid).ToArray(),
            PartyRutas = equipo.Select(_ => "").ToArray(),
            PartyAbilityNumbers = equipo.Select(p => p.AbilityNumber).ToArray(),
            TrainerId = resumen.TrainerId,
            Medallas = resumen.Medallas,
            Monedas = resumen.Dinero,
            SalaId = salaId,
            Boxes = resumen.Pokemon.Count(p => p.Donde.StartsWith("caja", StringComparison.Ordinal)),
            Muertos = 0,
            HaGanadoLiga = false,
            TotemItemIds = [],
            JuegoVersion = resumen.JuegoVersion,
            Entrenador = resumen.Entrenador,
            SegundosJugados = resumen.Segundos,
            SaveHash = saveHash,
            LauncherVersion = launcherVersion,
            RandomizerVersion = randomizerVersion,
            RevisionPartida = 0,
            Senales = null,
        };
    }

    // ─────────────────────────── internos ───────────────────────────

    private static void Agregar(ResumenPartida resumen, PKM pkm, GameStrings strings, string donde)
    {
        if (!EsValido(pkm)) return;
        var movimientos = new[] { pkm.Move1, pkm.Move2, pkm.Move3, pkm.Move4 }
            .Where(m => m > 0)
            .Select(m => NombreSeguro(strings.Move, m));
        resumen.Pokemon.Add(new ResumenPokemon
        {
            Pid = pkm.PID,
            Especie = NombreSeguro(strings.Species, pkm.Species),
            Mote = pkm.IsNicknamed ? pkm.Nickname : null,
            Nivel = pkm.Stat_Level,
            Shiny = pkm.IsShiny,
            // Orden verificado contra el huellas.json original y el export Showdown: HP/Atk/Def/SpA/SpD/Spe
            Ivs = $"{pkm.IV_HP}/{pkm.IV_ATK}/{pkm.IV_DEF}/{pkm.IV_SPA}/{pkm.IV_SPD}/{pkm.IV_SPE}",
            Habilidad = NombreHabilidad(pkm, strings),
            Ataques = string.Join(", ", movimientos),
            Objeto = pkm.HeldItem > 0 ? NombreSeguro(strings.Item, pkm.HeldItem) : null,
            Donde = donde,
            AbilityNumber = pkm.AbilityNumber,
        });
    }

    private static bool EsValido(PKM pkm) => pkm.Species > 0;

    /// <summary>Nombre localizado de la habilidad actual (en gens 3-6 «Ability» es el slot del PersonalInfo).</summary>
    private static string NombreHabilidad(PKM pkm, GameStrings strings)
    {
        var id = pkm.Ability;
        var personal = pkm.PersonalInfo;
        if (pkm.Format <= 6 && id >= 0 && id < personal.AbilityCount)
        {
            var idHabilidad = personal.GetAbilityAtIndex(id);
            if (idHabilidad >= 0) id = idHabilidad;
        }
        return NombreSeguro(strings.Ability, id);
    }

    /// <summary>Medallas por generación, con las mismas propiedades que usa el launcher original.</summary>
    private static int ContarMedallas(SaveFile sav) => sav switch
    {
        SAV4 s4 => Popcount(s4.Badges),
        SAV3 s3 => Popcount(s3.Badges),
        SAV2 s2 => Popcount(s2.Badges),
        SAV1 s1 => Popcount(s1.Badges),
        SAV5 s5 => Popcount(s5.Misc.Badges),
        SAV6 s6 => Popcount(s6.Badges),
        SAV8SWSH s8 => Popcount(s8.Badges),
        _ => 0,
    };

    private static int Popcount(long valor)
    {
        var cuenta = 0;
        while (valor != 0)
        {
            cuenta += (int)(valor & 1);
            valor >>= 1;
        }
        return cuenta;
    }

    private static string NombreSeguro(object lista, int indice) => lista switch
    {
        string[] nombres => indice >= 0 && indice < nombres.Length ? nombres[indice] : "?",
        System.Collections.IList accesible => indice >= 0 && indice < accesible.Count ? $"{accesible[indice]}" : "?",
        _ => "?",
    };
}
