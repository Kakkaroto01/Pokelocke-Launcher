using System;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PokeLobby.Client.Dtos;

namespace PokeLobby.Client;

/// <summary>
/// Excepción del protocolo de Pokelobby. Contrato de error observado en vivo:
/// 401 de negocio → {"error":"Email o contraseña incorrectos."};
/// 500 del manejador global → {"statusCode":500,"message":"Error interno del servidor."}.
/// </summary>
public sealed class PokelobbyApiException : Exception
{
    public int StatusCode { get; }
    /// <summary>Texto del campo "error" (o "message") del cuerpo JSON, si llegó.</summary>
    public string? ErrorCode { get; }
    public bool IsUnauthorized => StatusCode == (int)HttpStatusCode.Unauthorized;

    public PokelobbyApiException(int statusCode, string? error, string? message = null)
        : base(message ?? error ?? $"HTTP {statusCode}")
    {
        StatusCode = statusCode;
        ErrorCode = error;
    }
}

/// <summary>
/// Cliente del protocolo REST de Pokelobby, recreado a partir del análisis estático
/// del launcher original (INFORME-PokeLobby.md §2.3) y verificado en vivo con sondas
/// inocuas. La URL base se resuelve exactamente igual que el launcher oficial:
/// variable de entorno <c>POKELOBBY_API</c> o el valor por defecto en Render.
/// </summary>
public sealed class PokelobbyClient : IDisposable
{
    public const string DefaultBaseUrl = "https://api-pokemon-main-a21032e.onrender.com";

    /// <summary>Serialización estilo Web (camelCase + lectura insensible a mayúsculas).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private string? _accessToken;

    public Uri BaseUrl { get; }

    /// <summary>Token persistente de sesión (equivale al refreshToken de session.json). Trátalo como secreto.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Access token en memoria (se renueva con RefreshAsync / reintento automático en 401).</summary>
    public string? AccessToken => _accessToken;

    public bool HasSession => RefreshToken is not null;

    public PokelobbyClient(string? baseUrl = null, string? refreshToken = null, HttpMessageHandler? handler = null)
    {
        BaseUrl = new Uri(ResolveBaseUrl(baseUrl).TrimEnd('/') + "/");
        RefreshToken = refreshToken;
        _http = handler is null
            ? new HttpClient { BaseAddress = BaseUrl, Timeout = TimeSpan.FromSeconds(90) }
            : new HttpClient(handler, disposeHandler: true) { BaseAddress = BaseUrl, Timeout = TimeSpan.FromSeconds(90) };
        // 90 s de timeout: el servidor real vive en Render y puede tener cold start.
    }

    /// <summary>Misma resolución de URL que el launcher: POKELOBBY_API o el valor por defecto.</summary>
    public static string ResolveBaseUrl(string? baseUrl = null)
        => !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl!.Trim()
         : (Environment.GetEnvironmentVariable("POKELOBBY_API") ?? DefaultBaseUrl).Trim();

    // ─────────────────────────────── Auth ───────────────────────────────

    /// <summary>POST /api/auth/login con {email, password}. Adopta los tokens si la respuesta los trae.</summary>
    public async Task<JsonNode?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Post, "api/auth/login", new { email, password }, authed: false, allowRetry: false, ct);
        AdoptSession(node);
        return node;
    }

    /// <summary>POST /api/auth/refresh con {refreshToken}. Renueva el access token.</summary>
    public async Task<JsonNode?> RefreshAsync(CancellationToken ct = default)
    {
        if (RefreshToken is null)
            throw new PokelobbyApiException(0, null, "No hay refreshToken en la sesión (usa LoginAsync primero).");
        var node = await SendAsync(HttpMethod.Post, "api/auth/refresh", new { refreshToken = RefreshToken }, authed: false, allowRetry: false, ct);
        AdoptSession(node);
        return node;
    }

    /// <summary>POST /api/auth/logout (autenticado) y limpieza de la sesión local.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try { await SendAsync(HttpMethod.Post, "api/auth/logout", null, authed: true, allowRetry: false, ct); }
        finally { _accessToken = null; RefreshToken = null; }
    }

    /// <summary>GET /api/auth/{provider}/url?redirect_uri=… → {"url": …} (proveedores observados: google, github).</summary>
    public async Task<string?> GetOAuthUrlAsync(string provider, string redirectUri, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get,
            $"api/auth/{Uri.EscapeDataString(provider)}/url?redirect_uri={Uri.EscapeDataString(redirectUri)}",
            null, authed: false, allowRetry: false, ct);
        return (string?)node?["url"];
    }

    /// <summary>POST /api/auth/{provider}/exchange con {code, redirectUri} (flujo OAuth con callback local).</summary>
    public async Task<JsonNode?> ExchangeOAuthCodeAsync(string provider, string code, string redirectUri, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Post, $"api/auth/{Uri.EscapeDataString(provider)}/exchange",
            new { code, redirectUri }, authed: false, allowRetry: false, ct);
        AdoptSession(node);
        return node;
    }

    // ─────────────────────────────── Salas ──────────────────────────────

    /// <summary>GET /api/salas/mis-salas (crudo).</summary>
    public Task<JsonNode?> GetMisSalasRawAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "api/salas/mis-salas", null, authed: true, allowRetry: true, ct);

    /// <summary>GET /api/salas/mis-salas → lista de salas del jugador.</summary>
    public async Task<List<RoomInfo>?> GetMisSalasAsync(CancellationToken ct = default)
        => (await GetMisSalasRawAsync(ct))?.Deserialize<List<RoomInfo>>(Json);

    /// <summary>PATCH /api/salas/activa — marcar la sala activa (cuerpo inferido {salaId}).</summary>
    public Task<JsonNode?> SetSalaActivaAsync(int salaId, CancellationToken ct = default)
        => SendAsync(new HttpMethod("PATCH"), "api/salas/activa", new { salaId }, authed: true, allowRetry: true, ct);

    /// <summary>GET /api/salas/{id}/randomizer?version=N.</summary>
    public Task<JsonNode?> GetRandomizerSalaAsync(int salaId, int version, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"api/salas/{salaId}/randomizer?version={version}", null, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/salas/{id}/randomizer — publicar la configuración del randomizer (staff).</summary>
    public Task<JsonNode?> PublicarRandomizerSalaAsync(int salaId, object configuracion, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"api/salas/{salaId}/randomizer", configuracion, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/salas/{id}/organizacion/mods — publicar los mods permitidos.</summary>
    public Task<JsonNode?> PublicarModsPermitidosAsync(int salaId, object mods, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"api/salas/{salaId}/organizacion/mods", mods, authed: true, allowRetry: true, ct);

    // ──────────────────── Partidas / integridad / vivo ──────────────────

    /// <summary>POST /api/saves/showdown — subida de progreso de partida.</summary>
    public Task<JsonNode?> SendShowdownAsync(ShowdownUpload payload, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/saves/showdown", payload, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/saves/vivo — eventos en vivo leídos de la RAM del emulador ({salaId, eventos}).</summary>
    public Task<JsonNode?> EnviarEventosVivoAsync(int salaId, object eventos, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/saves/vivo", new { salaId, eventos }, authed: true, allowRetry: true, ct);

    /// <summary>GET /api/saves/integridad/estado?salaId=.</summary>
    public Task<JsonNode?> GetIntegridadEstadoAsync(int salaId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"api/saves/integridad/estado?salaId={salaId}", null, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/saves/integridad/evento — reportar un evento de integridad.</summary>
    public Task<JsonNode?> EnviarEventoIntegridadAsync(object evento, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/saves/integridad/evento", evento, authed: true, allowRetry: true, ct);

    /// <summary>GET /api/saves/integridad/bloqueos.</summary>
    public Task<JsonNode?> GetBloqueosAlertaAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "api/saves/integridad/bloqueos", null, authed: true, allowRetry: true, ct);

    /// <summary>GET /api/saves/integridad/mods?salaId=.</summary>
    public Task<JsonNode?> GetModsSalaAsync(int salaId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"api/saves/integridad/mods?salaId={salaId}", null, authed: true, allowRetry: true, ct);

    /// <summary>GET /api/saves/integridad/verificaciones/pendientes.</summary>
    public Task<JsonNode?> GetVerificacionesPendientesAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "api/saves/integridad/verificaciones/pendientes", null, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/saves/integridad/verificaciones/{id}/informe.</summary>
    public Task<JsonNode?> EnviarInformeVerificacionAsync(int verificacionId, object informe, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"api/saves/integridad/verificaciones/{verificacionId}/informe", informe, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/saves/presencia-sala — fijar presencia en la sala activa.</summary>
    public Task<JsonNode?> SetPresenciaSalaAsync(int salaId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/saves/presencia-sala", new { salaId }, authed: true, allowRetry: true, ct);

    /// <summary>DELETE /api/saves/presencia-sala — salir de la presencia de sala.</summary>
    public Task<JsonNode?> ClearPresenciaSalaAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, "api/saves/presencia-sala", null, authed: true, allowRetry: true, ct);

    /// <summary>DELETE /api/saves/presencia — borrar la presencia del launcher.</summary>
    public Task<JsonNode?> ClearPresenciaLauncherAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, "api/saves/presencia", null, authed: true, allowRetry: true, ct);

    // ─────────────────────────── Economía / items ───────────────────────

    /// <summary>GET /api/items/inventario?salaId=.</summary>
    public async Task<List<InventoryItem>?> GetInventarioAsync(int salaId, CancellationToken ct = default)
        => (await SendAsync(HttpMethod.Get, $"api/items/inventario?salaId={salaId}", null, authed: true, allowRetry: true, ct))
            ?.Deserialize<List<InventoryItem>>(Json);

    /// <summary>GET /api/items/historial?salaId=.</summary>
    public async Task<List<HistorialEntrega>?> GetHistorialAsync(int salaId, CancellationToken ct = default)
        => (await SendAsync(HttpMethod.Get, $"api/items/historial?salaId={salaId}", null, authed: true, allowRetry: true, ct))
            ?.Deserialize<List<HistorialEntrega>>(Json);

    /// <summary>POST /api/items/inventario/usar — usar/canjear un item.</summary>
    public Task<JsonNode?> UsarItemAsync(object payload, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/items/inventario/usar", payload, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/items/revivir — pedir revivir un Pokémon.</summary>
    public Task<JsonNode?> SolicitarRevivirAsync(object payload, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/items/revivir", payload, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/items/aplicar-menta — aplicar una menta (cambiar naturaleza).</summary>
    public Task<JsonNode?> SolicitarAplicarMentaAsync(object payload, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/items/aplicar-menta", payload, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/items/inventario/{id}/entregar — aceptar la entrega de una carta.</summary>
    public Task<JsonNode?> EntregarResultadoAsync(int itemId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"api/items/inventario/{itemId}/entregar", null, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/items/inventario/{id}/descartar — descartar la entrega de una carta.</summary>
    public Task<JsonNode?> DescartarInventarioAsync(int itemId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"api/items/inventario/{itemId}/descartar", null, authed: true, allowRetry: true, ct);

    /// <summary>GET /api/items/egg/{id} — resultado del huevo misterioso.</summary>
    public async Task<EggPokemonResult?> GetEggPokemonAsync(int eggId, CancellationToken ct = default)
        => (await SendAsync(HttpMethod.Get, $"api/items/egg/{eggId}", null, authed: true, allowRetry: true, ct))
            ?.Deserialize<EggPokemonResult>(Json);

    /// <summary>GET /api/intercambios/{id} — estado de un intercambio.</summary>
    public async Task<TradeDetail?> GetTradeAsync(int tradeId, CancellationToken ct = default)
        => (await SendAsync(HttpMethod.Get, $"api/intercambios/{tradeId}", null, authed: true, allowRetry: true, ct))
            ?.Deserialize<TradeDetail>(Json);

    /// <summary>GET /api/items/mt/catalogo?salaId= — catálogo de MT de la sala.</summary>
    public Task<JsonNode?> GetMtCatalogoAsync(int salaId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"api/items/mt/catalogo?salaId={salaId}", null, authed: true, allowRetry: true, ct);

    // ───────────────────────────── Asistente ────────────────────────────

    /// <summary>POST /api/asistente/preguntar.</summary>
    public Task<JsonNode?> PreguntarAsistenteAsync(object payload, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/asistente/preguntar", payload, authed: true, allowRetry: true, ct);

    /// <summary>POST /api/asistente/tickets.</summary>
    public Task<JsonNode?> CrearTicketAsistenteAsync(object payload, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "api/asistente/tickets", payload, authed: true, allowRetry: true, ct);

    // ───────────────────────── Hub (sonda) ──────────────────────────────

    /// <summary>
    /// Sonda del hub SignalR: POST /hubs/notificaciones/negotiate sin token.
    /// Verificado en vivo → 401 + WWW-Authenticate: Bearer. Para conectarse de
    /// verdad usa <see cref="PokelobbyNotificationsHub"/>.
    /// </summary>
    public async Task<(int Estado, string? WwwAuthenticate)> ProbeHubNegotiateAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "hubs/notificaciones/negotiate?negotiateVersion=1");
        using var response = await _http.SendAsync(request, ct);
        var www = response.Headers.WwwAuthenticate?.ToString();
        return ((int)response.StatusCode, string.IsNullOrEmpty(www) ? null : www);
    }

    // ───────────────────────── Plumbling HTTP ───────────────────────────

    private async Task<JsonNode?> SendAsync(HttpMethod method, string url, object? body, bool authed, bool allowRetry, CancellationToken ct)
    {
        var response = await SendCoreAsync(method, url, body, authed, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized && authed && allowRetry && RefreshToken is not null)
        {
            // 401 → refrescar token → reintentar una vez, igual que el launcher original.
            response.Dispose();
            await RefreshAsync(ct);
            response = await SendCoreAsync(method, url, body, authed, ct);
        }
        try
        {
            if (!response.IsSuccessStatusCode) throw await ToErrorAsync(response, ct);
            return await ReadJsonAsync(response, ct);
        }
        finally { response.Dispose(); }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body, bool authed, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, url);
        try
        {
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
            if (authed && _accessToken is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        finally { request.Dispose(); }
    }

    private static async Task<PokelobbyApiException> ToErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? error = null;
        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > 0)
            {
                var node = JsonNode.Parse(Encoding.UTF8.GetString(bytes));
                error = (string?)(node?["error"] ?? node?["message"]);
            }
        }
        catch (JsonException) { /* cuerpo no JSON */ }
        return new PokelobbyApiException((int)response.StatusCode, error);
    }

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content is null) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length == 0 ? null : JsonNode.Parse(Encoding.UTF8.GetString(bytes));
    }

    /// <summary>Adopta refreshToken/accessToken de una respuesta de sesión (claves insensibles a mayúsculas).</summary>
    private void AdoptSession(JsonNode? node)
    {
        if (node is not JsonObject map) return;
        foreach (var (key, value) in map)
        {
            switch (key?.ToLowerInvariant())
            {
                case "refreshtoken" when value is not null:
                    RefreshToken = value.ToString();
                    break;
                case "accesstoken" or "token" when value is not null:
                    _accessToken = value.ToString();
                    break;
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
