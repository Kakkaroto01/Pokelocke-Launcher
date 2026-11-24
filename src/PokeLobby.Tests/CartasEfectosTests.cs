using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PokeLobby.PokeLocke;
using PKHeX.Core;
using Xunit;

namespace PokeLobby.Tests;

/// <summary>
/// Correcciones de efectos pedidas por el usuario: habilidad legal (63), MT que enseña de verdad
/// (66), editor de IV/EV (83 y 84) y reglas del gachapón (nivel 1, mote, 2 ataques de la generación).
/// </summary>
public class CartasEfectosTests
{
    private static async Task<(string Ruta, List<ResultadoAccion> Res, PKM Antes)> Ejecutar(
        Action<SaveFile>? ajustar, Action<AccionLauncher> config, string tipo = AccionTipo.CardEffect)
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        for (var i = 0; i < 6; i++)
        {
            var p = sav.GetPartySlotAtIndex(i);
            if (p.Species == 0) continue;
            p.IV_HP = 20; p.IV_ATK = 20; p.IV_DEF = 20;
            p.IV_SPA = 20; p.IV_SPD = 20; p.IV_SPE = 20;
            p.RefreshChecksum();
            sav.SetPartySlotAtIndex(p, i);
        }
        ajustar?.Invoke(sav);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));
        var antes = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        var fb = new FakeFirebase().Agregar("accion", Ayuda.Accion(tipo, config, "Heartgold"));
        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);
        return (ruta, res, antes);
    }

    [Fact] // 63: la habilidad cambia a una LEGAL de la especie
    public async Task Carta63_CambiaHabilidadLegal()
    {
        var (ruta, res, _) = await Ejecutar(null, a => a.CardId = 63);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");

        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.True(p.Ability > 0, "la habilidad quedó a 0");
        Assert.True(EfectosCartas.PuedeTenerHabilidad(p, p.Ability),
            $"la habilidad {p.Ability} no es legal para la especie");
    }

    [Fact] // 66: enseña el movimiento (reemplazando uno si no hay hueco)
    public async Task Carta66_AprendeMovimiento()
    {
        var (ruta, res, _) = await Ejecutar(null, a => a.CardId = 66);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");

        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.True(p.HasMove(85), $"Thunderbolt no está entre [{string.Join(",", p.Moves)}]");
        Assert.True(p.Moves.All(m => m <= p.MaxMoveID), "se coló un movimiento de otra generación");
    }

    [Fact] // 83: editor de IV/EV aplicado tal cual
    public async Task Carta83_Zenkai_AplicaLosIvsYEvsEditados()
    {
        var (ruta, res, _) = await Ejecutar(null, a => a.CardId = 83);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");

        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(31, p.IV_HP); Assert.Equal(31, p.IV_ATK); Assert.Equal(31, p.IV_DEF);
        Assert.Equal(31, p.IV_SPA); Assert.Equal(31, p.IV_SPD); Assert.Equal(31, p.IV_SPE);
        Assert.Equal(4, p.EV_HP);
        Assert.Equal(252, p.EV_ATK);
        Assert.Equal(252, p.EV_SPE);
        Assert.Equal(508, p.EV_HP + p.EV_ATK + p.EV_DEF + p.EV_SPA + p.EV_SPD + p.EV_SPE);
    }

    [Fact] // gacha: nivel 1, mote elegido, 2 ataques de la generación e IVs 0..31
    public async Task Gacha_Nivel1_Mote_DosAtaquesDeLaGeneracion()
    {
        var (ruta, res, _) = await Ejecutar(sav => sav.SetPartySlotAtIndex(sav.BlankPKM, 5),
            a =>
            {
                a.SpeciesId = 133; a.SpeciesName = "Eevee"; a.IsShiny = false;
                a.Ball = "POKEBALL"; a.GameGen = 4;
                a.Params = System.Text.Json.Nodes.JsonNode.Parse("""{"nickname":"MotePrueba"}""");
            }, AccionTipo.GachaPokemon);

        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");

        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(5);
        Assert.Equal(133, p.Species);
        Assert.Equal(1, p.Stat_Level);
        Assert.Equal("MotePrueba", p.Nickname);
        var movimientos = p.Moves.Where(m => m > 0).ToArray();
        Assert.InRange(movimientos.Length, 1, 2);
        Assert.All(movimientos, m => Assert.True(m <= p.MaxMoveID, $"movimiento {m} fuera de la generación"));
        Assert.All(new[] { p.IV_HP, p.IV_ATK, p.IV_DEF, p.IV_SPA, p.IV_SPD, p.IV_SPE }, iv => Assert.InRange(iv, 0, 31));
    }

    [Fact] // 59-especie: el gacha rechaza especies que no existen en el juego
    public async Task Gacha_EspecieInexistente_Falla()
    {
        var (_, res, _) = await Ejecutar(null, a =>
        {
            a.SpeciesId = 999; a.SpeciesName = "Zacian"; a.Ball = "POKEBALL"; a.GameGen = 4;
        }, AccionTipo.GachaPokemon);
        Assert.Equal(AccionEstado.Fallida, res.Single().Estado);
    }

    [Fact] // ITEM_GRANT con nombre en INGLÉS (la app manda "Flame Orb", no "Llama Esfera")
    public async Task ItemGrant_NombreIngles_SeAplica()
    {
        var (ruta, res, _) = await Ejecutar(null, a =>
        {
            a.ItemName = "Flame Orb";
            a.Quantity = 1;
        }, AccionTipo.ItemGrant);

        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");

        var sav = SaveUtil.GetSaveFile(ruta)!;
        var total = sav.Inventory.Pouches.Sum(p => p.Items.Where(i => i.Index > 0).Sum(i => i.Count));
        Assert.True(total > 0, "la mochila quedó vacía");
    }

    [Fact] // botón de la biblioteca: añade Carameloraros hasta el tope del juego
    public async Task Carameloraros_SeAnadenAlMaximoDelJuego()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        var tabla = GameInfo.GetStrings("en").Item;
        var id = -1;
        for (var k = 1; k < tabla.Count; k++)
            if (string.Equals(tabla[k], "Rare Candy", StringComparison.OrdinalIgnoreCase)) { id = k; break; }
        Assert.True(id > 0, "no encuentro Rare Candy en la tabla");

        var puestos = EfectosCartas.AnadirObjetos(sav.Inventory, id, sav.Generation >= 6 ? 995 : 99);
        sav.Inventory.CopyTo(sav);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));

        Assert.True(puestos > 0, $"no se añadió ninguno (id {id})");
        var releido = SaveUtil.GetSaveFile(ruta)!;
        var total = releido.Inventory.Pouches.SelectMany(p => p.Items).Where(i => i.Index == id).Sum(i => (int)i.Count);
        Assert.True(total >= puestos, $"leídos {total} de {puestos}");
    }

    [Fact] // 3DS: el title id sale de la cabecera NCCH/NCSD (para encontrar el main vivo de Azahar)
    public void Guardado3ds_LeerTitleId_NcchYNcsd()
    {
        var dir = Path.Combine(Ayuda.DirDatosTemporal(), "3ds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ncch = Path.Combine(dir, "a.cxi");
            var bytes = new byte[0x200];
            bytes[0x100] = (byte)'N'; bytes[0x101] = (byte)'C'; bytes[0x102] = (byte)'C'; bytes[0x103] = (byte)'H';
            BitConverter.GetBytes(0x000400000011C400UL).CopyTo(bytes, 0x118);
            File.WriteAllBytes(ncch, bytes);
            Assert.Equal(0x000400000011C400UL, Guardado3ds.LeerTitleId(ncch));

            var ncsd = Path.Combine(dir, "b.3ds");
            var b2 = new byte[0x200];
            b2[0x100] = (byte)'N'; b2[0x101] = (byte)'C'; b2[0x102] = (byte)'S'; b2[0x103] = (byte)'D';
            BitConverter.GetBytes(0x00040000001B5000UL).CopyTo(b2, 0x120 + 8);
            File.WriteAllBytes(ncsd, b2);
            Assert.Equal(0x00040000001B5000UL, Guardado3ds.LeerTitleId(ncsd));

            Assert.Null(Guardado3ds.LeerTitleId(Path.Combine(dir, "no-existe.cxi")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
