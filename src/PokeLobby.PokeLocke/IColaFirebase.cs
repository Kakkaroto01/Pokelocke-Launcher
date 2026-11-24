using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Fachada mínima de Firebase para la cola <c>launcherQueue</c>. Permite probar
/// <see cref="ColaLauncherService"/> sin red (implementación simulada en los tests)
/// mientras <see cref="FirebaseRtdbClient"/> es la implementación real.
/// </summary>
public interface IColaFirebase
{
    /// <summary>Log de eventos/errores (deserialización, claims fallidos…). Opcional.</summary>
    Action<string>? Registro { get; set; }

    /// <summary>Lee la cola completa del jugador (mapa actionId → acción). Tolerante a null/array/huecos.</summary>
    Task<Dictionary<string, AccionLauncher>> LeerColaAsync(string lockeId, string playerTag, CancellationToken ct = default);

    /// <summary>Lee UNA acción individual con su ETag (para el claim condicional).</summary>
    Task<(AccionLauncher? Accion, string? ETag)> LeerAccionAsync(string lockeId, string playerTag, string actionId, CancellationToken ct = default);

    /// <summary>
    /// Reclama la acción: PUT del objeto COMPLETO con status=<c>applying</c>, claimedAt y claimedBy,
    /// condicionado por if-match del ETag leído. Devuelve false si otro launcher la reclamó (412).
    /// </summary>
    Task<bool> ReclamarAccionAsync(string lockeId, string playerTag, string actionId, AccionLauncher accion, CancellationToken ct = default);

    /// <summary>Publica el resultado de la acción (PATCH de status/appliedAt/appliedBy/resultMessage/errorMessage).</summary>
    Task MarcarResultadoAsync(string lockeId, string playerTag, string actionId,
        string status, string resultMessage, string? errorMessage = null, CancellationToken ct = default);

    /// <summary>
    /// Devuelve la acción a <c>pending</c> (contrato v1.1.c: cancelar ≠ fallar) limpiando
    /// claimedAt/claimedBy para que pueda reclamarse de nuevo.
    /// </summary>
    Task DevolverAPendienteAsync(string lockeId, string playerTag, string actionId, string motivo, CancellationToken ct = default);
}
