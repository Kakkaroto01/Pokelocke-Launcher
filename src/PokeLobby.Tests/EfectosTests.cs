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
/// Efectos del catálogo aplicados por el SERVICIO REAL sobre un fixture de HeartGold
/// (formato de disco exacto): se guarda a disco y se RELEE el .sav para comprobar el valor.
/// </summary>
public class EfectosTests
{
    /// <summary>Copia el fixture real, normaliza el equipo (EV 10 / IV 20) y lo deja en disco.</summary>
    private static string PrepararReal(Action<SaveFile>? ajustar = null)
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        for (var i = 0; i < 6; i++)
        {
            var p = sav.GetPartySlotAtIndex(i);
            if (p.Species == 0) continue;
            p.EV_HP = 10; p.EV_ATK = 10; p.EV_DEF = 10;
            p.EV_SPA = 10; p.EV_SPD = 10; p.EV_SPE = 10;
            p.IV_HP = 20; p.IV_ATK = 20; p.IV_DEF = 20;
            p.IV_SPA = 20; p.IV_SPD = 20; p.IV_SPE = 20;
            p.RefreshChecksum();
            sav.SetPartySlotAtIndex(p, i);
        }
        ajustar?.Invoke(sav);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));
        return ruta;
    }

    private static async Task<(string Ruta, List<ResultadoAccion> Res, FakeFirebase Fb)> Ejecutar(
        Action<SaveFile>? ajustar, string tipo, Action<AccionLauncher> config, string juego = "Heartgold")
    {
        var ruta = PrepararReal(ajustar);
        var fb = new FakeFirebase().Agregar("accion", Ayuda.Accion(tipo, config, juego));
        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);
        return (ruta, res, fb);
    }

    private const string Params = "params";

    private static System.Text.Json.Nodes.JsonNode P(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!;

    [Fact]
    public async Task Carta67_BanoDinero_PersisteEnDisco()
    {
        var baseFija = SaveUtil.GetSaveFile(Path.Combine(AppContext.BaseDirectory, "testdata", "hgss.sav"))!.Money;
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect, a => a.CardId = 67);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var releido = SaveUtil.GetSaveFile(ruta)!;
        Assert.Equal(Math.Min((long)releido.MaxMoney, (long)baseFija + 25_000), (long)releido.Money);
    }

    [Fact]
    public async Task Carta67_RespetaAmountDeLaApp()
    {
        var baseFija = SaveUtil.GetSaveFile(Path.Combine(AppContext.BaseDirectory, "testdata", "hgss.sav"))!.Money;
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect,
            a => { a.CardId = 67; a.Params = P("""{"amount":50000}"""); });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var releido = SaveUtil.GetSaveFile(ruta)!;
        Assert.Equal(Math.Min((long)releido.MaxMoney, (long)baseFija + 50_000), (long)releido.Money);
    }

    [Fact]
    public async Task Carta70_PeonDeObra_Suma128Ev()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect,
            a => { a.CardId = 70; a.Params = P("""{"stat":"atk"}"""); });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(10 + 128, p.EV_ATK);
    }

    [Fact]
    public async Task Carta78_JefeCuadrilla_EvMaximoLegal()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect,
            a => { a.CardId = 78; a.Params = P("""{"stat":"spe"}"""); });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.True(p.EV_SPE >= 152 && p.EV_SPE <= 252, $"EV SPE={p.EV_SPE} fuera del tope legal");
        Assert.True(p.EV_HP + p.EV_ATK + p.EV_DEF + p.EV_SPA + p.EV_SPD + p.EV_SPE <= 510);
    }

    [Fact]
    public async Task Carta81_JefeDeObra_RepartoExacto()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect, a => a.CardId = 81);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(252, p.EV_ATK);
        Assert.Equal(252, p.EV_SPE);
        Assert.Equal(6, p.EV_HP);
        // Carta 81 usa el reparto de EVs del diálogo (252/252/6 = 510).
        Assert.Equal(510, p.EV_HP + p.EV_ATK + p.EV_DEF + p.EV_SPA + p.EV_SPD + p.EV_SPE);
    }

    [Fact]
    public async Task Carta74_Medicina_Exito_Ivs31()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect,
            a => { a.CardId = 74; a.Params = P("""{"success":true}"""); });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(31, p.IV_HP);
        Assert.Equal(31, p.IV_ATK);
        Assert.Equal(31, p.IV_SPE);
    }

    [Fact]
    public async Task Carta74_Medicina_Fallo_IvsCero()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect,
            a => { a.CardId = 74; a.Params = P("""{"success":false}"""); });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(0, p.IV_HP);
        Assert.Equal(0, p.EV_ATK);
    }

    [Fact]
    public async Task Carta41_EntrenoDiario_Iv31()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect,
            a => { a.CardId = 41; a.Params = P("""{"stat":"hp"}"""); });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        Assert.Equal(31, SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0).IV_HP);
    }

    [Fact]
    public async Task Carta42_EntrenoIntenso_DosIvs31()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect,
            a => { a.CardId = 42; a.Params = P("""{"stats":["def","spd"]}"""); });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(31, p.IV_DEF);
        Assert.Equal(31, p.IV_SPD);
    }

    [Fact]
    public async Task Carta83_Zenkai_ReviveYSube()
    {
        var (ruta, res, _) = await Ejecutar(sav =>
        {
            var p = sav.GetPartySlotAtIndex(0);
            p.Stat_HPCurrent = 0;
            p.RefreshChecksum();
            sav.SetPartySlotAtIndex(p, 0);
        }, AccionTipo.CardEffect, a => { a.CardId = 83; a.Params = P("""{"stats":["atk","spe"]}"""); });

        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var q = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(q.Stat_HPMax, q.Stat_HPCurrent);
        Assert.Equal(31, q.IV_ATK);
        Assert.Equal(31, q.IV_SPE);
        Assert.True(q.EV_ATK == 252 || q.EV_ATK + q.EV_SPE < 510);
    }

    [Fact]
    public async Task Carta84_EjemploASeguir_Ivs31YEvs()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.CardEffect, a => a.CardId = 84);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(31, p.IV_HP); Assert.Equal(31, p.IV_ATK); Assert.Equal(31, p.IV_DEF);
        Assert.Equal(31, p.IV_SPA); Assert.Equal(31, p.IV_SPD); Assert.Equal(31, p.IV_SPE);
        // El editor propone 4 HP / 252 ATK / 252 SPE = 508 EVs (el usuario puede cambiarlo en el diálogo).
        Assert.Equal(508, p.EV_HP + p.EV_ATK + p.EV_DEF + p.EV_SPA + p.EV_SPD + p.EV_SPE);
        Assert.Equal(252, p.EV_ATK);
        Assert.Equal(252, p.EV_SPE);
    }

    [Fact]
    public async Task Carta87_Sabotaje_IvCero_EnRival()
    {
        var (ruta, res, _) = await Ejecutar(sav =>
        {
            // poner un Pikachu en el slot 2 para que la carta del rival lo encuentre
            var pkm = sav.BlankPKM;
            pkm.Species = 25; pkm.Version = sav.Version;
            pkm.EXP = Experience.GetEXP(50, (byte)sav.Personal.GetFormEntry(25, 0).EXPGrowth);
            pkm.IV_ATK = 20; pkm.IV_SPE = 20;
            pkm.RefreshChecksum();
            sav.SetPartySlotAtIndex(pkm, 2);
        }, AccionTipo.CardEffect, a =>
        {
            a.CardId = 87;
            a.CreatedBy = "Kakkaroto_9062";
            a.Params = P("""{"stats":["atk","spe"],"targetSpeciesId":25}""");
        });

        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(2);
        Assert.Equal(25, p.Species);
        Assert.Equal(0, p.IV_ATK);
        Assert.Equal(0, p.IV_SPE);
    }

    [Fact]
    public async Task Carta88_SinObjetivo_QuedaPendiente()
    {
        var ruta = PrepararReal();
        var fb = new FakeFirebase().Agregar("c88", Ayuda.Accion(AccionTipo.CardEffect, a =>
        {
            a.CardId = 88;
            a.Params = P("""{"stats":["atk","def","spa","spd"],"targetSpeciesId":999}"""); // especie inexistente
        }, "Heartgold"));
        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);

        Assert.Equal(AccionEstado.Pendiente, res.Single().Estado);
        Assert.Contains("No encuentro el Pokémon objetivo", res.Single().ResultMessage);
        Assert.Equal(AccionEstado.Pendiente, fb.Cola["c88"].Status);
    }

    [Fact]
    public async Task CartaFueraDeCatalogo_Skipped_SinTocarSave()
    {
        var ruta = PrepararReal();
        var antes = File.ReadAllBytes(ruta);
        var fb = new FakeFirebase().Agregar("c31", Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = 31, "Heartgold"));
        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);

        Assert.Equal(AccionEstado.Omitida, res.Single().Estado);
        Assert.Equal(antes, File.ReadAllBytes(ruta));
    }

    [Fact]
    public async Task Gacha_PokemonNuevoEnCajaConBolaYShiny()
    {
        // equipo lleno → el Gachapón va a la caja
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.GachaPokemon, a =>
        {
            a.SpeciesId = 25; a.SpeciesName = "Pikachu";
            a.IsShiny = true; a.Ball = "ULTRABALL"; a.GameGen = 4;
        });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var sav = SaveUtil.GetSaveFile(ruta)!;
        var encontrado = Enumerable.Range(0, sav.BoxCount)
            .SelectMany(b => Enumerable.Range(0, sav.GetBoxData(b).Length).Select(s => (b, s)))
            .Select(x => sav.GetBoxSlotAtIndex(x.b, x.s))
            .FirstOrDefault(p => p.Species == 25 && p.IsShiny);
        Assert.NotNull(encontrado);
        Assert.Equal((int)Ball.Ultra, encontrado!.Ball);
    }

    [Fact]
    public async Task Gacha_ConHuecoEnEquipo_VaAlEquipo()
    {
        var (ruta, res, _) = await Ejecutar(sav => sav.SetPartySlotAtIndex(sav.BlankPKM, 5),
            AccionTipo.GachaPokemon, a =>
            {
                a.SpeciesId = 133; a.SpeciesName = "Eevee";
                a.IsShiny = false; a.Ball = "POKEBALL"; a.GameGen = 4;
            });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(5);
        Assert.Equal(133, p.Species);
        Assert.Equal(1, p.Stat_Level);   // regla del gachapón: siempre nivel 1
        Assert.True(p.Stat_HPMax > 0);
    }

    [Fact]
    public async Task Gacha_EspecieInexistente_Failed()
    {
        var (_, res, _) = await Ejecutar(null, AccionTipo.GachaPokemon, a =>
        {
            a.SpeciesId = 888; a.SpeciesName = "Zacian"; a.Ball = "POKEBALL";
        });
        Assert.Equal(AccionEstado.Fallida, res.Single().Estado);
        Assert.Contains("no está presente", res.Single().ResultMessage);
    }

    [Fact]
    public async Task ItemGrant_ObjetoNuevo_SeAnadeAMochila()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.ItemGrant, a =>
        {
            a.AppItemId = 80; a.ItemName = "Master Ball"; a.Quantity = 1;
        });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        var bag = SaveUtil.GetSaveFile(ruta)!.Inventory;
        Assert.True(bag.Pouches.Any(p => p.Items.Any(i => i.Index == 1 && i.Count > 0)),
            "la Master Ball no quedó en la mochila");
    }

    [Fact]
    public async Task ItemGrant_ObjetoInexistente_Failed()
    {
        var (_, res, _) = await Ejecutar(null, AccionTipo.ItemGrant, a =>
        {
            a.AppItemId = 9999; a.ItemName = "Objeto inventado"; a.Quantity = 1;
        });
        Assert.Equal(AccionEstado.Fallida, res.Single().Estado);
        Assert.Contains("no encontrado", res.Single().ResultMessage);
    }

    [Fact]
    public async Task Menta_CambiaNaturaleza()
    {
        var (ruta, res, _) = await Ejecutar(null, AccionTipo.MintUse, a =>
        {
            a.Nature = "TIMID"; a.AppItemId = 42;
        });
        Assert.True(res.Single().Estado == AccionEstado.Aplicada, $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        Assert.Equal(Nature.Timid, SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0).Nature);
    }

    [Fact]
    public async Task Minima_PokemonDeCaja_TambienEsObjetivo()
    {
        // El "muerto" de un Nuzlocke suele estar en una caja: debe poder elegirse como objetivo
        var (ruta, res, _) = await Ejecutar(sav =>
        {
            var pkm = sav.BlankPKM;
            pkm.Species = 133; pkm.Version = sav.Version;
            pkm.EXP = Experience.GetEXP(30, (byte)sav.Personal.GetFormEntry(133, 0).EXPGrowth);
            pkm.IV_HP = 5;
            pkm.RefreshChecksum();
            sav.SetBoxSlotAtIndex(pkm, 0, 0);
        }, AccionTipo.CardEffect, a => { a.CardId = 41; a.Params = P("""{"stat":"hp"}"""); });

        // el primer objetivo del diálogo automático es el equipo; forzamos el de caja
        var ruta2 = ruta;
        var fb = new FakeFirebase().Agregar("b1", Ayuda.Accion(AccionTipo.CardEffect,
            a => { a.CardId = 41; a.Params = P("""{"stat":"hp"}"""); }, "Heartgold"));
        var servicio = Ayuda.Servicio(fb, Ayuda.DirDatosTemporal());
        servicio.PedirObjetivo = (_, objetivos) => objetivos.First(o => o.Ubicacion.Box == 0 && o.Ubicacion.Slot == 0).Ubicacion;
        var res2 = await servicio.ProcesarColaAsync("l", "p", ruta2);

        Assert.Equal(AccionEstado.Aplicada, res2.Single().Estado);
        Assert.Equal(31, SaveUtil.GetSaveFile(ruta2)!.GetBoxSlotAtIndex(0, 0).IV_HP);
        _ = res;   // el primer lote no se usa (solo preparaba el estado)
    }
}

/// <summary>
/// Generaciones 5/6/7: los saves EN BLANCO no son exportables a disco (metadata de firma),
/// así que se valida el pipeline de PKHeX en memoria: la acción cambia el save y
/// <c>sav.Write()</c> deja <c>ChecksumsValid == true</c> sin alterar el tamaño.
/// </summary>
public class GeneracionesTests
{
    private static SaveFile EnBlanco(GameVersion version)
    {
        var sav = BlankSaveFile.Get(version, "TEST", LanguageID.Spanish);
        Ayuda.RellenarEquipo(sav, 6);
        return sav;
    }

    [Theory]
    [InlineData(GameVersion.B)]
    [InlineData(GameVersion.X)]
    [InlineData(GameVersion.SN)]
    public void DineroYEvs_WriteDejaChecksumsValidos(GameVersion version)
    {
        var sav = EnBlanco(version);
        var tamano = sav.Data.Length;

        EfectosCartas.DarDinero(sav);
        var pkm = sav.GetPartySlotAtIndex(0);
        EfectosCartas.AnadirEvs(pkm, "atk", 128);
        sav.SetPartySlotAtIndex(pkm, 0);

        var bytes = sav.Write(BinaryExportSetting.None).ToArray();
        Assert.True(sav.ChecksumsValid, $"{version}: checksums inválidos ({sav.ChecksumInfo})");
        Assert.Equal(tamano, bytes.Length);
        Assert.Equal(Math.Min(999_999, sav.MaxMoney), (int)sav.Money);
        Assert.Equal(10 + 128, sav.GetPartySlotAtIndex(0).EV_ATK);
    }

    [Theory]
    [InlineData(GameVersion.B)]
    [InlineData(GameVersion.X)]
    [InlineData(GameVersion.SN)]
    public void Gacha_PokemonNuevo_ConBolaYChecksumsValidos(GameVersion version)
    {
        var sav = EnBlanco(version);
        var (pkm, _) = InyectorEquipo.ConstruirPkmGacha(sav, 25, isShiny: false, Ball.Poke, "Pika");
        var (caja, slot) = BuscarHuecoCaja(sav);
        sav.SetBoxSlotAtIndex(pkm, caja, slot);

        var bytes = sav.Write(BinaryExportSetting.None).ToArray();
        Assert.True(sav.ChecksumsValid, $"{version}: checksums inválidos ({sav.ChecksumInfo})");
        Assert.Equal(sav.Data.Length, bytes.Length);
        Assert.Equal(25, sav.GetBoxSlotAtIndex(caja, slot).Species);
    }

    [Theory]
    [InlineData(GameVersion.B)]
    [InlineData(GameVersion.X)]
    [InlineData(GameVersion.SN)]
    public void Menta_SeAplicaYChecksumsValidos(GameVersion version)
    {
        var sav = EnBlanco(version);
        var pkm = sav.GetPartySlotAtIndex(0);
        EfectosCartas.DarMenta(pkm, Nature.Adamant);
        sav.SetPartySlotAtIndex(pkm, 0);

        var bytes = sav.Write(BinaryExportSetting.None).ToArray();
        Assert.True(sav.ChecksumsValid, $"{version}: checksums inválidos ({sav.ChecksumInfo})");
        Assert.Equal(sav.Data.Length, bytes.Length);
        Assert.Equal(Nature.Adamant, sav.GetPartySlotAtIndex(0).Nature);
    }
    private static InventoryType tipoDestino;

    private static (int Caja, int Slot) BuscarHuecoCaja(SaveFile sav)
    {
        for (var b = 0; b < sav.BoxCount; b++)
        {
            var datos = sav.GetBoxData(b);
            for (var s = 0; s < datos.Length; s++)
                if (datos[s].Species == 0) return (b, s);
        }
        return (0, 0);
    }
}