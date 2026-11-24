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

public class ComportamientoTests
{
    [Fact] // orden por createdAt (más antiguo primero)
    public async Task ProcesaEnOrdenDeCreatedAt()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        Ayuda.RellenarEquipo(sav, 6);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));

        var fb = new FakeFirebase()
            .Agregar("tarde", Ayuda.Accion(AccionTipo.CardEffect, a => { a.CardId = 67; a.CreatedAt = 3000; }, "Heartgold"))
            .Agregar("pronto", Ayuda.Accion(AccionTipo.CardEffect, a => { a.CardId = 41; a.CreatedAt = 1000; }, "Heartgold"));
        var servicio = Ayuda.Servicio(fb, Ayuda.DirDatosTemporal());
        var orden = new List<string>();
        servicio.AlProgresar = msg => { if (msg.Contains("Procesando")) orden.Add(msg); };

        await servicio.ProcesarColaAsync("l", "p", ruta);

        Assert.Equal(2, orden.Count);
        Assert.Contains("pronto", orden[0]);
        Assert.Contains("tarde", orden[1]);
    }

    [Fact] // cancelar un diálogo → pending (NO failed) y el resto del lote sigue
    public async Task CancelacionQuedaPendiente_NoFallida()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        Ayuda.RellenarEquipo(sav, 6);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));
        var antes = File.ReadAllBytes(ruta);
        var dineroAntes = sav.Money;

        var fb = new FakeFirebase()
            .Agregar("cancela", Ayuda.Accion(AccionTipo.CardEffect, a => { a.CardId = 41; a.CreatedAt = 1; }, "Heartgold"))
            .Agregar("sigue", Ayuda.Accion(AccionTipo.CardEffect, a => { a.CardId = 67; a.CreatedAt = 2; }, "Heartgold"));
        var servicio = Ayuda.Servicio(fb, Ayuda.DirDatosTemporal());
        servicio.PedirObjetivo = (_, _) => null;   // el jugador cancela
        servicio.PedirStat = _ => null;

        var res = await servicio.ProcesarColaAsync("l", "p", ruta);

        var cancelada = res.First(r => r.ActionId == "cancela");
        Assert.Equal(AccionEstado.Pendiente, cancelada.Estado);
        Assert.Equal(AccionEstado.Pendiente, fb.Cola["cancela"].Status);
        Assert.Contains("cancela", fb.DevueltasAPendiente);
        // la otra acción sí se aplicó
        Assert.Equal(AccionEstado.Aplicada, res.First(r => r.ActionId == "sigue").Estado);
        Assert.Equal((uint)Math.Min(sav.MaxMoney, dineroAntes + 25_000), SaveUtil.GetSaveFile(ruta)!.Money);
    }

    [Fact] // idempotencia: la segunda pasada no re-aplica y marca "ya aplicada"
    public async Task SegundaPasada_NoDuplicaEfecto()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        Ayuda.RellenarEquipo(sav, 6);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));
        var datos = Ayuda.DirDatosTemporal();

        var fb = new FakeFirebase().Agregar("una", Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = 70, "Heartgold"));
        var servicio = Ayuda.Servicio(fb, datos);
        await servicio.ProcesarColaAsync("l", "p", ruta);

        var evTrasPrimera = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0).EV_ATK;

        // segunda pasada (misma cola pendiente en memoria del fake)
        fb.Cola["una"].Status = AccionEstado.Pendiente;
        await Ayuda.Servicio(fb, datos).ProcesarColaAsync("l", "p", ruta);

        var evTrasSegunda = SaveUtil.GetSaveFile(ruta)!.GetPartySlotAtIndex(0).EV_ATK;
        Assert.Equal(evTrasPrimera, evTrasSegunda);   // NO se sumó otra vez
        Assert.True(File.Exists(Path.Combine(datos, "acciones-aplicadas.txt")));
    }

    [Fact] // emulador abierto → no toca el .sav ni marca nada
    public async Task EmuladorAbierto_NoEscribe()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        Ayuda.RellenarEquipo(sav, 6);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));
        var antes = File.ReadAllBytes(ruta);

        var fb = new FakeFirebase().Agregar("c", Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = 67, "Heartgold"));
        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal(), emuladorAbierto: true)
            .ProcesarColaAsync("l", "p", ruta);

        Assert.Empty(res);
        Assert.Empty(fb.Reclamadas);
        Assert.Equal(antes, File.ReadAllBytes(ruta));
    }

    [Fact] // si otro launcher reclamó la acción (412) → se salta
    public async Task ClaimRechazado_SeSalta()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        Ayuda.RellenarEquipo(sav, 6);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));

        var fb = new FakeFirebase { RechazarClaim = true }
            .Agregar("c", Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = 67, "Heartgold"));
        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);

        Assert.Empty(res);
        Assert.Empty(fb.Resultados);
    }

    [Fact] // juego del locke ≠ juego del .sav → failed con mensaje claro
    public async Task JuegoDistinto_Failed()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        Ayuda.RellenarEquipo(sav, 6);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));

        var fb = new FakeFirebase().Agregar("c", Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = 67, "Esmeralda"));
        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);

        Assert.Equal(AccionEstado.Fallida, res.Single().Estado);
        Assert.Contains("Esmeralda", res.Single().ResultMessage);
    }

    [Fact] // acción applying caducada (>10 min) se reprocesa como pendiente
    public async Task ClaimCaducado_SeReprocesa()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        Ayuda.RellenarEquipo(sav, 6);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));

        var accion = Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = 67, "Heartgold");
        accion.Status = AccionEstado.Aplicando;
        accion.ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds();
        accion.ClaimedBy = "otro-launcher";
        var fb = new FakeFirebase().Agregar("vieja", accion);

        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);
        Assert.Equal(AccionEstado.Aplicada, res.Single().Estado);
    }

    [Fact] // claim reciente de OTRO launcher → no se toca
    public async Task ClaimRecienteDeOtro_NoSeToca()
    {
        var ruta = Ayuda.CopiaSaveReal(out var sav);
        Ayuda.RellenarEquipo(sav, 6);
        File.WriteAllBytes(ruta, InyectorEquipo.SerializarSave(sav));

        var accion = Ayuda.Accion(AccionTipo.CardEffect, a => a.CardId = 67, "Heartgold");
        accion.Status = AccionEstado.Aplicando;
        accion.ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        accion.ClaimedBy = "otro-launcher";
        var fb = new FakeFirebase().Agregar("activa", accion);

        var res = await Ayuda.Servicio(fb, Ayuda.DirDatosTemporal()).ProcesarColaAsync("l", "p", ruta);
        Assert.Empty(res);   // ni aplicada ni fallida: la tiene otro
    }

    [Fact] // reparto inválido (total > 510) → failed sin tocar el .sav
    public async Task RepartoInvalido_Failed()
    {
        Assert.False(ColaLauncherService.RepartoValido(new Dictionary<string, int> { ["atk"] = 252, ["spe"] = 252, ["hp"] = 6, ["def"] = 1 }, out var motivo));
        Assert.Contains("510", motivo);
        Assert.False(ColaLauncherService.RepartoValido(new Dictionary<string, int> { ["atk"] = 253 }, out _));
        Assert.True(ColaLauncherService.RepartoValido(new Dictionary<string, int> { ["atk"] = 252, ["spe"] = 252, ["hp"] = 6 }, out _));
    }
}

/// <summary>
/// Tests del cliente Firebase real con HttpMessageHandler simulado: claim con ETag (412/200),
/// PUT del objeto COMPLETO y PATCH que no reutiliza contenido tras un 401.
/// </summary>
public class FirebaseClienteTests
{
    private sealed class HandlerSimulado : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _respuestas = new();
        public List<(HttpRequestMessage Peticion, string? Cuerpo)> Capturadas { get; } = [];
        public List<string> BodyPuts { get; } = [];
        public List<string> BodyPatches { get; } = [];

        public HandlerSimulado Encolar(Func<HttpRequestMessage, HttpResponseMessage> r) { _respuestas.Enqueue(r); return this; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            string? cuerpo = null;
            if (peticion.Content is not null)
            {
                cuerpo = await peticion.Content.ReadAsStringAsync(ct);
                if (peticion.Method == HttpMethod.Put) BodyPuts.Add(cuerpo);
                if (peticion.Method.Method == "PATCH") BodyPatches.Add(cuerpo);
            }
            Capturadas.Add((peticion, cuerpo));
            return _respuestas.Count > 0 ? _respuestas.Dequeue()(peticion) : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static FirebaseRtdbClient Cliente(HandlerSimulado h)
        => new("https://demo-default-rtdb.europe-west1.firebasedatabase.app", "API_KEY_TEST", auth: "token-fijo", handler: h);

    private static HttpResponseMessage Json(string texto, HttpStatusCode estado = HttpStatusCode.OK, string? etag = null)
    {
        var r = new HttpResponseMessage(estado) { Content = new StringContent(texto, Encoding.UTF8, "application/json") };
        if (etag is not null) r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return r;
    }

    [Fact] // 412 → false (otro launcher la reclamó) y el PUT llevó if-match
    public async Task Reclamar_412_False()
    {
        var h = new HandlerSimulado()
            .Encolar(_ => Json("""{"schema":1,"type":"CARD_EFFECT","status":"pending","cardId":67}""", etag: "\"e1\""))
            .Encolar(_ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed));
        var cliente = Cliente(h);
        var accion = new AccionLauncher { Type = AccionTipo.CardEffect, CardId = 67 };
        var ok = await cliente.ReclamarAccionAsync("locke", "tag_1", "a1", accion);
        Assert.False(ok);
        Assert.Contains("if-match", h.Capturadas[1].Peticion.Headers.Select(x => x.Key.ToLowerInvariant()));
    }

    [Fact] // claim OK: PUT del objeto COMPLETO con status/claimedAt/claimedBy y sin perder cardId/params
    public async Task Reclamar_Ok_ConservaTodosLosCampos()
    {
        var h = new HandlerSimulado()
            .Encolar(_ => Json("""{"schema":1,"type":"CARD_EFFECT","status":"pending","cardId":88,"cardName":"Sabotaje","createdAt":123,"params":{"stats":["atk","def"],"targetSpeciesId":25}}""", etag: "\"e2\""))
            .Encolar(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var cliente = Cliente(h);
        var accion = new AccionLauncher { Type = AccionTipo.CardEffect, CardId = 88, CardName = "Sabotaje" };
        var ok = await cliente.ReclamarAccionAsync("locke", "tag_1", "a2", accion);

        Assert.True(ok);
        Assert.Single(h.BodyPuts);
        var cuerpo = JsonNode.Parse(h.BodyPuts[0])!;
        Assert.Equal("applying", (string?)cuerpo["status"]);
        Assert.NotNull((long?)cuerpo["claimedAt"]);
        Assert.Equal(FirebaseRtdbClient.AppVersion, (string?)cuerpo["claimedBy"]);
        Assert.Equal(88, (int?)cuerpo["cardId"]);          // conserva campos originales
        Assert.Equal(123, (long?)cuerpo["createdAt"]);
        Assert.NotNull(cuerpo["params"]?["stats"]);        // conserva params
        Assert.Equal(25, (int?)cuerpo["params"]?["targetSpeciesId"]);
    }

    [Fact] // PATCH tras 401: el reintento NO reutiliza contenido consumido y escribe el resultado
    public async Task MarcarResultado_ReintentoTras401_UsaContenidoNuevo()
    {
        var h = new HandlerSimulado()
            .Encolar(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))   // fuerza renovación/retry
            .Encolar(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var cliente = Cliente(h);
        await cliente.MarcarResultadoAsync("locke", "tag_1", "a3", AccionEstado.Aplicada, "Dinero: 999.999");

        Assert.Equal(2, h.BodyPatches.Count);
        // el cuerpo se REGENERA (no llega vacío ni reciclado del intento fallido)
        Assert.False(string.IsNullOrWhiteSpace(h.BodyPatches[1]));
        foreach (var cuerpoTexto in h.BodyPatches)
        {
            var cuerpo = JsonNode.Parse(cuerpoTexto)!;
            Assert.Equal("applied", (string?)cuerpo["status"]);
            Assert.Equal("Dinero: 999.999", (string?)cuerpo["resultMessage"]);
            Assert.Equal(FirebaseRtdbClient.AppVersion, (string?)cuerpo["appliedBy"]);
            Assert.NotNull((long?)cuerpo["appliedAt"]);
        }
    }
}