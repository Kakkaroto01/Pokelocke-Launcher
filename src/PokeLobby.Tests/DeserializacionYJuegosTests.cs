using System;
using System.Linq;
using System.Text.Json.Nodes;
using PokeLobby.PokeLocke;
using Xunit;

namespace PokeLobby.Tests;

public class DeserializacionTests
{
    /// <summary>JSON tal como lo escribe Android: appItemId NÚMERO, params objeto, isShiny bool.</summary>
    private const string JsonAndroid = """
    {
      "a1": {"schema":1,"type":"CARD_EFFECT","status":"pending","createdAt":1760770000001,"createdBy":"el_2861","appVersion":"1.0.0","gameName":"Heartgold","cardId":67,"cardName":"Baño dinero"},
      "a2": {"schema":1,"type":"CARD_EFFECT","status":"pending","createdAt":1760770000002,"createdBy":"el_2861","appVersion":"1.0.0","gameName":"Heartgold","cardId":74,"cardName":"Medicina","params":{"success":true}},
      "a3": {"schema":1,"type":"GACHA_POKEMON","status":"pending","createdAt":1760770000003,"createdBy":"el_2861","appVersion":"1.0.0","gameName":"Heartgold","speciesId":25,"speciesName":"Pikachu","isShiny":true,"ball":"ULTRABALL","gameGen":4},
      "a4": {"schema":1,"type":"ITEM_GRANT","status":"pending","createdAt":1760770000004,"createdBy":"el_2861","appVersion":"1.0.0","gameName":"Heartgold","appItemId":80,"itemName":"Master Ball","quantity":1},
      "a5": {"schema":1,"type":"MINT_USE","status":"pending","createdAt":1760770000005,"createdBy":"el_2861","appVersion":"1.0.0","gameName":"Heartgold","nature":"TIMID","appItemId":42}
    }
    """;

    [Fact] // 5 acciones del JSON real de Android → 5 leídas (sin perder ITEM_GRANT/MINT_USE)
    public void JsonAndroid_CincoAcciones_CincoLeidas()
    {
        var parseada = ColaDeserializador.Parsear(JsonNode.Parse(JsonAndroid));
        Assert.Empty(parseada.Errores);
        Assert.Equal(5, parseada.Acciones.Count);
        Assert.Equal(80, parseada.Acciones["a4"].AppItemId);
        Assert.Equal("Master Ball", parseada.Acciones["a4"].ItemName);
        Assert.Equal(1, parseada.Acciones["a4"].Quantity);
        Assert.Equal("TIMID", parseada.Acciones["a5"].Nature);
        Assert.Equal(42, parseada.Acciones["a5"].AppItemId);
        Assert.True(parseada.Acciones["a3"].IsShiny);
        Assert.True(parseada.Acciones["a2"].ParamBool("success"));
    }

    [Fact] // appItemId también como string numérico (tolerancia v1.1.a)
    public void AppItemId_StringNumerico_Tolerado()
    {
        const string json = """
        {"x":{"schema":1,"type":"ITEM_GRANT","status":"pending","createdAt":1,"createdBy":"t","appVersion":"1","gameName":"X","appItemId":"80","itemName":"Master Ball","quantity":1}}
        """;
        var parseada = ColaDeserializador.Parsear(JsonNode.Parse(json));
        Assert.Equal(80, parseada.Acciones["x"].AppItemId);
    }

    [Fact] // cola null / ausente / "null" → diccionario vacío sin errores
    public void ColaNula_Vacia_SinErrores()
    {
        Assert.Empty(ColaDeserializador.Parsear(null).Acciones);
        Assert.Empty(ColaDeserializador.Parsear(JsonNode.Parse("null")).Acciones);
        Assert.Empty(ColaDeserializador.Parsear(JsonNode.Parse("{}")).Acciones);
    }

    [Fact] // array JSON con huecos null de RTDB
    public void ColaArray_ConHuecosNull_Leida()
    {
        const string json = """
        [ {"schema":1,"type":"CARD_EFFECT","status":"pending","createdAt":1,"createdBy":"t","appVersion":"1","gameName":"X","cardId":67},
          null,
          {"schema":1,"type":"CARD_EFFECT","status":"pending","createdAt":2,"createdBy":"t","appVersion":"1","gameName":"X","cardId":31} ]
        """;
        var parseada = ColaDeserializador.Parsear(JsonNode.Parse(json));
        Assert.Empty(parseada.Errores);
        Assert.Equal(2, parseada.Acciones.Count);
        Assert.Equal(67, parseada.Acciones["[0]"].CardId);
        Assert.Equal(31, parseada.Acciones["[2]"].CardId);
    }

    [Fact] // entradas ilegibles NO se ocultan: se registran como error
    public void ColaConEntradaInvalida_SeRegistraError()
    {
        const string json = """
        {"buena":{"schema":1,"type":"CARD_EFFECT","status":"pending","createdAt":1,"createdBy":"t","appVersion":"1","gameName":"X","cardId":67},
         "mala":"esto-no-es-un-objeto"}
        """;
        var parseada = ColaDeserializador.Parsear(JsonNode.Parse(json));
        Assert.Single(parseada.Acciones);
        Assert.Single(parseada.Errores);
        Assert.Equal("mala", parseada.Errores[0].Clave);
    }

    [Fact] // carta 88 (rival) con params de stats y targetSpeciesId
    public void Carta88Rival_ParamsLeidos()
    {
        const string json = """
        {"r":{"schema":1,"type":"CARD_EFFECT","status":"pending","createdAt":1,"createdBy":"Kakkaroto_9062","appVersion":"1","gameName":"Heartgold","cardId":88,"params":{"stats":["atk","def","spa","spd"],"targetSpeciesId":25,"targetNickname":"Pikachu"}}}
        """;
        var a = ColaDeserializador.Parsear(JsonNode.Parse(json)).Acciones["r"];
        Assert.Equal(88, a.CardId);
        Assert.Equal(25, a.ParamInt("targetSpeciesId"));
        Assert.Equal("Pikachu", a.ParamStr("targetNickname"));
        Assert.Equal(4, a.ParamStrList("stats").Count);
    }
}

public class JuegosTests
{
    [Fact] // los 28 nombres de la app tienen tabla de versiones y normalizan
    public void Nombres_28_ConVersiones()
    {
        Assert.Equal(28, JuegosApp.Nombres.Length);
        foreach (var nombre in JuegosApp.Nombres)
        {
            var v = JuegosApp.AVersionesAceptables(nombre);
            Assert.True(v.Count > 0, $"«{nombre}» sin versiones aceptables");
        }
    }

    [Theory] // correcciones del v1: Omega Ruby = OR, Alpha Sapphire = AS, Perla Reluciente = SP
    [InlineData("Rubi Omega", GameVersionPoke.OR)]
    [InlineData("Zafiro Alfa", GameVersionPoke.AS)]
    [InlineData("Perla Reluciente", GameVersionPoke.SP)]
    [InlineData("Heartgold", GameVersionPoke.HG)]
    [InlineData("Soul Silver", GameVersionPoke.SS)]
    [InlineData("Rubi", GameVersionPoke.R)]
    [InlineData("Zafiro", GameVersionPoke.S)]
    public void MapeosCorregidos(string nombre, GameVersionPoke esperada)
        => Assert.Contains(esperada, JuegosApp.AVersionesAceptables(nombre));

    [Theory] // PKHeX devuelve versiones COMBINADAS en algunos saves: deben aceptarse
    [InlineData("Diamante", PKHeX.Core.GameVersion.DP)]
    [InlineData("Perla", PKHeX.Core.GameVersion.DP)]
    [InlineData("Rubi", PKHeX.Core.GameVersion.RS)]
    [InlineData("Zafiro", PKHeX.Core.GameVersion.RS)]
    [InlineData("Esmeralda", PKHeX.Core.GameVersion.RS)]
    [InlineData("Heartgold", PKHeX.Core.GameVersion.HGSS)]
    [InlineData("Soul Silver", PKHeX.Core.GameVersion.HGSS)]
    [InlineData("Rojo Fuego", PKHeX.Core.GameVersion.FRLG)]
    [InlineData("Verde Hoja", PKHeX.Core.GameVersion.FRLG)]
    [InlineData("Negro", PKHeX.Core.GameVersion.BW)]
    [InlineData("X", PKHeX.Core.GameVersion.XY)]
    public void VersionesCombinadas_Aceptadas(string nombre, PKHeX.Core.GameVersion versionCombinada)
        => Assert.True(JuegosApp.AceptaSave(nombre, versionCombinada),
            $"«{nombre}» debería aceptar el save con versión combinada {versionCombinada}");

    [Theory] // juego distinto → rechazado
    [InlineData("Heartgold", PKHeX.Core.GameVersion.E)]
    [InlineData("Diamante", PKHeX.Core.GameVersion.HG)]
    [InlineData("X", PKHeX.Core.GameVersion.SN)]
    public void JuegoDistinto_Rechazado(string nombre, PKHeX.Core.GameVersion version)
        => Assert.False(JuegosApp.AceptaSave(nombre, version));

    [Fact] // nombre desconocido → se deja pasar (no bloquea versiones nuevas)
    public void NombreDesconocido_DejaPasar()
        => Assert.True(JuegosApp.AceptaSave("Leyendas Arceus", PKHeX.Core.GameVersion.PLA));
}
