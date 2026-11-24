using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PKHeX.Core;

namespace PokeLobby.PokeLocke;

/// <summary>Una partida compartida de PokeLocke (/lockes/{lockeId} en Firebase RTDB).</summary>
public sealed class Locke
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Juego { get; set; } = "";
    public int Jugadores { get; set; }
    public string Modo { get; set; } = "";
    public bool IsPkhexEnabled { get; set; }
    public bool IsShinylocke { get; set; }
    public bool IsMegasEnabled { get; set; }
    public bool IsSoloRun { get; set; }
    public long LastActive { get; set; }
    public Dictionary<string, PlayerLocke> Players { get; set; } = new();
}

/// <summary>Jugador de un locke con su economía, equipo y cola de acciones del launcher.</summary>
public sealed class PlayerLocke
{
    public string PlayerTag { get; set; } = "";
    public int Monedas { get; set; }
    public int PrioridadOro { get; set; }
    public int TicketsPremium { get; set; }
    public int TicketsMentas { get; set; }
    public int TicketsEvolucion { get; set; }
    public int TicketsObjetos { get; set; }
    public List<ItemInventario> Inventario { get; set; } = [];
    public MyTeam? MyTeam { get; set; }

    /// <summary>
    /// Cola de acciones para el launcher (mapa actionId → acción). Deserializada de forma
    /// tolerante con <see cref="ColaDeserializador"/> (nunca lanza por la cola completa).
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, AccionLauncher> LauncherQueue { get; set; } = new();
}

/// <summary>Item del inventario de la app (las «cartas» con efectos sobre el .sav).</summary>
public sealed class ItemInventario
{
    public int Id { get; set; }
    public string Type { get; set; } = "CARTA";
    public int Quantity { get; set; }
}

public sealed class MyTeam
{
    public List<MonEquipo> Slots { get; set; } = [];
}

/// <summary>Pokémon del equipo de la app (contrato BuildPokemon de PokeLocke Android).</summary>
public sealed class MonEquipo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string Nature { get; set; } = "";
    public string Ability { get; set; } = "";
    public List<string> Moves { get; set; } = [];
    public Dictionary<string, int> Ivs { get; set; } = new();
    public Dictionary<string, int> Evs { get; set; } = new();
    public Dictionary<string, int>? Stages { get; set; }
    public Dictionary<string, int>? BaseStats { get; set; }
    public List<string>? Types { get; set; }
    public string? SpriteUrl { get; set; }
    public bool IsShiny { get; set; }
}

// ─────────────────────── Cola del launcher (contrato v1.1) ───────────────────────

/// <summary>Tipos de acción que el launcher puede procesar.</summary>
public static class AccionTipo
{
    public const string CardEffect = "CARD_EFFECT";
    public const string GachaPokemon = "GACHA_POKEMON";
    public const string ItemGrant = "ITEM_GRANT";
    public const string MintUse = "MINT_USE";
}

/// <summary>Estados del ciclo de vida de una acción en la cola.</summary>
public static class AccionEstado
{
    public const string Pendiente = "pending";
    public const string Aplicando = "applying";
    public const string Aplicada = "applied";
    public const string Fallida = "failed";
    public const string Omitida = "skipped";
}

/// <summary>
/// Una acción en la cola <c>launcherQueue</c> que el launcher debe procesar.
/// Contrato v1.1: la app solo crea acciones <c>pending</c>; el launcher las reclama
/// (<c>applying</c> + claimedAt/claimedBy) y después las pasa a
/// <c>applied</c>/<c>failed</c>/<c>skipped</c>.
/// </summary>
public sealed class AccionLauncher
{
    public int Schema { get; set; } = 1;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = AccionEstado.Pendiente;

    /// <summary>Timestamp de creación (ms epoch, lo pone la app).</summary>
    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "";

    /// <summary>Nombre del juego tal como lo escribe la app (para validar contra la ROM).</summary>
    [JsonPropertyName("gameName")]
    public string GameName { get; set; } = "";

    // ── Campos del claim (los rellena el launcher al reclamar) ──
    [JsonPropertyName("claimedAt")]
    public long? ClaimedAt { get; set; }

    [JsonPropertyName("claimedBy")]
    public string? ClaimedBy { get; set; }

    // ── Campos que rellena el launcher al procesar ──
    [JsonPropertyName("appliedAt")]
    public long? AppliedAt { get; set; }

    [JsonPropertyName("appliedBy")]
    public string? AppliedBy { get; set; }

    /// <summary>Texto corto en español para mostrar en el móvil.</summary>
    [JsonPropertyName("resultMessage")]
    public string? ResultMessage { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    // ── CARD_EFFECT ──
    [JsonPropertyName("cardId")]
    public int? CardId { get; set; }

    [JsonPropertyName("cardName")]
    public string? CardName { get; set; }

    /// <summary>Parámetros flexibles de la carta (success, nature, stat, stats…).</summary>
    [JsonPropertyName("params")]
    public JsonNode? Params { get; set; }

    // ── GACHA_POKEMON ──
    [JsonPropertyName("speciesId")]
    public int? SpeciesId { get; set; }

    [JsonPropertyName("speciesName")]
    public string? SpeciesName { get; set; }

    [JsonPropertyName("isShiny")]
    public bool? IsShiny { get; set; }

    /// <summary>POKEBALL | SUPERBALL | LUJOBALL | ULTRABALL | MASTERBALL</summary>
    [JsonPropertyName("ball")]
    public string? Ball { get; set; }

    [JsonPropertyName("gameGen")]
    public int? GameGen { get; set; }

    // ── ITEM_GRANT / MINT_USE ──
    /// <summary>
    /// ID del objeto en la app. Android lo envía SIEMPRE como número JSON (v1.1.a);
    /// el converter tolera también strings numéricos.
    /// </summary>
    [JsonPropertyName("appItemId")]
    [JsonConverter(typeof(ConverterEnteroTolerante))]
    public int? AppItemId { get; set; }

    /// <summary>Nombre del objeto en español (catálogo de la app).</summary>
    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    // ── MINT_USE ──
    /// <summary>Naturaleza en inglés mayúsculas (ADAMANT, TIMID, SERIOUS…).</summary>
    [JsonPropertyName("nature")]
    public string? Nature { get; set; }

    // ── Helpers ──

    public string? ParamStr(string clave) => (string?)Params?[clave];

    public bool? ParamBool(string clave) => Params?[clave] is { } n ? (bool?)n.GetValue<bool>() : null;

    public int? ParamInt(string clave) => Params?[clave] is JsonValue v
        ? v.TryGetValue<int>(out var i) ? i
            : v.TryGetValue<string>(out var s) && int.TryParse(s, out var p) ? p
            : null
        : null;

    public List<string> ParamStrList(string clave)
    {
        if (Params?[clave] is not JsonArray arr) return [];
        return arr.Where(n => n is not null).Select(n => n!.GetValue<string>()).ToList();
    }

    /// <summary>Indica si la acción ya fue procesada (aplicada, fallida u omitida).</summary>
    [JsonIgnore]
    public bool EstaProcesada => Status is AccionEstado.Aplicada or AccionEstado.Fallida or AccionEstado.Omitida;

    /// <summary>Copia profunda ligera (para el claim conservando todos los campos).</summary>
    public AccionLauncher Clonar()
        => new()
        {
            Schema = Schema,
            Type = Type,
            Status = Status,
            CreatedAt = CreatedAt,
            CreatedBy = CreatedBy,
            AppVersion = AppVersion,
            GameName = GameName,
            ClaimedAt = ClaimedAt,
            ClaimedBy = ClaimedBy,
            AppliedAt = AppliedAt,
            AppliedBy = AppliedBy,
            ResultMessage = ResultMessage,
            ErrorMessage = ErrorMessage,
            CardId = CardId,
            CardName = CardName,
            SpeciesId = SpeciesId,
            SpeciesName = SpeciesName,
            IsShiny = IsShiny,
            Ball = Ball,
            GameGen = GameGen,
            AppItemId = AppItemId,
            ItemName = ItemName,
            Quantity = Quantity,
            Nature = Nature,
            Params = Params?.DeepClone(),
        };
}

/// <summary>
/// Converter tolerante para enteros que pueden llegar como número JSON o como string
/// numérico («80» o 80). Contrato v1.1.a: Android envía appItemId como número.
/// </summary>
public sealed class ConverterEnteroTolerante : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type tipo, JsonSerializerOptions opciones)
        => reader.TokenType switch
        {
            JsonTokenType.Number => reader.TryGetInt32(out var i) ? i : null,
            JsonTokenType.String => int.TryParse(reader.GetString(), out var s) ? s : null,
            JsonTokenType.Null => null,
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, int? valor, JsonSerializerOptions opciones)
    {
        if (valor is null) writer.WriteNullValue();
        else writer.WriteNumberValue(valor.Value);
    }
}

/// <summary>Resultado de parsear una cola: acciones legibles + entradas con error (no se ocultan).</summary>
public sealed record ColaParseada(
    Dictionary<string, AccionLauncher> Acciones,
    List<(string Clave, string Motivo)> Errores);

/// <summary>
/// Deserializador tolerante para <c>launcherQueue</c>: acepta null, array JSON (con huecos
/// null), objeto con claves push de Firebase, o ausencia. Las entradas que no se puedan
/// parsear NO se descartan en silencio: se devuelven en <see cref="ColaParseada.Errores"/>
/// para que el servicio las marque <c>failed</c>.
/// </summary>
public static class ColaDeserializador
{
    public static ColaParseada Parsear(JsonNode? nodo)
    {
        var acciones = new Dictionary<string, AccionLauncher>();
        var errores = new List<(string, string)>();
        if (nodo is null)
            return new ColaParseada(acciones, errores);

        if (nodo is JsonValue val && val.TryGetValue<string>(out var s) && (s == "null" || s.Length == 0))
            return new ColaParseada(acciones, errores);

        if (nodo is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] is null) continue;   // hueco null de RTDB
                if (arr[i] is not JsonObject obj)
                {
                    errores.Add(($"[{i}]", "Elemento de array no es un objeto"));
                    continue;
                }
                ParsearUno(obj, $"[{i}]", acciones, errores);
            }
        }
        else if (nodo is JsonObject mapa)
        {
            foreach (var (clave, valor) in mapa)
            {
                if (valor is null) continue;
                if (valor is JsonValue v2 && v2.TryGetValue<string>(out var nulo) && nulo == "null") continue;
                if (valor is not JsonObject obj2)
                {
                    errores.Add((clave, "Valor no es un objeto"));
                    continue;
                }
                ParsearUno(obj2, clave, acciones, errores);
            }
        }
        return new ColaParseada(acciones, errores);
    }

    private static void ParsearUno(JsonObject obj, string clave,
        Dictionary<string, AccionLauncher> acciones, List<(string, string)> errores)
    {
        try
        {
            var accion = JsonSerializer.Deserialize<AccionLauncher>(obj.ToJsonString(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (accion is null)
                errores.Add((clave, "Deserialización devolvió null"));
            else
                acciones[clave] = accion;
        }
        catch (JsonException ex)
        {
            errores.Add((clave, $"JSON inválido: {ex.Message}"));
        }
        catch (Exception ex)
        {
            errores.Add((clave, $"Error al leer la acción: {ex.Message}"));
        }
    }

    /// <summary>Parsea UNA acción individual (para LeerAccionAsync). Lanza si es inválida.</summary>
    public static AccionLauncher ParsearUna(JsonNode nodo)
    {
        if (nodo is not JsonObject obj)
            throw new JsonException("La acción no es un objeto JSON");
        return JsonSerializer.Deserialize<AccionLauncher>(obj.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new JsonException("La acción se deserializó como null");
    }
}

/// <summary>Nombres estables de versiones para las tablas de aceptación (evita depender del enum de PKHeX en el DTO).</summary>
public enum GameVersionPoke
{
    S = 1, R = 2, E = 3, FR = 4, LG = 5,
    HG = 7, SS = 8,
    D = 10, P = 11, Pt = 12,
    W = 20, B = 21, W2 = 22, B2 = 23,
    X = 24, Y = 25, AS = 26, OR = 27,
    SN = 30, MN = 31, US = 32, UM = 33,
    SW = 44, SH = 45,
    BD = 48, SP = 49,
    SL = 50, VL = 51,
    // Combinadas que devuelve PKHeX en algunos saves
    RS = 58, FRLG = 60, DP = 64, HGSS = 66, BW = 67, B2W2 = 68, XY = 69,
    ORAS = 71, SM = 72, USUM = 73, SWSH = 75, BDSP = 76, SV = 77,
}

/// <summary>Nombres de juego soportados por la app (sección 1 del doc de integración).</summary>
public static class JuegosApp
{
    public static readonly string[] Nombres =
    [
        "Rojo Fuego", "Verde Hoja", "Heartgold", "Soul Silver",
        "Rubi", "Zafiro", "Esmeralda",
        "Rubi Omega", "Zafiro Alfa",
        "Diamante", "Perla", "Platino",
        "Diamante brillante", "Perla Reluciente",
        "Blanco", "Negro", "Blanco 2", "Negro 2",
        "X", "Y", "Sol", "Luna", "Ultrasol", "Ultraluna",
        "Espada", "Escudo", "Escarlata", "Purpura",
    ];

    /// <summary>Normaliza un nombre de juego (minúsculas, sin tildes, sin espacios) para comparaciones.</summary>
    public static string Normalizar(string nombre) => nombre
        .ToLowerInvariant()
        .Replace("á", "a").Replace("é", "e").Replace("í", "i")
        .Replace("ó", "o").Replace("ú", "u").Replace("ü", "u")
        .Replace(" ", "");

    /// <summary>
    /// Conjunto de versiones PKHeX que acepta cada nombre de la app. PKHeX devuelve
    /// versiones combinadas en algunos saves (DP, RS, HGSS, FRLG…), así que la
    /// comparación es contra un CONJUNTO, nunca contra una única versión (v1.1.e).
    /// Rubi Omega → OR, Zafiro Alfa → AS (corregido del v1), y «Perla Reluciente» → SP.
    /// </summary>
    public static IReadOnlySet<GameVersionPoke> AVersionesAceptables(string nombreJuego) => Normalizar(nombreJuego) switch
    {
        "rojofuego" or "verdehoja" => new HashSet<GameVersionPoke> { GameVersionPoke.FR, GameVersionPoke.LG, GameVersionPoke.FRLG },
        "heartgold" => new HashSet<GameVersionPoke> { GameVersionPoke.HG, GameVersionPoke.HGSS },
        "soulsilver" => new HashSet<GameVersionPoke> { GameVersionPoke.SS, GameVersionPoke.HGSS },
        "rubi" => new HashSet<GameVersionPoke> { GameVersionPoke.R, GameVersionPoke.RS },
        "zafiro" => new HashSet<GameVersionPoke> { GameVersionPoke.S, GameVersionPoke.RS },
        "esmeralda" => new HashSet<GameVersionPoke> { GameVersionPoke.E, GameVersionPoke.RS },
        "rubiomega" => new HashSet<GameVersionPoke> { GameVersionPoke.OR, GameVersionPoke.ORAS },   // Omega Ruby = OR
        "zafiroalfa" => new HashSet<GameVersionPoke> { GameVersionPoke.AS, GameVersionPoke.ORAS },   // Alpha Sapphire = AS
        "diamante" => new HashSet<GameVersionPoke> { GameVersionPoke.D, GameVersionPoke.DP },
        "perla" => new HashSet<GameVersionPoke> { GameVersionPoke.P, GameVersionPoke.DP },
        "platino" => new HashSet<GameVersionPoke> { GameVersionPoke.Pt, GameVersionPoke.DP },
        "diamantebrillante" => new HashSet<GameVersionPoke> { GameVersionPoke.BD, GameVersionPoke.BDSP },
        "perlareluciente" => new HashSet<GameVersionPoke> { GameVersionPoke.SP, GameVersionPoke.BDSP },
        "negro" => new HashSet<GameVersionPoke> { GameVersionPoke.B, GameVersionPoke.BW },
        "blanco" => new HashSet<GameVersionPoke> { GameVersionPoke.W, GameVersionPoke.BW },
        "negro2" => new HashSet<GameVersionPoke> { GameVersionPoke.B2, GameVersionPoke.B2W2 },
        "blanco2" => new HashSet<GameVersionPoke> { GameVersionPoke.W2, GameVersionPoke.B2W2 },
        "x" => new HashSet<GameVersionPoke> { GameVersionPoke.X, GameVersionPoke.XY },
        "y" => new HashSet<GameVersionPoke> { GameVersionPoke.Y, GameVersionPoke.XY },
        "sol" => new HashSet<GameVersionPoke> { GameVersionPoke.SN, GameVersionPoke.SM },
        "luna" => new HashSet<GameVersionPoke> { GameVersionPoke.MN, GameVersionPoke.SM },
        "ultrasol" => new HashSet<GameVersionPoke> { GameVersionPoke.US, GameVersionPoke.USUM },
        "ultraluna" => new HashSet<GameVersionPoke> { GameVersionPoke.UM, GameVersionPoke.USUM },
        "espada" => new HashSet<GameVersionPoke> { GameVersionPoke.SW, GameVersionPoke.SWSH },
        "escudo" => new HashSet<GameVersionPoke> { GameVersionPoke.SH, GameVersionPoke.SWSH },
        "escarlata" => new HashSet<GameVersionPoke> { GameVersionPoke.SL, GameVersionPoke.SV },
        "purpura" => new HashSet<GameVersionPoke> { GameVersionPoke.VL, GameVersionPoke.SV },
        _ => new HashSet<GameVersionPoke>(),
    };

    /// <summary>
    /// ¿Acepta este save la acción con <paramref name="nombreJuego"/>? Un nombre desconocido
    /// se deja pasar (no bloqueamos lockes de versiones nuevas).
    /// </summary>
    public static bool AceptaSave(string nombreJuego, GameVersion versionSave)
    {
        if (string.IsNullOrWhiteSpace(nombreJuego)) return true;
        var aceptadas = AVersionesAceptables(nombreJuego);
        if (aceptadas.Count == 0) return true;   // nombre desconocido: dejar pasar
        foreach (var v in aceptadas)
            if ((GameVersion)v == versionSave) return true;
        return false;
    }
}
