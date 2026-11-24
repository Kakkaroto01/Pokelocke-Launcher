using Microsoft.AspNetCore.SignalR.Client;
using PokeLobby.Client.Dtos;

namespace PokeLobby.Client;

/// <summary>
/// Cliente del hub SignalR {POKELOBBY_API}/hubs/notificaciones con reconexión
/// automática y el único evento observado en el launcher original:
/// «CartaRecibida» (el servidor empuja las cartas del árbitro).
/// </summary>
public sealed class PokelobbyNotificationsHub : IAsyncDisposable
{
    private HubConnection? _connection;

    public Uri BaseUrl { get; }

    public PokelobbyNotificationsHub(string? baseUrl = null)
        => BaseUrl = new Uri(PokelobbyClient.ResolveBaseUrl(baseUrl).TrimEnd('/') + "/");

    /// <summary>
    /// Conecta al hub y registra el manejador de cartas recibidas.
    /// El proveedor de token se consulta en cada (re)conexión: pásale una función
    /// que devuelva un access token vigente (p. ej. refrescándolo con <see cref="PokelobbyClient.RefreshAsync"/>).
    /// </summary>
    public async Task ConnectAsync(
        Func<Task<string?>> accessTokenProvider,
        Action<CartaRecibidaNotificacion> onCartaRecibida,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(BaseUrl, "hubs/notificaciones"), options => options.AccessTokenProvider = accessTokenProvider)
            .WithAutomaticReconnect()
            .Build();

        connection.On<CartaRecibidaNotificacion>("CartaRecibida", onCartaRecibida);
        connection.Closed += error =>
        {
            log?.Invoke($"conexión cerrada: {error?.Message ?? "-"}");
            return Task.CompletedTask;
        };
        connection.Reconnecting += error =>
        {
            log?.Invoke("reconectando…");
            return Task.CompletedTask;
        };
        connection.Reconnected += id =>
        {
            log?.Invoke($"reconectado ({id})");
            return Task.CompletedTask;
        };

        _connection = connection;
        await connection.StartAsync(ct);
        log?.Invoke("conectado a /hubs/notificaciones");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_connection is not null) await _connection.StopAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is not null) await connection.DisposeAsync();
    }
}
