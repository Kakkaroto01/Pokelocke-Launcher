using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PokeLobby.PokeLocke;

/// <summary>Sesión de cuenta Firebase (email/password) tras login/registro/renovación.</summary>
public sealed record SesionCuenta(string IdToken, string RefreshToken, string Uid, string Email, int ExpiraEnSegundos);

/// <summary>
/// Cliente de Firebase Realtime Database por REST + SSE con **sesión de cuenta**:
/// login con el mismo email/password de la app PokeLocke (Firebase Email/Password Auth),
/// renovación con refresh token, lectura de los lockes de TU cuenta vía
/// <c>user_lockes/{uid}</c> y tu playerTag vía <c>users/{uid}</c>.
///
/// Configuración por variables de entorno:
///   POKELOCKE_FIREBASE_URL     → https://<proyecto>-default-rtdb.<region>.firebasedatabase.app
///   POKELOCKE_FIREBASE_API_KEY → Web API Key (Firebase Console → Configuración del proyecto)
///   POKELOCKE_FIREBASE_AUTH    → (opcional) token fijo si prefieres traerlo de fuera
/// </summary>
public sealed class FirebaseRtdbClient : IDisposable, IColaFirebase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly object _tokenLock = new();
    private string? _idToken;
    private string? _refreshTokenSesion;
    private DateTimeOffset _tokenCaduca;

    public Uri BaseUrl { get; }
    public string? ApiKey { get; }
    public string? Auth { get; }

    /// <summary>UID de la cuenta con sesión iniciada (null si es anónima/sin sesión).</summary>
    public string? Uid { get; private set; }
    public string? EmailSesion { get; private set; }
    public bool SesionIniciada => Uid is not null;
    /// <summary>El refresh token VIGENTE de la sesión (rota en cada renovación — persíguelo para restaurar sesiones).</summary>
    public string? RefresTokenActual { get { lock (_tokenLock) { return _refreshTokenSesion; } } }
    /// <summary>Se dispara cada vez que la sesión se renueva (para persistir el refresh token nuevo).</summary>
    public event Action<SesionCuenta>? SesionRenovada;

    public FirebaseRtdbClient(string? baseUrl = null, string? apiKey = null, string? auth = null, HttpMessageHandler? handler = null)
    {
        var raw = baseUrl ?? Environment.GetEnvironmentVariable("POKELOCKE_FIREBASE_URL");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Falta la URL de Firebase: define POKELOCKE_FIREBASE_URL (https://<proyecto>-default-rtdb.<region>.firebasedatabase.app).");
        BaseUrl = new Uri(raw.TrimEnd('/') + "/");
        ApiKey = apiKey ?? Environment.GetEnvironmentVariable("POKELOCKE_FIREBASE_API_KEY");
        Auth = auth ?? Environment.GetEnvironmentVariable("POKELOCKE_FIREBASE_AUTH");
        _http = handler is null ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _http.BaseAddress = BaseUrl;   // las rutas relativas («lockes/…») resuelven contra aquí
    }

    // ─────────────────────── cuenta (login email/password) ───────────────────────

    /// <summary>Inicia sesión con el mismo email/password de la app PokeLocke.</summary>
    public async Task<SesionCuenta> IniciarSesionAsync(string email, string password, CancellationToken ct = default)
        => await AutenticarCuentaAsync("accounts:signInWithPassword", email, password, ct);

    /// <summary>Crea una cuenta nueva (Firebase Email/Password).</summary>
    public async Task<SesionCuenta> CrearCuentaAsync(string email, string password, CancellationToken ct = default)
        => await AutenticarCuentaAsync("accounts:signUp", email, password, ct);

    /// <summary>Restaura una sesión a partir del refresh token persistido (sin pedir contraseña).</summary>
    public async Task<SesionCuenta> RestaurarSesionAsync(string refreshToken, CancellationToken ct = default)
        => await RenovarConRefreshAsync(refreshToken, ct);

    public void CerrarSesion()
    {
        lock (_tokenLock)
        {
            _idToken = null;
            _refreshTokenSesion = null;
            Uid = null;
            EmailSesion = null;
        }
    }

    private async Task<SesionCuenta> AutenticarCuentaAsync(string endpoint, string email, string password, CancellationToken ct)
    {
        var (estado, texto) = await IdentityToolkitAsync(endpoint, new { email, password, returnSecureToken = true }, ct);
        var nodo = ParseJson(texto);
        if ((int)estado >= 400)
            throw new InvalidOperationException(TraducirErrorAuth((string?)nodo["error"]?["message"] ?? texto));
        var sesion = SesionDesdeIdentityToolkit(nodo, email)
            ?? throw new InvalidOperationException("Respuesta de identidad incompleta (sin idToken).");
        AplicarSesion(sesion);
        return sesion;
    }

    private async Task<SesionCuenta> RenovarConRefreshAsync(string refreshToken, CancellationToken ct)
    {
        var (estado, texto) = await SecureTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);
        var nodo = ParseJson(texto);
        if ((int)estado >= 400)
            throw new InvalidOperationException(TraducirErrorAuth((string?)nodo["error"]?["message"] ?? texto));
        var sesion = SesionDesdeSecureToken(nodo)
            ?? throw new InvalidOperationException("Respuesta de renovación incompleta.");
        AplicarSesion(sesion);
        return sesion;
    }

    private void AplicarSesion(SesionCuenta sesion)
    {
        lock (_tokenLock)
        {
            _idToken = sesion.IdToken;
            _refreshTokenSesion = sesion.RefreshToken;
            _tokenCaduca = DateTimeOffset.UtcNow.AddSeconds(sesion.ExpiraEnSegundos);
            Uid = sesion.Uid;
            EmailSesion = sesion.Email;
        }
        if (!string.IsNullOrEmpty(sesion.RefreshToken))
            SesionRenovada?.Invoke(sesion);
    }

    private async Task<(HttpStatusCode Estado, string Texto)> IdentityToolkitAsync(string endpoint, object cuerpo, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ApiKey))
            throw new InvalidOperationException("Falta la Web API Key: define POKELOCKE_FIREBASE_API_KEY.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var contenido = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
        using var respuesta = await _http.PostAsync($"https://identitytoolkit.googleapis.com/v1/{endpoint}?key={ApiKey}", contenido, cts.Token);
        return (respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync(ct));
    }

    private async Task<(HttpStatusCode Estado, string Texto)> SecureTokenAsync(Dictionary<string, string> formulario, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ApiKey))
            throw new InvalidOperationException("Falta la Web API Key: define POKELOCKE_FIREBASE_API_KEY.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var contenido = new FormUrlEncodedContent(formulario);
        using var respuesta = await _http.PostAsync($"https://securetoken.googleapis.com/v1/token?key={ApiKey}", contenido, cts.Token);
        return (respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync(ct));
    }

    private static JsonNode? ParseJson(string texto)
        => texto.Length == 0 ? null : JsonNode.Parse(texto);

    private static SesionCuenta? SesionDesdeIdentityToolkit(JsonNode nodo, string emailFallback)
        => (string?)nodo["idToken"] is not { } idToken ? null : new SesionCuenta(
            IdToken: idToken,
            RefreshToken: (string?)nodo["refreshToken"] ?? "",
            Uid: (string?)nodo["localId"] ?? "",
            Email: (string?)nodo["email"] ?? emailFallback,
            ExpiraEnSegundos: int.TryParse((string?)nodo["expiresIn"], out var s) ? s : 3600);

    private static SesionCuenta? SesionDesdeSecureToken(JsonNode nodo)
        => (string?)nodo["id_token"] is not { } idToken ? null : new SesionCuenta(
            IdToken: idToken,
            RefreshToken: (string?)nodo["refresh_token"] ?? "",
            Uid: (string?)nodo["user_id"] ?? "",
            Email: (string?)nodo["email"] ?? "",
            ExpiraEnSegundos: int.TryParse((string?)nodo["expires_in"], out var s) ? s : 3600);

    private static string TraducirErrorAuth(string codigo)
        => codigo switch
        {
            "EMAIL_NOT_FOUND" or "INVALID_LOGIN_CREDENTIALS" or "INVALID_PASSWORD" => "Ese email no existe o la contraseña es incorrecta.",
            "EMAIL_EXISTS" => "Ya existe una cuenta con ese email (prueba a iniciar sesión).",
            "WEAK_PASSWORD : Password should be at least 6 characters" or "WEAK_PASSWORD" => "La contraseña debe tener al menos 6 caracteres.",
            "TOO_MANY_ATTEMPTS_TRY_LATER" => "Demasiados intentos fallidos: espera un minuto.",
            "ADMIN_ONLY_OPERATION" => "Ese método de acceso no está habilitado en el proyecto Firebase.",
            _ => $"Error de autenticación: {codigo}",
        };

    // ─────────────────────── perfil y lockes de la cuenta ───────────────────────

    /// <summary>Lee users/{uid} y deriva tu playerTag (usuario_discriminator, p. ej. «el_2861»).</summary>
    public async Task<string> MiPlayerTagAsync(CancellationToken ct = default)
    {
        if (Uid is null)
            throw new InvalidOperationException("Inicia sesión con tu cuenta primero.");
        var texto = await LeerRutaAsync($"users/{Uid}.json", ct);
        var nodo = texto == "null" ? null : JsonNode.Parse(texto)?.AsObject();
        var usuario = (string?)nodo?["usuario"];
        var discriminator = (string?)nodo?["discriminator"];
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(discriminator))
            throw new InvalidOperationException(
                "Tu cuenta no tiene perfil de PokeLocke todavía (crea el perfil desde la app).");
        return $"{usuario}_{discriminator}";
    }

    /// <summary>Lee user_lockes/{uid} → los ids de los lockes de TU cuenta.</summary>
    public async Task<List<string>> LeerIdsMisLockesAsync(CancellationToken ct = default)
    {
        if (Uid is null)
            throw new InvalidOperationException("Inicia sesión con tu cuenta primero.");
        var texto = await LeerRutaAsync($"user_lockes/{Uid}.json", ct);
        if (texto == "null" || texto.Length == 0) return [];
        var nodo = JsonNode.Parse(texto)?.AsObject();
        return nodo is null ? [] : nodo.Select(p => p.Key).ToList();
    }

    /// <summary>
    /// Crea un locke para tu cuenta: escribe /lockes/{id} con tu playerTag como jugador
    /// y lo vincula en user_lockes/{uid}. Devuelve el id nuevo.
    /// </summary>
    public async Task<string> CrearLockeAsync(string nombre, string juego, CancellationToken ct = default)
    {
        var tag = await MiPlayerTagAsync(ct);
        var id = Guid.NewGuid().ToString();
        var cuerpo = new
        {
            id,
            nombre,
            juego,
            codigo = Random.Shared.Next(0, 10000).ToString("D4"),
            isPkhexEnabled = true,
            isShinylocke = false,
            isMegasEnabled = false,
            isSoloRun = true,
            lastActive = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            players = new Dictionary<string, object>
            {
                [tag] = new { playerTag = tag, monedas = 0, prioridadOro = 0, ticketsPremium = 0, ticketsMentas = 0, ticketsEvolucion = 0, ticketsObjetos = 0 },
            },
        };
        var contenido = new StringContent(JsonSerializer.Serialize(cuerpo, Json), Encoding.UTF8, "application/json");
        using (var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Put, ConAuth($"lockes/{id}.json", token)) { Content = contenido }, ct: ct))
            respuesta.EnsureSuccessStatusCode();
        using (var vinculo = new StringContent("true", Encoding.UTF8, "application/json"))
        using (var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Put, ConAuth($"user_lockes/{Uid}/{id}.json", token)) { Content = vinculo }, ct: ct))
            respuesta.EnsureSuccessStatusCode();
        return id;
    }

    // ─────────────────────── token (cuenta → fijo → anónimo) ───────────────────────

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        lock (_tokenLock)
        {
            if (_idToken is not null && DateTimeOffset.UtcNow < _tokenCaduca.AddMinutes(-2))
                return _idToken;
        }
        // 1) sesión de cuenta: renovar con el refresh token persistido
        if (_refreshTokenSesion is not null && !string.IsNullOrEmpty(ApiKey))
        {
            await RenovarConRefreshAsync(_refreshTokenSesion, ct);
            lock (_tokenLock)
            {
                if (_idToken is not null && DateTimeOffset.UtcNow < _tokenCaduca.AddMinutes(-1))
                    return _idToken;
            }
        }
        // 2) token fijo del llamador
        if (!string.IsNullOrEmpty(Auth)) return Auth;
        // 3) anónimo (solo rutas públicas)
        if (!string.IsNullOrEmpty(ApiKey))
        {
            var (estado, texto) = await IdentityToolkitAsync("accounts:signUp", new { returnSecureToken = true }, ct);
            var nodo = ParseJson(texto);
            var idToken = (string?)nodo?["idToken"];
            if (idToken is not null)
            {
                lock (_tokenLock)
                {
                    _idToken = idToken;
                    _tokenCaduca = DateTimeOffset.UtcNow.AddSeconds(
                        int.TryParse((string?)nodo?["expiresIn"], out var segundos) ? segundos : 3600);
                }
                return idToken;
            }
        }
        return "";
    }

    private async Task<HttpResponseMessage> EnviarConAuthAsync(Func<string, HttpRequestMessage> fabrica, bool reintentar401 = true, CancellationToken ct = default)
    {
        var token = await TokenAsync(ct);
        using var peticion = fabrica(token);   // la fábrica recibe el TOKEN y construye la ruta autenticada
        // La respuesta se devuelve VIVA: la dispone el llamador (su using).
        var respuesta = await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, ct);
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized && reintentar401)
        {
            lock (_tokenLock) { _idToken = null; }
            respuesta.Dispose();
            token = await TokenAsync(ct);
            using var reintento = fabrica(token);
            return await _http.SendAsync(reintento, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        return respuesta;
    }

    private async Task<string> LeerRutaAsync(string ruta, CancellationToken ct)
    {
        using var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Get, ConAuth(ruta, token)), ct: ct);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadAsStringAsync(ct);
    }

    private static string ConAuth(string ruta, string token) => string.IsNullOrEmpty(token)
        ? ruta
        : $"{ruta}{(ruta.Contains('?') ? "&" : "?")}auth={Uri.EscapeDataString(token)}";

    // ─────────────────────── operaciones de datos ───────────────────────

    /// <summary>GET /lockes.json → todos los lockes (id → Locke), para administración.</summary>
    public async Task<Dictionary<string, Locke>> ListarLockesAsync(CancellationToken ct = default)
    {
        using var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Get, ConAuth("lockes.json", token)), ct: ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return new Dictionary<string, Locke>();
        respuesta.EnsureSuccessStatusCode();
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        return texto == "null" || texto.Length == 0
            ? new Dictionary<string, Locke>()
            : JsonSerializer.Deserialize<Dictionary<string, Locke>>(texto, Json) ?? new Dictionary<string, Locke>();
    }

    /// <summary>PUT /lockes/{id}/vivo/{timestamp}.json → publica eventos en vivo (RAM-vivo) para que la app los lea.</summary>
    /// <summary>Publica el equipo real del .sav en myTeam (la app lo escucha en tiempo real).</summary>
    public async Task<bool> EscribirEquipoAsync(string lockeId, string playerTag, object equipo, CancellationToken ct = default)
    {
        var contenido = new StringContent(JsonSerializer.Serialize(equipo, Json), Encoding.UTF8, "application/json");
        using var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Put,
            ConAuth($"lockes/{lockeId}/players/{Uri.EscapeDataString(playerTag)}/myTeam.json", token)) { Content = contenido }, ct: ct);
        return respuesta.IsSuccessStatusCode;
    }

    public async Task<bool> GuardarEventosVivoAsync(string lockeId, List<object> eventos, CancellationToken ct = default)
    {
        var marca = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var contenido = new StringContent(JsonSerializer.Serialize(eventos, Json), Encoding.UTF8, "application/json");
        using var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Put,
            ConAuth($"lockes/{lockeId}/vivo/{marca}.json", token)) { Content = contenido }, ct: ct);
        return respuesta.IsSuccessStatusCode;
    }

    // ─────────────────────── cola launcherQueue (IColaFirebase) ───────────────────────

    /// <summary>Log de eventos/errores de cola (errores de deserialización, claims fallidos…).</summary>
    public Action<string>? Registro { get; set; }

    /// <summary>Clase de versión del launcher para appliedBy/claimedBy.</summary>
    public const string AppVersion = "LockeLauncher 1.1";

    private string RutaCola(string lockeId, string playerTag)
        => $"lockes/{lockeId}/players/{Uri.EscapeDataString(playerTag)}/launcherQueue";

    private string RutaAccion(string lockeId, string playerTag, string actionId)
        => $"{RutaCola(lockeId, playerTag)}/{actionId}.json";

    /// <summary>
    /// Lee la cola completa del jugador (GET …/launcherQueue.json) y la parsea de forma
    /// tolerante (acepta null, array con huecos, objeto con claves push). Las entradas
    /// inválidas se registran en <see cref="Registro"/> (nunca se descartan en silencio).
    /// </summary>
    public async Task<Dictionary<string, AccionLauncher>> LeerColaAsync(
        string lockeId, string playerTag, CancellationToken ct = default)
    {
        var ruta = $"{RutaCola(lockeId, playerTag)}.json";
        using var respuesta = await EnviarConAuthAsync(token =>
        {
            var p = new HttpRequestMessage(HttpMethod.Get, ConAuth(ruta, token));
            return p;
        }, ct: ct);
        respuesta.EnsureSuccessStatusCode();
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        var nodo = texto == "null" || texto.Length == 0 ? null : JsonNode.Parse(texto);
        var parseada = ColaDeserializador.Parsear(nodo);
        foreach (var (clave, motivo) in parseada.Errores)
        {
            Registro?.Invoke($"⚠ cola: entrada «{clave}» ilegible ({motivo})");
            // No se oculta: se marca failed en Firebase para que el jugador lo vea en el móvil
            try
            {
                await MarcarResultadoAsync(lockeId, playerTag, clave, AccionEstado.Fallida,
                    $"JSON inválido: {motivo}", motivo, ct);
            }
            catch (Exception ex)
            {
                Registro?.Invoke($"⚠ no se pudo marcar la entrada «{clave}» como failed: {ex.Message}");
            }
        }
        return parseada.Acciones;
    }

    /// <summary>
    /// Lee UNA acción individual con su ETag: GET …/launcherQueue/{actionId}.json con
    /// <c>X-Firebase-ETag</c>. Devuelve (null, null) si la acción no existe.
    /// </summary>
    public async Task<(AccionLauncher? Accion, string? ETag)> LeerAccionAsync(
        string lockeId, string playerTag, string actionId, CancellationToken ct = default)
    {
        var ruta = RutaAccion(lockeId, playerTag, actionId);
        using var respuesta = await EnviarConAuthAsync(token =>
        {
            var p = new HttpRequestMessage(HttpMethod.Get, ConAuth(ruta, token));
            p.Headers.Add("X-Firebase-ETag", "true");
            return p;
        }, ct: ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return (null, null);
        respuesta.EnsureSuccessStatusCode();
        var etag = respuesta.Headers.ETag?.Tag ?? "";
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        if (texto == "null" || texto.Length == 0) return (null, etag);
        try
        {
            return (ColaDeserializador.ParsearUna(JsonNode.Parse(texto)!), etag);
        }
        catch (Exception ex)
        {
            Registro?.Invoke($"⚠ acción «{actionId}» ilegible: {ex.Message}");
            return (null, etag);
        }
    }

    /// <summary>
    /// Reclama la acción (contrato v1.1): PUT del objeto COMPLETO con status=<c>applying</c>,
    /// <c>claimedAt</c> y <c>claimedBy</c>, condicionado por <c>if-match</c> del ETag del nodo
    /// de ESA acción. Si otro launcher la reclamó antes → 412 → false.
    /// </summary>
    public async Task<bool> ReclamarAccionAsync(string lockeId, string playerTag, string actionId, AccionLauncher accion, CancellationToken ct = default)
    {
        var ruta = RutaAccion(lockeId, playerTag, actionId);

        // 1) ETag fresco del nodo de ESTA acción (no de la cola completa)
        var (actual, etag) = await LeerAccionAsync(lockeId, playerTag, actionId, ct);
        if (actual is null) return false;   // desapareció
        var copia = actual.Clonar();
        copia.Status = AccionEstado.Aplicando;
        copia.ClaimedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        copia.ClaimedBy = AppVersion;

        // 2) PUT del objeto completo con if-match
        using var respuesta = await EnviarConAuthAsync(token =>
        {
            var contenido = new StringContent(
                JsonSerializer.Serialize(copia, Json), Encoding.UTF8, "application/json");   // dentro de la fábrica: el reintento 401 no reutiliza contenido consumido
            var p = new HttpRequestMessage(HttpMethod.Put, ConAuth(ruta, token)) { Content = contenido };
            p.Headers.Add("if-match", etag ?? "");
            return p;
        }, ct: ct);
        if ((int)respuesta.StatusCode == 412) return false;   // PreconditionFailed: no es nuestra
        respuesta.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// PATCH del resultado de una acción (status, appliedAt, appliedBy, resultMessage, errorMessage).
    /// Usa PATCH de RTDB para conservar el resto de campos (incluidos claimedAt/claimedBy).
    /// </summary>
    public async Task MarcarResultadoAsync(string lockeId, string playerTag, string actionId,
        string status, string resultMessage, string? errorMessage = null, CancellationToken ct = default)
    {
        var ruta = RutaAccion(lockeId, playerTag, actionId);
        using var respuesta = await EnviarConAuthAsync(token =>
        {
            var patch = new
            {
                status,
                appliedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                appliedBy = AppVersion,
                resultMessage,
                errorMessage,
            };
            var contenido = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json");   // dentro de la fábrica (reintento 401)
            return new HttpRequestMessage(new HttpMethod("PATCH"), ConAuth(ruta, token)) { Content = contenido };
        }, ct: ct);
        respuesta.EnsureSuccessStatusCode();
    }

    /// <summary>Devuelve la acción a pending (cancelación del usuario): limpia el claim.</summary>
    public async Task DevolverAPendienteAsync(string lockeId, string playerTag, string actionId, string motivo, CancellationToken ct = default)
    {
        var ruta = RutaAccion(lockeId, playerTag, actionId);
        using var respuesta = await EnviarConAuthAsync(token =>
        {
            var patch = new
            {
                status = AccionEstado.Pendiente,
                claimedAt = (long?)null,
                claimedBy = (string?)null,
                resultMessage = motivo,
            };
            var contenido = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json");
            return new HttpRequestMessage(new HttpMethod("PATCH"), ConAuth(ruta, token)) { Content = contenido };
        }, ct: ct);
        respuesta.EnsureSuccessStatusCode();
    }

    /// <summary>GET /lockes/{lockeId}.json → el locke completo (null si no existe).</summary>
    public async Task<Locke?> ObtenerLockeAsync(string lockeId, CancellationToken ct = default)
    {
        using var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Get, ConAuth($"lockes/{lockeId}.json", token)), ct: ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return null;
        respuesta.EnsureSuccessStatusCode();
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        return texto == "null" ? null : JsonSerializer.Deserialize<Locke>(texto, Json);
    }

    /// <summary>GET /lockes/{id}/players/{tag}/myTeam.json → el equipo del jugador en la app.</summary>
    public async Task<MyTeam?> LeerEquipoAsync(string lockeId, string playerTag, CancellationToken ct = default)
    {
        using var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Get,
            ConAuth($"lockes/{lockeId}/players/{Uri.EscapeDataString(playerTag)}/myTeam.json", token)), ct: ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return null;
        respuesta.EnsureSuccessStatusCode();
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        return texto == "null" ? null : JsonSerializer.Deserialize<MyTeam>(texto, Json);
    }

    /// <summary>PUT /lockes/{id}/players/{tag}/myTeam.json → escribe el equipo del jugador.</summary>
    public async Task GuardarEquipoAsync(string lockeId, string playerTag, MyTeam equipo, CancellationToken ct = default)
    {
        var contenido = new StringContent(JsonSerializer.Serialize(equipo, Json), Encoding.UTF8, "application/json");
        using var respuesta = await EnviarConAuthAsync(token => new HttpRequestMessage(HttpMethod.Put,
            ConAuth($"lockes/{lockeId}/players/{Uri.EscapeDataString(playerTag)}/myTeam.json", token)) { Content = contenido }, ct: ct);
        respuesta.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Suscripción en tiempo real (REST streaming de Firebase): cada cambio en
    /// /lockes/{lockeId} llega como evento SSE con {path, data}. El callback recibe el JSON
    /// crudo de cada evento; lanza OperationCanceledException al cancelar.
    /// </summary>
    public async Task SuscribirLockeAsync(string lockeId, Action<string> alEvento, CancellationToken ct = default)
    {
        using var respuesta = await EnviarConAuthAsync(token =>
        {
            var peticion = new HttpRequestMessage(HttpMethod.Get, ConAuth($"lockes/{lockeId}.json", token));
            peticion.Headers.Accept.ParseAdd("text/event-stream");
            return peticion;
        }, ct: ct);
        respuesta.EnsureSuccessStatusCode();
        await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
        using var lector = new StreamReader(flujo, Encoding.UTF8);
        while (!ct.IsCancellationRequested)
        {
            var linea = await lector.ReadLineAsync(ct);
            if (linea is null) return;   // conexión cerrada por el servidor
            if (linea.StartsWith("data: ", StringComparison.Ordinal))
                alEvento(linea["data: ".Length..]);
        }
    }

    public void Dispose() => _http.Dispose();
}