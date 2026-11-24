using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using PokeLobby.PokeLocke;
using PKHeX.Core;

namespace PokeLobby.Tests;

/// <summary>
/// Lector de fixtures para los tests: los saves EN BLANCO de <see cref="BlankSaveFile"/> no
/// son exportables (State.Exportable init-only y Data vacío en gen 3/4), así que el harness
/// serializa sus bytes con una cabecera propia y los reconoce al re-leer desde disco.
/// Formato: "LKBL" + 1 byte de versión + payload (Data del save en blanco).
/// </summary>
public sealed class LectorFixtures : ISaveReader
{
    private static readonly GameVersion[] Versiones =
    [
        GameVersion.B, GameVersion.W, GameVersion.B2, GameVersion.W2,
        GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS,
        GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM,
        GameVersion.SW, GameVersion.SH, GameVersion.BD, GameVersion.SP,
        GameVersion.SL, GameVersion.VL,
    ];

    private static HashSet<long>? _longitudes;

    private static HashSet<long> Longitudes()
    {
        if (_longitudes is not null) return _longitudes;
        var set = new HashSet<long>();
        foreach (var v in Versiones)
        {
            try
            {
                var len = BlankSaveFile.Get(v, "TEST", LanguageID.Spanish).Data.Length;
                if (len > 0) set.Add(5 + len);
            }
            catch { }
        }
        return _longitudes = set;
    }

    public bool IsRecognized(long dataLength) => Longitudes().Contains(dataLength);

    public bool TryRead(Memory<byte> data, out SaveFile? result, string path)
    {
        result = null;
        var span = data.Span;
        GameVersion version;
        ReadOnlySpan<byte> payload;
        if (data.Length >= 6 && span[0] == (byte)'L' && span[1] == (byte)'K' && span[2] == (byte)'B' && span[3] == (byte)'L')
        {
            var indice = span[4];
            if (indice >= Versiones.Length) return false;
            version = Versiones[indice];
            payload = span[5..];
        }
        else
        {
            return false;   // sin cabecera LKBL no reclamamos el archivo (evita confundir un SAV4 real)
        }
        var sav = BlankSaveFile.Get(version, "TEST", LanguageID.Spanish);
        var destino = sav.Data;
        if (payload.Length != destino.Length) return false;
        payload.CopyTo(destino);
        result = sav;
        return true;
    }
}

/// <summary>Registra el lector de fixtures antes de que corran los tests.</summary>
public static class InicializadorTests
{
    [ModuleInitializer]
    public static void Inicializar()
        => SaveUtil.CustomSaveReaders.Add(new LectorFixtures());
}

/// <summary>Utilidades comunes de los tests: fixtures en disco + JSON de acciones.</summary>
public static class Ayuda
{
    private static string Base => Path.Combine(AppContext.BaseDirectory, "tmp-tests");

    public static string NuevoArchivoTemporal(string sufijo = ".sav")
    {
        Directory.CreateDirectory(Base);
        return Path.Combine(Base, $"{Guid.NewGuid():N}{sufijo}");
    }

    public static string DirDatosTemporal()
    {
        var d = Path.Combine(Base, "datos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Copia el fixture REAL de HeartGold (formato de disco exacto) a un archivo temporal.</summary>
    public static string CopiaSaveReal(out SaveFile sav)
    {
        var origen = Path.Combine(AppContext.BaseDirectory, "testdata", "hgss.sav");
        var ruta = NuevoArchivoTemporal();
        File.Copy(origen, ruta, overwrite: true);
        sav = SaveUtil.GetSaveFile(ruta) ?? throw new InvalidOperationException("el fixture HGSS no se re-leyó");
        return ruta;
    }

    /// <summary>
    /// Save en blanco EXPORTABLE a disco (gen 5/6/7) con el equipo rellenado: escribe el fixture
    /// con cabecera LKBL para que <see cref="LectorFixtures"/> lo re-lea. Devuelve ruta.
    /// </summary>
    public static string SaveEnBlancoExportable(GameVersion version, params ushort[] especies)
    {
        var sav = BlankSaveFile.Get(version, "TEST", LanguageID.Spanish);
        RellenarEquipo(sav, especies);
        var ruta = NuevoArchivoTemporal();
        EscribirFixture(sav, ruta);
        return ruta;
    }

    /// <summary>Escribe el save a disco en el formato de fixture (LKBL + índice + Data).</summary>
    public static void EscribirFixture(SaveFile sav, string ruta)
    {
        var versiones = new[]
        {
            GameVersion.B, GameVersion.W, GameVersion.B2, GameVersion.W2,
            GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS,
            GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM,
            GameVersion.SW, GameVersion.SH, GameVersion.BD, GameVersion.SP,
            GameVersion.SL, GameVersion.VL,
        };
        var indice = Array.IndexOf(versiones, sav.Version);
        if (indice < 0) throw new InvalidOperationException($"versión {sav.Version} no soportada por el fixture");
        var payload = sav.Write(BinaryExportSetting.None).ToArray();
        using var fs = File.Create(ruta);
        fs.Write("LKBL"u8);
        fs.WriteByte((byte)indice);
        fs.Write(payload);
    }

    /// <summary>Rellena el equipo con N Pokémon variados (para poder apuntar acciones a ellos).</summary>
    public static void RellenarEquipo(SaveFile sav, params ushort[] especies)
    {
        var i = 0;
        foreach (var sp in especies)
        {
            if (i >= 6) break;
            var pkm = sav.BlankPKM;
            pkm.Species = sp;
            pkm.Version = sav.Version;
            pkm.CurrentLevel = 50;
            pkm.Stat_Level = 50;
            var pi = sav.Personal.GetFormEntry(sp, 0);
            pkm.EXP = Experience.GetEXP(50, (byte)pi.EXPGrowth);
            pkm.OriginalTrainerName = sav.OT;
            pkm.TID16 = sav.TID16;
            pkm.SID16 = sav.SID16;
            pkm.IV_HP = 20; pkm.IV_ATK = 20; pkm.IV_DEF = 20;
            pkm.IV_SPA = 20; pkm.IV_SPD = 20; pkm.IV_SPE = 20;
            pkm.EV_HP = 10; pkm.EV_ATK = 10; pkm.EV_DEF = 10;
            pkm.EV_SPA = 10; pkm.EV_SPD = 10; pkm.EV_SPE = 10;
            pkm.SetMoves([(ushort)1]);
            pkm.ResetPartyStats();
            pkm.RefreshChecksum();
            sav.SetPartySlotAtIndex(pkm, i++);
        }
    }

    public static AccionLauncher Accion(string tipo, Action<AccionLauncher>? config = null, string gameName = "")
    {
        var a = new AccionLauncher
        {
            Schema = 1,
            Type = tipo,
            Status = AccionEstado.Pendiente,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CreatedBy = "test_1234",
            AppVersion = "1.0.0",
            GameName = gameName,
        };
        config?.Invoke(a);
        return a;
    }

    /// <summary>Servicio con respuestas automáticas de diálogo (elige el primer objetivo disponible).</summary>
    /// <param name="fixtures">true = persiste los saves en blanco con cabecera LKBL (gen 5/6/7).</param>
    public static ColaLauncherService Servicio(FakeFirebase fb, string datosDir, bool emuladorAbierto = false, bool fixtures = false, Func<ObjetivoPkm, bool>? filtrarObjetivo = null)
    {
        var servicio = new ColaLauncherService(fb, datosDir, () => emuladorAbierto)
        {
            PedirObjetivo = (_, objetivos) => (filtrarObjetivo is null ? objetivos.FirstOrDefault() : objetivos.FirstOrDefault(filtrarObjetivo))?.Ubicacion,
            PedirStat = _ => "atk",
            PedirDosStats = _ => ["atk", "spe"],
            PedirMovimiento = (_, maxMov) => (ushort)Math.Min(85, (int)maxMov),
            PedirHabilidad = (_, opciones) => opciones.Count > 0 ? opciones[0].Id : null,
            PedirMovimientoReemplazo = (_, _) => 0,
            PedirMovimientoDeLista = (_, opciones) => opciones.Count > 0 ? opciones[0] : null,
            PedirEdicionStats = (_, _, _) => (new[] { 31, 31, 31, 31, 31, 31 }, new[] { 4, 252, 0, 0, 0, 252 }),
            PedirMote = _ => "MoteTest",
            PedirRepartoEvs = _ => new Dictionary<string, int> { ["atk"] = 252, ["spe"] = 252, ["hp"] = 6 },
            PedirNivel = _ => 50,
        };

        if (fixtures)
        {
            // SOLO para saves en blanco NO exportables (gen 5/6/7): se serializa con Write()
            // (checksums válidos) y se envuelve en la cabecera LKBL del lector de fixtures.
            servicio.Serializador = (sav, ruta) =>
            {
                EscribirFixture(sav, ruta);
                return File.ReadAllBytes(ruta);
            };
        }
        return servicio;
    }
}

/// <summary>IColaFirebase en memoria: cola desde JSON/diccionario, registra resultados y claims.</summary>
public sealed class FakeFirebase : IColaFirebase
{
    public Dictionary<string, AccionLauncher> Cola { get; } = new();
    public List<(string Id, string Status, string Msg)> Resultados { get; } = new();
    public List<string> Reclamadas { get; } = [];
    public List<string> DevueltasAPendiente { get; } = [];
    public bool RechazarClaim { get; set; }
    public Action<string>? Registro { get; set; }

    public FakeFirebase Cargar(string json)
    {
        var parseada = ColaDeserializador.Parsear(System.Text.Json.Nodes.JsonNode.Parse(json));
        foreach (var (k, v) in parseada.Acciones) Cola[k] = v;
        return this;
    }

    public FakeFirebase Agregar(string id, AccionLauncher accion)
    {
        Cola[id] = accion;
        return this;
    }

    public Task<Dictionary<string, AccionLauncher>> LeerColaAsync(string lockeId, string playerTag, CancellationToken ct = default)
        => Task.FromResult(new Dictionary<string, AccionLauncher>(Cola));

    public Task<(AccionLauncher? Accion, string? ETag)> LeerAccionAsync(string lockeId, string playerTag, string actionId, CancellationToken ct = default)
        => Task.FromResult((Cola.TryGetValue(actionId, out var a) ? a : null, $"\"etag-{actionId}\""));

    public Task<bool> ReclamarAccionAsync(string lockeId, string playerTag, string actionId, AccionLauncher accion, CancellationToken ct = default)
    {
        if (RechazarClaim) return Task.FromResult(false);
        accion.Status = AccionEstado.Aplicando;
        accion.ClaimedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        accion.ClaimedBy = FirebaseRtdbClient.AppVersion;
        Reclamadas.Add(actionId);
        return Task.FromResult(true);
    }

    public Task MarcarResultadoAsync(string lockeId, string playerTag, string actionId, string status, string resultMessage, string? errorMessage = null, CancellationToken ct = default)
    {
        Resultados.Add((actionId, status, resultMessage));
        if (Cola.TryGetValue(actionId, out var a))
        {
            a.Status = status;
            a.ResultMessage = resultMessage;
            a.ErrorMessage = errorMessage;
        }
        return Task.CompletedTask;
    }

    public Task DevolverAPendienteAsync(string lockeId, string playerTag, string actionId, string motivo, CancellationToken ct = default)
    {
        DevueltasAPendiente.Add(actionId);
        if (Cola.TryGetValue(actionId, out var a))
        {
            a.Status = AccionEstado.Pendiente;
            a.ClaimedAt = null;
            a.ClaimedBy = null;
        }
        return Task.CompletedTask;
    }
}
