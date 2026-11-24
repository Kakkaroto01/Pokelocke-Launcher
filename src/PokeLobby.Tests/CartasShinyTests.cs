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
/// Cartas 36 (Glow up), 56 (Cambio color) y 59 (Muda de piel), según la app Android:
/// funcionan sobre EQUIPO y CAJAS (el selector del launcher muestra ambos, estilo PKHeX).
/// </summary>
public class CartasShinyTests
{
    /// <summary>Prepara una copia del save real y aplica la carta, eligiendo el hueco con <paramref name="filtro"/>.</summary>
    private static async Task<(string Ruta, List<ResultadoAccion> Res, FakeFirebase Fb)> Ejecutar(
        Action<SaveFile>? ajustar, int cardId, Func<ObjetivoPkm, bool>? filtro = null, string juego = "Heartgold")
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
        var fb = new FakeFirebase().Agregar("accion", Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = cardId, juego));
        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal(), filtrarObjetivo: filtro)
            .ProcesarColaAsync("l", "p", ruta);
        return (ruta, res, fb);
    }

    /// <summary>Primer hueco de caja con un Pokémon en el save (o (-1,-1)).</summary>
    private static (int Caja, int Slot) PrimeraCajaConPokemon(SaveFile sav)
    {
        for (var caja = 0; caja < sav.BoxCount; caja++)
        {
            var datos = sav.GetBoxData(caja);
            for (var slot = 0; slot < datos.Length; slot++)
                if (datos[slot].Species > 0) return (caja, slot);
        }
        return (-1, -1);
    }

    [Fact] // 36 en el equipo
    public async Task Carta36_GlowUp_Equipo()
    {
        var (ruta, res, _) = await Ejecutar(null, 36);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");

        var p = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.Equal(31, p.IV_HP);
        Assert.Equal(31, p.IV_ATK);
        Assert.Equal(31, p.IV_DEF);
        Assert.Equal(31, p.IV_SPA);
        Assert.Equal(31, p.IV_SPD);
        Assert.Equal(31, p.IV_SPE);
        Assert.True(p.IsShiny, "el Pokémon debería haber quedado shiny");
    }

    [Fact] // 36 en una CAJA
    public async Task Carta36_GlowUp_Caja()
    {
        (int Caja, int Slot) objetivo = (-1, -1);
        var (ruta, res, _) = await Ejecutar(sav =>
        {
            objetivo = PrimeraCajaConPokemon(sav);
            Assert.True(objetivo.Caja >= 0, "el save de test no tiene Pokémon en cajas");
            var p = sav.GetBoxSlotAtIndex(objetivo.Caja, objetivo.Slot);
            p.IV_HP = 20; p.IV_ATK = 20; p.IV_DEF = 20;
            p.IV_SPA = 20; p.IV_SPD = 20; p.IV_SPE = 20;
            p.RefreshChecksum();
            sav.SetBoxSlotAtIndex(p, objetivo.Caja, objetivo.Slot);
        }, 36, o => !o.Ubicacion.EsEquipo && o.Ubicacion.Box == objetivo.Caja && o.Ubicacion.Slot == objetivo.Slot);

        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");

        var p2 = SaveUtil.GetSaveFile(ruta)!.GetBoxSlotAtIndex(objetivo.Caja, objetivo.Slot);
        Assert.Equal(31, p2.IV_ATK);
        Assert.Equal(31, p2.IV_SPE);
        Assert.True(p2.IsShiny, "el Pokémon de caja debería haber quedado shiny");
    }

    [Fact] // 56 en el equipo: normal → shiny
    public async Task Carta56_CambioColor_Equipo_NormalAShiny()
    {
        var (ruta, res, _) = await Ejecutar(null, 56);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        Assert.True(SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0).IsShiny);
    }

    [Fact] // 56 en CAJA: shiny → normal
    public async Task Carta56_CambioColor_Caja_ShinyANormal()
    {
        (int Caja, int Slot) objetivo = (-1, -1);
        var (ruta, res, _) = await Ejecutar(sav =>
        {
            objetivo = PrimeraCajaConPokemon(sav);
            Assert.True(objetivo.Caja >= 0, "el save de test no tiene Pokémon en cajas");
            var p = sav.GetBoxSlotAtIndex(objetivo.Caja, objetivo.Slot);
            p.SetShiny();
            sav.SetBoxSlotAtIndex(p, objetivo.Caja, objetivo.Slot);
        }, 56, o => !o.Ubicacion.EsEquipo && o.Ubicacion.Box == objetivo.Caja && o.Ubicacion.Slot == objetivo.Slot);

        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        Assert.False(SaveUtil.GetSaveFile(ruta)!.GetBoxSlotAtIndex(objetivo.Caja, objetivo.Slot).IsShiny,
            "el Pokémon de caja debería haber dejado de ser shiny");
    }

    [Fact] // 59 en el equipo: shiny debilitado → revivido y sin shiny
    public async Task Carta59_MudaDePiel_Equipo_ReviveYQuitaShiny()
    {
        var (ruta, res, _) = await Ejecutar(sav =>
        {
            var p = sav.GetPartySlotAtIndex(0);
            p.SetShiny();
            p.Stat_HPCurrent = 0;
            sav.SetPartySlotAtIndex(p, 0);
        }, 59);

        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");

        var p2 = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0);
        Assert.False(p2.IsShiny, "el Pokémon debería haber dejado de ser shiny");
        Assert.True(p2.Stat_HPCurrent > 0, "el Pokémon debería estar curado");
    }

    [Fact] // 59 en CAJA: shiny → sin shiny (no hay HP que revivir)
    public async Task Carta59_MudaDePiel_Caja_QuitaShiny()
    {
        (int Caja, int Slot) objetivo = (-1, -1);
        var (ruta, res, _) = await Ejecutar(sav =>
        {
            objetivo = PrimeraCajaConPokemon(sav);
            Assert.True(objetivo.Caja >= 0, "el save de test no tiene Pokémon en cajas");
            var p = sav.GetBoxSlotAtIndex(objetivo.Caja, objetivo.Slot);
            p.SetShiny();
            sav.SetBoxSlotAtIndex(p, objetivo.Caja, objetivo.Slot);
        }, 59, o => !o.Ubicacion.EsEquipo && o.Ubicacion.Box == objetivo.Caja && o.Ubicacion.Slot == objetivo.Slot);

        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        Assert.Contains("caja", res.Single().ResultMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(SaveUtil.GetSaveFile(ruta)!.GetBoxSlotAtIndex(objetivo.Caja, objetivo.Slot).IsShiny);
    }

    [Fact] // 59 sin candidatos: Omitida (no aplicable), no failed
    public async Task Carta59_SinCandidatos_QuedaOmitida()
    {
        var (_, res, _) = await Ejecutar(null, 59);
        Assert.Equal(AccionEstado.Omitida, res.Single().Estado);
        Assert.Contains("shiny", res.Single().ResultMessage, StringComparison.OrdinalIgnoreCase);
    }
}
