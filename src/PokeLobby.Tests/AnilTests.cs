using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeLobby.PokeLocke;
using Xunit;

namespace PokeLobby.Tests;

/// <summary>
/// Backend de **Pokémon Añil** (.rxdata): se genera una partida sintética con el propio escritor
/// de Ruby Marshal y un PBS mínimo, y se comprueban los efectos igual que en un save real.
/// </summary>
public class AnilTests
{
    // ── utilidades para construir la partida sintética ──

    private static RubyObject Mon(string especieInterna)
    {
        var mon = new RubyObject("PokeBattle_Pokemon");
        mon.Set("@species", new RubySymbol(especieInterna));
        mon.Set("@name", null);
        mon.Set("@level", 5);
        mon.Set("@exp", 100);
        mon.Set("@hp", 20);
        mon.Set("@totalhp", 20);
        mon.Set("@shiny", false);
        mon.Set("@super_shiny", false);
        mon.Set("@form", 0);
        mon.Set("@gender", 0);
        mon.Set("@happiness", 70);
        mon.Set("@personalID", 123456L);
        mon.Set("@item", null);
        mon.Set("@ability", new RubySymbol("RUNAWAY"));
        mon.Set("@nature", new RubySymbol("HARDY"));
        mon.Set("@iv", new List<object> { 10, 10, 10, 10, 10, 10 });
        mon.Set("@ev", new List<object> { 0, 0, 0, 0, 0, 0 });
        var mov = new RubyObject("PokeBattle_Move");
        mov.Set("@id", new RubySymbol("TACKLE"));
        mov.Set("@pp", 35);
        mov.Set("@ppup", 0);
        mon.Set("@moves", new List<object> { mov });
        return mon;
    }

    private static string EscribirPbs(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pokemon.txt"),
            "# sintético\n[BULBASAUR]\nName = Bulbasaur\nTypes = GRASS,POISON\nBaseStats = 45,49,49,45,65,65\nAbilities = OVERGROW\n[EEVEE]\nName = Eevee\nTypes = NORMAL\nBaseStats = 55,55,50,55,75,65\nAbilities = RUNAWAY\n");
        File.WriteAllText(Path.Combine(dir, "abilities.txt"), "[RUNAWAY]\nName = Fuga\n[OVERGROW]\nName = Espesura\n");
        File.WriteAllText(Path.Combine(dir, "moves.txt"), "[TACKLE]\nName = Placaje\nType = NORMAL\nPower = 40\nPP = 35\n");
        File.WriteAllText(Path.Combine(dir, "items.txt"), "[POTION]\nName = Poción\nPocket = 2\n[TM01]\nName = MT01\nPocket = 4\n[TM02]\nName = MT02\nPocket = 4\n");
        return dir;
    }

    private static string CrearPartida(string ruta, string pbsDir)
    {
        var party = new List<object> { Mon("EEVEE"), Mon("BULBASAUR") };
        var entrenador = new RubyObject("PokeBattle_Trainer");
        entrenador.Set("@name", "TEST");
        entrenador.Set("@money", 1000L);
        entrenador.Set("@party", party);

        var caja = new RubyObject("PokemonStorageBox");
        caja.Set("@name", "Caja 1");
        caja.Set("@pokemon", new List<object>(Enumerable.Repeat<object?>(null, 30).Select(x => x!)));
        var storage = new RubyObject("PokemonStorage");
        storage.Set("@boxes", new List<object> { caja });

        var pocket = new List<object>();
        pocket.Add(new List<object> { new RubySymbol("POTION"), 3 });
        var bolsillos = new List<object> { new List<object>(), pocket, new List<object>(), new List<object>() };
        var bag = new RubyObject("PokemonBag");
        bag.Set("@pockets", bolsillos);

        var root = new RubyObject("Game");
        root.Set("@trainer", entrenador);
        root.Set("@storage", storage);
        root.Set("@bag", bag);

        using var fs = File.Create(ruta);
        var writer = new RubyMarshalWriter(fs);
        writer.WriteHeader();
        writer.WriteValue(root);
        return ruta;
    }

    private static PartidaAnil Nueva(out string pbs)
    {
        var baseDir = Path.Combine(Ayuda.DirDatosTemporal(), "anil-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        pbs = EscribirPbs(Path.Combine(baseDir, "PBS"));
        var ruta = Path.Combine(baseDir, "Partida 1.rxdata");
        CrearPartida(ruta, pbs);
        return PartidaAnil.Cargar(ruta, pbs);
    }

    // ── pruebas ──

    [Fact]
    public void CargaYGuarda_ConservaDineroYEquipo()
    {
        var partida = Nueva(out _);
        Assert.Equal(2, partida.Equipo.Count);
        Assert.Equal(1000u, partida.Dinero);

        partida.Dinero = 25000;
        partida.Guardar();

        var re = PartidaAnil.Cargar(partida.Ruta, partida.CarpetaPbs);
        Assert.Equal(25000u, re.Dinero);
        Assert.Equal(2, re.Equipo.Count);
    }

    [Fact]
    public void Gacha_CreaNivel1ConMoteShinyYAtaquesDelJuego()
    {
        var partida = Nueva(out _);
        var donde = partida.CrearGacha(133, "Eevee", shiny: true, mote: "Chispa", out var nota);
        Assert.NotNull(donde);
        Assert.Null(nota);
        partida.Guardar();

        var re = PartidaAnil.Cargar(partida.Ruta, partida.CarpetaPbs);
        var nuevo = re.Equipo.Last();
        Assert.Equal(1, nuevo.Level);
        Assert.Equal("Chispa", nuevo.Nickname);
        Assert.True(nuevo.IsShiny);
        Assert.InRange(nuevo.Moves.Count, 1, 2);
        Assert.All(nuevo.IVs, iv => Assert.InRange(iv, 0, 31));
    }

    [Fact]
    public void Cartas_DineroIvHabilidadMovimientoMtYObjeto()
    {
        var partida = Nueva(out _);
        var aplicador = new AplicadorAnil
        {
            PedirPokemon = (_, _) => 0,
            PedirStat = _ => 1,
            PedirHabilidad = (_, _) => "OVERGROW",
            PedirMovimiento = (_, _) => "TACKLE",
            PedirEdicionStats = (_, _, _, _) => (new[] { 31, 31, 31, 31, 31, 31 }, new[] { 0, 252, 0, 0, 0, 252 }),
        };

        AccionLauncher Carta(int id) => new() { Type = AccionTipo.CardEffect, CardId = id, Schema = 1, GameName = " Pokémon Añil " };
        void A(int id)
        {
            var r = aplicador.Aplicar(Carta(id), partida);
            Assert.True(r.Estado == AccionEstado.Aplicada, $"carta {id}: {r.ResultMessage} / {r.ErrorMessage}");
        }

        A(67);   // dinero
        A(41);   // IV 31
        A(63);   // habilidad
        A(66);   // movimiento
        A(84);   // IVs/EVs
        A(65);   // MT
        var objeto = aplicador.Aplicar(new AccionLauncher { Type = AccionTipo.ItemGrant, ItemName = "POTION", Quantity = 2, Schema = 1, GameName = "Añil" }, partida);
        Assert.Equal(AccionEstado.Aplicada, objeto.Estado);
        partida.Guardar();

        var re = PartidaAnil.Cargar(partida.Ruta, partida.CarpetaPbs);
        Assert.Equal(26000u, re.Dinero);
        var mon = re.Equipo[0];
        Assert.Equal(31, mon.IVs[1]);
        Assert.Contains(mon.Ability, new[] { "Espesura", "OVERGROW" });   // el PBS mínimo puede devolver el nombre interno
        Assert.Contains(mon.Moves[0].Name, new[] { "Placaje", "TACKLE" });   // el PBS mínimo puede devolver el interno
        Assert.Equal(31, mon.IVs[0]);
        Assert.Equal(252, mon.EVs[1]);
        Assert.True(re.Mochila.Any(i => string.Equals(i.InternalName, "POTION", StringComparison.OrdinalIgnoreCase) && i.Quantity >= 5));
        Assert.True(re.Mochila.Count(i => i.InternalName.StartsWith("TM", StringComparison.OrdinalIgnoreCase)) > 0);
    }

    [Fact]
    public void MentaCambiaNaturalezaYShinySePoneYSeQuita()
    {
        var partida = Nueva(out _);
        var aplicador = new AplicadorAnil { PedirPokemon = (_, _) => 0 };
        var menta = aplicador.Aplicar(new AccionLauncher { Type = AccionTipo.MintUse, Nature = "ADAMANT", Schema = 1, GameName = "Añil" }, partida);
        Assert.Equal(AccionEstado.Aplicada, menta.Estado);
        Assert.Equal(AccionEstado.Aplicada, aplicador.Aplicar(new AccionLauncher { Type = AccionTipo.CardEffect, CardId = 36, Schema = 1, GameName = "Añil" }, partida).Estado);
        Assert.Equal(AccionEstado.Aplicada, aplicador.Aplicar(new AccionLauncher { Type = AccionTipo.CardEffect, CardId = 59, Schema = 1, GameName = "Añil" }, partida).Estado);
        partida.Guardar();

        var re = PartidaAnil.Cargar(partida.Ruta, partida.CarpetaPbs);
        Assert.Equal("ADAMANT", re.Equipo[0].InternalNature);
        Assert.False(re.Equipo[0].IsShiny);
    }
}
