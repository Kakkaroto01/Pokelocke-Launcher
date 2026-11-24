using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PokeLobby.PokeLocke;
using PKHeX.Core;
using Xunit;

namespace PokeLobby.Tests;

/// <summary>
/// PROMPT 6 — regresión del BLOQUEANTE de checksums: el .sav debe escribirse con el pipeline
/// de PKHeX (Write()) para que el juego lo acepte, y el backup debe ser el original exacto.
/// </summary>
public class ChecksumsTests
{
    /// <summary>Copia el fixture REAL y lo deja con checksums válidos (baseline antes de la acción).</summary>
    private static (string Ruta, byte[] Bytes) CopiaValida()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        // baseline válido: reescribe con Write() para partir de ChecksumsValid == true
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));
        var bytes = File.ReadAllBytes(ruta);
        var comprobado = SaveUtil.GetSaveFile(ruta)!;
        Assert.True(comprobado.ChecksumsValid, "el baseline del test debe tener checksums válidos");
        return (ruta, bytes);
    }

    [Fact] // 1.d — carta 70 + Gachapón + ITEM_GRANT con el servicio REAL (sin seams)
    public async Task Regresion_ChecksumsValidos_TrasCarta70GachaEItem()
    {
        var (ruta, original) = CopiaValida();
        var tamanoOriginal = original.Length;

        var antes = SaveUtil.GetSaveFile(ruta)!;
        Assert.True(antes.ChecksumsValid);
        var evAntes = antes.GetPartySlotAtIndex(0).EV_ATK;
        var dineroAntes = antes.Money;
        var monsAntes = antes.PartyData.Count(p => p.Species > 0)
                        + Enumerable.Range(0, antes.BoxCount).Sum(b => antes.GetBoxData(b).Count(p => p.Species > 0));

        var fb = new FakeFirebase()
            .Agregar("evs", Ayuda.Accion(AccionTipo.CardEffect,
                a => { a.CardId = 70; a.CreatedAt = 1; a.Params = JsonNode.Parse("""{"stat":"atk"}"""); }, "Heartgold"))
            .Agregar("gacha", Ayuda.Accion(AccionTipo.GachaPokemon, a =>
            {
                a.CreatedAt = 2; a.SpeciesId = 25; a.SpeciesName = "Pikachu";
                a.IsShiny = false; a.Ball = "POKEBALL"; a.GameGen = 4;
            }, "Heartgold"))
            .Agregar("item", Ayuda.Accion(AccionTipo.ItemGrant, a =>
            {
                a.CreatedAt = 3; a.AppItemId = 80; a.ItemName = "Master Ball"; a.Quantity = 1;
            }, "Heartgold"));

        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);

        Assert.Equal(3, res.Count(r => r.Estado == AccionEstado.Aplicada));

        // EVIDENCIA: checksums válidos tras releer el archivo de disco
        var despues = SaveUtil.GetSaveFile(ruta)!;
        Assert.True(despues.ChecksumsValid, $"checksums inválidos tras guardar: {despues.ChecksumInfo}");

        // los valores cambiaron de verdad
        Assert.Equal(evAntes + 128, despues.GetPartySlotAtIndex(0).EV_ATK);
        var monsDespues = despues.PartyData.Count(p => p.Species > 0)
                          + Enumerable.Range(0, despues.BoxCount).Sum(b => despues.GetBoxData(b).Count(p => p.Species > 0));
        Assert.True(monsDespues > monsAntes, "el Gachapón no añadió ningún Pokémon");
        Assert.True(despues.Inventory.Pouches.Any(p => p.Items.Any(i => i.Index == 1 && i.Count > 0)),
            "la Master Ball no quedó en la mochila");

        // el tamaño del archivo no cambia
        Assert.Equal(tamanoOriginal, new FileInfo(ruta).Length);

        // el backup es byte a byte el original (antes de esta operación)
        var backup = Directory.GetFiles(Path.GetDirectoryName(ruta)!, Path.GetFileNameWithoutExtension(ruta) + ".bak-*")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .First();
        Assert.Equal(original, File.ReadAllBytes(backup));
    }

    [Fact] // 1.c — gen 5/6/7 en blanco: Write() produce checksums válidos y la acción persiste
    public void GeneracionesEnBlanco_WriteProduceChecksumsValidos()
    {
        foreach (var version in new[] { GameVersion.B, GameVersion.X, GameVersion.SN })
        {
            var sav = BlankSaveFile.Get(version, "TEST", LanguageID.Spanish);
            Ayuda.RellenarEquipo(sav, 6);
            var tamano = sav.Data.Length;

            var pkm = sav.GetPartySlotAtIndex(0);
            EfectosCartas.AnadirEvs(pkm, "atk", 128);
            sav.SetPartySlotAtIndex(pkm, 0);

            var bytes = InyectorEquipo.SerializarSave(sav);
            Assert.Equal(tamano, bytes.Length);
            Assert.True(sav.ChecksumsValid, $"{version}: checksums inválidos ({sav.ChecksumInfo})");
            Assert.Equal(10 + 128, sav.GetPartySlotAtIndex(0).EV_ATK);
        }
    }
    [Fact] // el pipeline de serialización conserva el tamaño y valida checksums en un save real
    public void SerializarSave_ConservaTamanoYChecksums()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        var antes = new FileInfo(ruta).Length;
        var bytes = InyectorEquipo.SerializarSave(sav);
        File.WriteAllBytes(ruta, bytes);
        Assert.Equal(antes, new FileInfo(ruta).Length);
        Assert.True(SaveUtil.GetSaveFile(ruta)!.ChecksumsValid);
    }
}

/// <summary>PROMPT 6 — puntos 2, 3 y 4.</summary>
public class ColaExtraTests
{
    [Fact] // 2 — entradas ilegibles se marcan failed en Firebase con el motivo
    public async Task EntradaIlegible_SeMarcaFailedEnFirebase()
    {
        var h = new HttpMessageHandlerSimulado();
        // GET cola: una acción buena y una entrada que NO es objeto
        h.EncolarGet("""{"buena":{"schema":1,"type":"CARD_EFFECT","status":"pending","createdAt":1,"createdBy":"t","appVersion":"1","gameName":"Heartgold","cardId":67},"rota":"esto-no-es-un-objeto"}""");
        h.EncolarOk();   // PATCH de la entrada rota (failed)

        var cliente = new FirebaseRtdbClient("https://demo-default-rtdb.europe-west1.firebasedatabase.app", "KEY", auth: "tok", handler: h);
        var cola = await cliente.LeerColaAsync("locke", "tag_1");

        Assert.Single(cola);                       // solo la buena entra en la cola
        var patch = h.Patches.Single();            // la rota se marca failed
        var cuerpo = JsonNode.Parse(patch)!;
        Assert.Equal("failed", (string?)cuerpo["status"]);
        Assert.Contains("JSON inválido", (string?)cuerpo["resultMessage"]);
        Assert.Contains("rota", h.RutasPatched.Single());
    }

    [Fact] // 3 — una acción failed no impide guardar las demás; acciones-aplicadas.txt solo las aplicadas
    public async Task AccionFallida_NoImpideLasDemas_Y_IdempotenciaSoloAplicadas()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));
        var datos = Ayuda.DirDatosTemporal();

        var dineroAntes = sav.Money;
        var fb = new FakeFirebase()
            .Agregar("mala", Ayuda.Accion(AccionTipo.GachaPokemon, a =>
            {
                a.CreatedAt = 1; a.SpeciesId = 888; a.SpeciesName = "Zacian"; a.Ball = "POKEBALL";
            }, "Heartgold"))
            .Agregar("buena", Ayuda.Accion(AccionTipo.CardEffect,
                a => { a.CreatedAt = 2; a.CardId = 67; }, "Heartgold"));

        var res = await Ayuda.Servicio(fb, datos).ProcesarColaAsync("l", "p", ruta);

        Assert.Equal(AccionEstado.Fallida, res.First(r => r.ActionId == "mala").Estado);
        Assert.Equal(AccionEstado.Aplicada, res.First(r => r.ActionId == "buena").Estado);

        var dinero = SaveUtil.GetSaveFile(ruta)!.Money;
        Assert.Equal((uint)Math.Min(sav.MaxMoney, dineroAntes + 25_000), dinero);   // la buena sí se guardó (+25.000)

        var lineas = File.ReadAllLines(Path.Combine(datos, "acciones-aplicadas.txt"));
        Assert.Contains("buena", lineas);
        Assert.DoesNotContain("mala", lineas);    // la fallida NO se registra como aplicada
        Assert.True(SaveUtil.GetSaveFile(ruta)!.ChecksumsValid);
    }

    [Fact] // 4 — 87/88 sin objetivo: resultMessage al móvil + aviso en el log tras 3 intentos
    public async Task Carta87_SinObjetivo_AvisaTrasTresIntentos()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));
        var datos = Ayuda.DirDatosTemporal();
        var avisos = new List<string>();

        ColaLauncherService NuevoServicio()
        {
            var fb = new FakeFirebase().Agregar("c87", Ayuda.Accion(AccionTipo.CardEffect, a =>
            {
                a.CardId = 87;
                a.CreatedBy = "Kakkaroto_9062";
                a.Params = JsonNode.Parse("""{"stats":["atk","spe"],"targetSpeciesId":999}""");
            }, "Heartgold"));
            var servicio = Ayuda.Servicio(fb, datos);
            servicio.AlProgresar = msg => avisos.Add(msg);
            return servicio;
        }

        for (var intento = 1; intento <= 3; intento++)
        {
            var res = await NuevoServicio().ProcesarColaAsync("l", "p", ruta);
            Assert.Equal(AccionEstado.Pendiente, res.Single().Estado);
            Assert.Contains("No encuentro el Pokémon objetivo", res.Single().ResultMessage);
        }

        Assert.Contains(avisos, a => a.Contains("#999") && a.Contains("3 intentos"));
    }
}

/// <summary>HttpMessageHandler configurable para los tests de cliente Firebase.</summary>
public sealed class HttpMessageHandlerSimulado : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _respuestas = new();
    public List<string> Patches { get; } = [];
    public List<string> RutasPatched { get; } = [];

    public HttpMessageHandlerSimulado EncolarGet(string json)
    {
        _respuestas.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        return this;
    }

    public HttpMessageHandlerSimulado EncolarOk()
    {
        _respuestas.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        if (peticion.Method.Method == "PATCH" && peticion.Content is not null)
        {
            Patches.Add(await peticion.Content.ReadAsStringAsync(ct));
            RutasPatched.Add(peticion.RequestUri!.ToString());
        }
        return _respuestas.Count > 0 ? _respuestas.Dequeue()(peticion) : new HttpResponseMessage(HttpStatusCode.OK);
    }
}
