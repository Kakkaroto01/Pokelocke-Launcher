using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Construye el nodo <c>myTeam</c> que espera la app Android («Buildeo → Mi Equipo») a partir
/// del equipo real del <c>.sav</c>, con el formato acordado en <c>shared/Intermediario.txt</c>:
/// 6 huecos (null si vacío), nombres de ataque en inglés, habilidad en español, tipos en inglés.
/// </summary>
public static class EquipoRemoto
{
    private static readonly string[] OrdenStats = ["hp", "atk", "def", "spa", "spd", "spe"];

    public static Dictionary<string, object?> Construir(SaveFile sav)
    {
        var es = GameInfo.GetStrings("es");
        var en = GameInfo.GetStrings("en");
        var slots = new List<object?>();

        for (var i = 0; i < 6; i++)
        {
            var pkm = sav.GetPartySlotAtIndex(i);
            if (pkm.Species <= 0)
            {
                slots.Add(null);
                continue;
            }

            var hab = pkm.Format == 4 ? pkm.Data[0x15] : pkm.Ability;
            var habilidad = hab >= 0 && hab < es.Ability.Count ? es.Ability[hab] : "?";
            var ataques = pkm.Moves.Where(m => m > 0)
                .Select(m => m < en.Move.Count ? en.Move[m] : "#" + m)
                .ToList();
            var tipos = new List<string>();
            try
            {
                var t1 = (MoveType)pkm.PersonalInfo.Type1;
                var t2 = (MoveType)pkm.PersonalInfo.Type2;
                tipos.Add(t1.ToString());
                if (t2 != t1) tipos.Add(t2.ToString());
            }
            catch
            {
                // tipos no disponibles en esta tabla: se dejan vacíos
            }

            slots.Add(new Dictionary<string, object?>
            {
                ["id"] = pkm.Species,
                ["name"] = pkm.Species < es.Species.Count ? es.Species[pkm.Species] : "#" + pkm.Species,
                ["level"] = pkm.CurrentLevel,
                ["nature"] = pkm.Nature.ToString().ToUpperInvariant(),
                ["ability"] = habilidad,
                ["moves"] = ataques,
                ["ivs"] = Stats(pkm.IV_HP, pkm.IV_ATK, pkm.IV_DEF, pkm.IV_SPA, pkm.IV_SPD, pkm.IV_SPE),
                ["evs"] = Stats(pkm.EV_HP, pkm.EV_ATK, pkm.EV_DEF, pkm.EV_SPA, pkm.EV_SPD, pkm.EV_SPE),
                ["stages"] = Stats(0, 0, 0, 0, 0, 0),
                ["baseStats"] = BaseStats(pkm),
                ["types"] = tipos,
                ["spriteUrl"] = $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/{pkm.Species}.png",
                ["isShiny"] = pkm.IsShiny,
            });
        }

        return new Dictionary<string, object?> { ["slots"] = slots };
    }

    private static Dictionary<string, int> Stats(int hp, int atk, int def, int spa, int spd, int spe) => new()
    {
        ["hp"] = hp, ["atk"] = atk, ["def"] = def, ["spa"] = spa, ["spd"] = spd, ["spe"] = spe,
    };

    private static Dictionary<string, int> BaseStats(PKM pkm)
    {
        try
        {
            var p = pkm.PersonalInfo;
            return new Dictionary<string, int>
            {
                ["hp"] = p.HP, ["atk"] = p.ATK, ["def"] = p.DEF,
                ["spa"] = p.SPA, ["spd"] = p.SPD, ["spe"] = p.SPE,
            };
        }
        catch
        {
            return new Dictionary<string, int> { ["hp"] = 0, ["atk"] = 0, ["def"] = 0, ["spa"] = 0, ["spd"] = 0, ["spe"] = 0 };
        }
    }
}
