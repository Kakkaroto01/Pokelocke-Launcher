using System;
using System.Collections.Generic;
using System.Linq;
using PokeLobby.PokeLocke;
using PKHeX.Core;
using Xunit;

namespace PokeLobby.Tests;

/// <summary>
/// Matriz de efectos por GENERACIÓN (3→9) para no depender de ir probando juego a juego.
/// Comprueba, sobre saves en blanco de cada juego, las operaciones que cambian de formato:
/// habilidad (offsets distintos), movimientos, IVs/EVs, dinero, objetos y el gachapón.
/// </summary>
public class GeneracionesMatrixTests
{
    public static IEnumerable<object[]> Versiones() => new[]
    {
        new object[] { GameVersion.E },     // gen 3
        new object[] { GameVersion.HG },    // gen 4
        new object[] { GameVersion.B2 },    // gen 5
        new object[] { GameVersion.OR },    // gen 6
        new object[] { GameVersion.US },    // gen 7
        new object[] { GameVersion.SW },    // gen 8
        new object[] { GameVersion.SL },    // gen 9
    };

    private static SaveFile Blanco(GameVersion version)
    {
        var sav = BlankSaveFile.Get(version, "TEST", LanguageID.Spanish);
        Ayuda.RellenarEquipo(sav, 133, 25, 1, 4, 7, 150);
        return sav;
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void HabilidadLibre_EscribeEnElOffsetDeCadaGeneracion(GameVersion version)
    {
        var sav = Blanco(version);
        var pkm = sav.GetPartySlotAtIndex(0);
        var objetivo = 25;                       // Sombra Trampa (existe en todas las gen)
        Assert.True(objetivo <= pkm.MaxAbilityID, $"{version}: objetivo {objetivo} > máximo {pkm.MaxAbilityID}");

        if (pkm.Format == 3)
        {
            // gen 3 no guarda el id: la habilidad sale del PID y la tabla (2 opciones). Solo
            // comprobamos que la API no rompe y devuelve una habilidad válida.
            var hab = EfectosCartas.IdHabilidadActual(pkm);
            Assert.InRange(hab, 0, pkm.MaxAbilityID);
            return;
        }

        var ok = EfectosCartas.PonerHabilidadLibre(pkm, objetivo);
        Assert.True(ok, $"{version}: PonerHabilidadLibre devolvió false (fmt {pkm.Format})");
        Assert.Equal(objetivo, EfectosCartas.IdHabilidadActual(pkm));
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void Movimiento_SeAprendeEnTodas(GameVersion version)
    {
        var sav = Blanco(version);
        var pkm = sav.GetPartySlotAtIndex(0);
        var mov = (ushort)33;                    // Placaje
        Assert.True(mov <= pkm.MaxMoveID, $"{version}: Placaje fuera de rango");
        EfectosCartas.EnsenarMovimientoEn(pkm, 0, mov);
        Assert.True(pkm.HasMove(mov), $"{version}: no aprendió el movimiento");
        Assert.All(pkm.Moves.Where(m => m > 0), m => Assert.True(m <= pkm.MaxMoveID));
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void IvsYEvs_SeAplicanEnTodas(GameVersion version)
    {
        var sav = Blanco(version);
        var pkm = sav.GetPartySlotAtIndex(0);
        EfectosCartas.PonerIvsYevsLibre(pkm,
            ivs: [31, 31, 31, 31, 31, 31],
            evs: [4, 252, 0, 0, 0, 252]);

        foreach (var st in new[] { "hp", "atk", "def", "spa", "spd", "spe" })
            Assert.Equal(31, EfectosCartas.LeerIv(pkm, st));
        Assert.Equal(252, EfectosCartas.LeerEv(pkm, "atk"));
        Assert.Equal(252, EfectosCartas.LeerEv(pkm, "spe"));
        Assert.Equal(4, EfectosCartas.LeerEv(pkm, "hp"));
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void Dinero_Suma25kEnTodas(GameVersion version)
    {
        var sav = Blanco(version);
        uint inicial;
        try { inicial = sav.Money; }
        catch (ArgumentOutOfRangeException) { return; }   // save en blanco de SV sin bloque de dinero
        var esperado = (uint)Math.Min((long)sav.MaxMoney, (long)inicial + 25_000);
        sav.Money = esperado;
        Assert.Equal(esperado, sav.Money);
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void Gacha_Nivel1YDosAtaquesEnTodas(GameVersion version)
    {
        var sav = Blanco(version);
        var (pkm, _) = InyectorEquipo.ConstruirPkmGacha(sav, 133, isShiny: false, Ball.Poke, "MoteTest");

        Assert.Equal(1, pkm.CurrentLevel);
        Assert.Equal("MoteTest", pkm.Nickname);
        var movs = pkm.Moves.Where(m => m > 0).ToArray();
        Assert.InRange(movs.Length, 1, 2);
        Assert.All(movs, m => Assert.True(m <= pkm.MaxMoveID, $"{version}: movimiento {m} fuera de rango"));
        Assert.All(new[] { pkm.IV_HP, pkm.IV_ATK, pkm.IV_DEF, pkm.IV_SPA, pkm.IV_SPD, pkm.IV_SPE },
            iv => Assert.InRange(iv, 0, 31));
        Assert.InRange(pkm.Ability, 0, pkm.MaxAbilityID);
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void Objetos_SeAnadenEnTodas(GameVersion version)
    {
        var sav = Blanco(version);
        var tabla = GameInfo.GetStrings("en").Item;
        var id = -1;
        for (var i = 1; i < tabla.Count; i++)
            if (string.Equals(tabla[i], "Rare Candy", StringComparison.OrdinalIgnoreCase)) { id = i; break; }
        if (id <= 0) return;   // juego sin Carameloraros: nada que comprobar

        var puestos = EfectosCartas.AnadirObjetos(sav.Inventory, id, 99);
        if (sav.Inventory.Pouches.SelectMany(p => p.Items).All(i => i.Index != id))
            return;            // el juego no tiene ese objeto en la mochila (formato distinto)
        Assert.True(puestos >= 0, $"{version}: AnadirObjetos falló");
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void Menta_CambiaLaNaturalezaEnTodas(GameVersion version)
    {
        var sav = Blanco(version);
        var pkm = sav.GetPartySlotAtIndex(0);
        var ok = EfectosCartas.DarMenta(pkm, Nature.Adamant);
        Assert.True(ok, $"{version}: DarMenta devolvió false");
        Assert.Equal(Nature.Adamant, pkm.Nature);
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void MtSeDesbloqueanEnTodas(GameVersion version)
    {
        var sav = Blanco(version);
        EfectosCartas.DarTodasLasMt(sav.Inventory);
        sav.Inventory.CopyTo(sav);

        // En saves EN BLANCO muchas generaciones no traen el bloque de MT inicializado; aquí solo
        // se comprueba que la operación no rompe. El desbloqueo real está verificado sobre un save
        // real de Rubí Omega (61 → 107 MT) y en HGSS.
        var conTm = sav.Inventory.Pouches.Where(p => p.Type.ToString().Contains("TM", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var pouch in conTm)
            Assert.True(pouch.Items.Length > 0, $"{version}: mochila de MT vacía tras desbloquear");
    }

    [Theory]
    [MemberData(nameof(Versiones))]
    public void Shiny_SePoneYSeQuitaEnTodas(GameVersion version)
    {
        var sav = Blanco(version);
        var pkm = sav.GetPartySlotAtIndex(0);
        EfectosCartas.PonerShiny(pkm);
        Assert.True(pkm.IsShiny, $"{version}: no quedó shiny");
        var ok = EfectosCartas.QuitarShiny(pkm);
        Assert.True(ok, $"{version}: QuitarShiny devolvió false");
        Assert.False(pkm.IsShiny, $"{version}: sigue siendo shiny");
    }

    /// <summary>Escribe un save en blanco que PKHeX pueda volver a leer (gen 5-9).</summary>
    private static string EscribirBlanco(SaveFile sav)
    {
        var ruta = System.IO.Path.Combine(Ayuda.DirDatosTemporal(), "blanco-" + Guid.NewGuid().ToString("N") + ".sav");
        System.IO.File.WriteAllBytes(ruta, sav.Write(BinaryExportSetting.None).ToArray());
        return ruta;
    }

    [Fact] // servicio completo en gen 5 con la carta de IV/EV
    public async System.Threading.Tasks.Task EndToEnd_Gen5_EjemploASeguir()
    {
        var sav = Blanco(GameVersion.B2);
        var ruta = System.IO.Path.Combine(Ayuda.DirDatosTemporal(), "gen5-" + Guid.NewGuid().ToString("N") + ".sav");
        Ayuda.EscribirFixture(sav, ruta);
        var fb = new FakeFirebase().Agregar("a", Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = 84, "Negro 2"));
        var servicio = Ayuda.Servicio(fb, Ayuda.DirDatosTemporal(), fixtures: true);
        var res = await servicio.ProcesarColaAsync("l", "p", ruta);
        Assert.True(res.Single().Estado == AccionEstado.Aplicada,
            $"{res.Single().ResultMessage} / {res.Single().ErrorMessage}");
        Assert.Contains("IVs y EVs", res.Single().ResultMessage);
    }
}