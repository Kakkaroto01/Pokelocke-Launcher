using System;
using PokeLobby.PokeLocke;
using PKHeX.Core;
using Xunit;

namespace PokeLobby.Tests;

/// <summary>
/// Contrato acordado con la app Android (Gemini, 2026-10-21):
/// lista cerrada de Poké Balls y aviso cuando llega una desconocida.
/// </summary>
public class ContratoColaTests
{
    [Theory]
    [InlineData("POKEBALL", Ball.Poke)]
    [InlineData("SUPERBALL", Ball.Great)]
    [InlineData("LUJOBALL", Ball.Luxury)]
    [InlineData("ULTRABALL", Ball.Ultra)]
    [InlineData("MASTERBALL", Ball.Master)]
    [InlineData("pokeball", Ball.Poke)]        // tolerante a minúsculas
    [InlineData(null, Ball.Poke)]              // ausente ⇒ Poké Ball
    public void BolaDe_MapeaLaListaCerrada(string? nombre, Ball esperada)
    {
        var bola = EfectosCartas.BolaDe(nombre, out var desconocida);
        Assert.Equal(esperada, bola);
        Assert.False(desconocida);
    }

    [Theory]
    [InlineData("REPEATBALL")]
    [InlineData("TIMERBALL")]
    [InlineData("")]
    public void BolaDe_MarcaDesconocidaPeroNoRompe(string nombre)
    {
        var bola = EfectosCartas.BolaDe(nombre, out var desconocida);
        Assert.Equal(Ball.Poke, bola);
        if (nombre.Length == 0)
            Assert.False(desconocida);   // vacío se trata como POKEBALL
        else
            Assert.True(desconocida);
    }

    [Theory] // estadísticas del contrato: solo estas 6
    [InlineData("hp", true)]
    [InlineData("atk", true)]
    [InlineData("spa", true)]
    [InlineData("spe", true)]
    [InlineData("def", true)]
    [InlineData("spd", true)]
    [InlineData("speed", false)]
    [InlineData("", false)]
    public void EsStatValida_SoloLasSeis(string stat, bool esperado)
        => Assert.Equal(esperado, EfectosCartas.EsStatValida(stat));
}