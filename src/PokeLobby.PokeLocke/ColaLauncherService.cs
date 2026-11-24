using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PokeLobby.PokeLocke;

/// <summary>Resultado de procesar una acción individual. Verificador: null = OK, string = motivo de fallo.</summary>
public sealed record ResultadoAccion(
    string ActionId, string Estado, string ResultMessage, string? ErrorMessage = null,
    Func<SaveFile, string?>? Verificador = null);

/// <summary>Ubicación de un Pokémon dentro del save (equipo i-ésimo o caja/slot).</summary>
public sealed record UbicacionPkm(int PartyIndex, int Box, int Slot)
{
    public static UbicacionPkm EnEquipo(int indice) => new(indice, -1, -1);
    public static UbicacionPkm EnCaja(int caja, int slot) => new(-1, caja, slot);
    public bool EsEquipo => PartyIndex >= 0;
}

/// <summary>Objetivo de diálogo: ubicación + PKM, con etiqueta legible «Equipo 2» / «Caja 3 · slot 12».</summary>
public sealed record ObjetivoPkm(UbicacionPkm Ubicacion, PKM Pkm)
{
    public string Etiqueta()
    {
        var strings = GameInfo.GetStrings("es");
        var nombre = string.IsNullOrWhiteSpace(Pkm.Nickname)
            ? (Pkm.Species > 0 && Pkm.Species < strings.Species.Count ? strings.Species[Pkm.Species] : "?")
            : Pkm.Nickname;
        var lugar = Ubicacion.EsEquipo
            ? $"Equipo {Ubicacion.PartyIndex + 1}"
            : $"Caja {Ubicacion.Box + 1} · slot {Ubicacion.Slot + 1}";
        return $"{lugar} — {nombre} Nv{Pkm.Stat_Level}";
    }
}

/// <summary>
/// Procesa la cola <c>launcherQueue</c> del jugador (contrato v1.1):
/// · Ordena por <c>createdAt</c>, reclama cada acción con ETag del nodo individual
///   (PUT del objeto COMPLETO con claimedAt/claimedBy; 412 → la reclamó otro launcher).
/// · Una acción <c>applying</c> con claimedAt de más de 10 min vuelve a tratarse como pending.
/// · Aplica cada efecto sobre el .sav EN MEMORIA, escribe UNA vez al final (con el emulador
///   cerrado), RELEE el archivo de disco y ejecuta los verificadores de cada efecto.
/// · Solo tras verificar marca <c>applied</c> en Firebase y registra la idempotencia local.
/// · Si la verificación falla: restaura el backup y marca <c>failed</c>.
/// · Cancelar un diálogo NO es fallar: la acción vuelve a <c>pending</c>.
/// </summary>
public sealed partial class ColaLauncherService
{
    private readonly IColaFirebase _firebase;
    private readonly string _datosDir;
    private readonly Func<bool> _emuladorAbierto;

    /// <summary>Caducidad de un claim: una acción applying con claimedAt más antigua se re-procesa.</summary>
    public static readonly long CaducidadClaimMs = (long)TimeSpan.FromMinutes(10).TotalMilliseconds;

    /// <summary>Número de acciones pendientes en la última lectura (contador de la UI).</summary>
    public int Pendientes { get; private set; }

    /// <summary>Backups conservados por archivo de partida.</summary>
    public int MaxBackups { get; set; } = 5;

    /// <summary>Callback de progreso (por acción) para el log de la UI.</summary>
    public Action<string>? AlProgresar { get; set; }

    /// <summary>Pide elegir un Pokémon del .sav (equipo Y cajas). null = cancelado (queda pending).</summary>
    public Func<string, List<ObjetivoPkm>, UbicacionPkm?>? PedirObjetivo { get; set; }

    /// <summary>Pide un reparto de EVs (validado: ≤252 por stat, ≤510 total). null = cancelado.</summary>
    public Func<string, Dictionary<string, int>?>? PedirRepartoEvs { get; set; }

    /// <summary>Pide un movimiento (ID). null = cancelado.</summary>
    public Func<string, ushort, ushort?>? PedirMovimiento { get; set; }

    /// <summary>Sin hueco libre: elige qué movimiento (índice 0..3) reemplazar.</summary>
    public Func<string, IReadOnlyList<ushort>, int?>? PedirMovimientoReemplazo { get; set; }

    /// <summary>Elige uno de los movimientos indicados (p. ej. los 4 del Pokémon Mentor).</summary>
    public Func<string, IReadOnlyList<ushort>, ushort?>? PedirMovimientoDeLista { get; set; }
    /// <summary>Diálogo para elegir un objeto de una lista (p. ej. qué megapiedra).</summary>
    public Func<string, IReadOnlyList<(int Id, string Nombre)>, int?>? PedirObjetoDeLista { get; set; }

    /// <summary>Pide una habilidad (ID). null = cancelado.</summary>
    public Func<string, IReadOnlyList<(int Id, string Nombre)>, int?>? PedirHabilidad { get; set; }

    /// <summary>Pide una stat (hp|atk|def|spa|spd|spe). null = cancelado.</summary>
    public Func<string, string?>? PedirStat { get; set; }

    /// <summary>Pide dos stats (Zenkai). null = cancelado.</summary>
    public Func<string, List<string>?>? PedirDosStats { get; set; }

    /// <summary>Pide el nivel de un Pokémon del Gachapón. null = usar el nivel medio del equipo.</summary>
    public Func<string, int?>? PedirNivel { get; set; }

    /// <summary>Diálogo de texto para el mote del Pokémon del gachapón.</summary>
    public Func<string, string?>? PedirMote { get; set; }

    /// <summary>Editor de IVs/EVs: devuelve los 6 IVs (0..31) y los 6 EVs (0..252).</summary>
    public Func<string, PKM, int, (int[] Ivs, int[] Evs)?>? PedirEdicionStats { get; set; }

    /// <summary>
    /// Seam de test: abre un .sav. En producción (null) usa <see cref="SaveUtil.GetSaveFile(string)"/>.
    /// </summary>
    public Func<string, SaveFile>? Abridor { get; set; }

    /// <summary>
    /// Seam SOLO para tests con saves en blanco no exportables (gen 5/6/7): serializa el save
    /// a disco. En producción es null y se usa el pipeline real de PKHeX (<c>Write()</c>).
    /// </summary>
    public Func<SaveFile, string, byte[]>? Serializador { get; set; }



    public ColaLauncherService(IColaFirebase firebase, string datosDir, Func<bool> emuladorAbierto)
    {
        _firebase = firebase;
        _datosDir = datosDir;
        _emuladorAbierto = emuladorAbierto;
    }

    /// <summary>Cuenta las acciones pendientes sin procesarlas (lectura ligera para la UI).</summary>
    public async Task<int> ContarPendientesAsync(string lockeId, string playerTag, CancellationToken ct = default)
    {
        var cola = await _firebase.LeerColaAsync(lockeId, playerTag, ct);
        Pendientes = cola.Values.Count(EsProcesable);
        return Pendientes;
    }

    /// <summary>¿Debe procesarse? (pendiente, o applying con claim caducado >10 min).</summary>
    private static bool EsProcesable(AccionLauncher a)
        => a.Status == AccionEstado.Pendiente
           || (a.Status == AccionEstado.Aplicando && ClaimCaducado(a));

    private static bool ClaimCaducado(AccionLauncher a)
        => a.ClaimedAt is null || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - a.ClaimedAt.Value > CaducidadClaimMs;

    /// <summary>
    /// Procesa la cola completa. Devuelve los resultados por acción.
    /// </summary>
    public async Task<List<ResultadoAccion>> ProcesarColaAsync(
        string lockeId, string playerTag, string rutaSave, CancellationToken ct = default)
    {
        var resultados = new List<ResultadoAccion>();
        Diagnostico($"== Cola: locke={lockeId} tag={playerTag} save={rutaSave} existe={File.Exists(rutaSave)}");
        var cola = await _firebase.LeerColaAsync(lockeId, playerTag, ct);
        Pendientes = cola.Values.Count(EsProcesable);
        Diagnostico($"   leídas={cola.Count} procesables={Pendientes}");

        // Entradas ilegibles → failed con motivo (nunca se ocultan)
        // (los errores ya se registraron en LeerColaAsync vía Registro; aquí no tenemos las claves,
        //  así que el registro en Firebase de entradas ilegibles se hace desde la implementación real.)

        var aplicadasLocal = LeerAplicadasLocales();

        // Orden por createdAt (más antiguo primero); solo procesables
        var ordenadas = cola
            .Where(kv => EsProcesable(kv.Value))
            .OrderBy(kv => kv.Value.CreatedAt)
            .ToList();

        if (ordenadas.Count == 0)
            return resultados;

        if (_emuladorAbierto())
        {
            var aviso = $"Emulador abierto: {ordenadas.Count} acción(es) se aplicarán cuando lo cierres (o con «Aplicar pendientes»).";
            AlProgresar?.Invoke(aviso);
            return resultados;   // no tocar el .sav con el emulador corriendo
        }

        // Partida de Pokémon Añil (.rxdata): backend de Ruby Marshal, no PKHeX.
        if (EsPartidaAnil(rutaSave))
            return await ProcesarColaAnilAsync(lockeId, playerTag, rutaSave, ordenadas, aplicadasLocal, resultados, ct);

        var save = AbrirSave(rutaSave);
        var batch = new List<(string Id, ResultadoAccion Resultado)>();   // acciones con cambio pendientes de escribir
        var backupCreado = false;

        foreach (var (actionId, accion) in ordenadas)
        {
            if (ct.IsCancellationRequested) break;

            var claveLocal = ClaveLocal(actionId);
            if (aplicadasLocal.Contains(claveLocal))
            {
                await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId,
                    AccionEstado.Aplicada, "Ya aplicada anteriormente por este launcher.", null, ct);
                resultados.Add(new ResultadoAccion(actionId, AccionEstado.Aplicada, "Ya aplicada anteriormente."));
                continue;
            }

            // Validación de juego por acción (v1.1: conjuntos de versiones)
            if (!ValidarJuego(accion.GameName, save, rutaSave, out var msgJuego))
            {
                await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId,
                    AccionEstado.Fallida, msgJuego, msgJuego, ct);
                resultados.Add(new ResultadoAccion(actionId, AccionEstado.Fallida, msgJuego, msgJuego));
                continue;
            }

            // Claim condicional por acción (ETag del nodo individual, PUT completo)
            var reclamada = await _firebase.ReclamarAccionAsync(lockeId, playerTag, actionId, accion, ct);
            if (!reclamada)
            {
                AlProgresar?.Invoke($"⏭ {actionId[..Math.Min(8, actionId.Length)]}… la reclamó otro launcher.");
                continue;
            }

            AlProgresar?.Invoke($"Procesando {actionId[..Math.Min(8, actionId.Length)]}… ({accion.Type})");

            // Snapshot para rollback por acción: si la verificación in-memory falla,
            // se restaura el estado ANTES de esta acción (el resto del lote sigue limpio).
            var snapshot = save.Data.ToArray();

            ResultadoAccion resultado;
            try
            {
                resultado = await AplicarAccion(accion, save);
            }
            catch (Exception ex)
            {
                RestauraData(save, snapshot);
                resultado = new ResultadoAccion(actionId, AccionEstado.Fallida, "Error interno", ex.Message);
            }
            Diagnostico($"   [{actionId}] {accion.Type} card={accion.CardId} → {resultado.Estado}: {resultado.ResultMessage}" + (string.IsNullOrWhiteSpace(resultado.ErrorMessage) ? "" : " | error: " + resultado.ErrorMessage));

            switch (resultado.Estado)
            {
                case AccionEstado.Aplicada:
                {
                    // Verificación semántica INMEDIATA sobre el save en memoria (punto en el tiempo
                    // de ESTA acción: las acciones posteriores pueden acumular sobre el mismo Pokémon)
                    var motivoMemoria = resultado.Verificador?.Invoke(save);
                    if (motivoMemoria is not null)
                    {
                        RestauraData(save, snapshot);
                        resultado = new ResultadoAccion(actionId, AccionEstado.Fallida,
                            $"Verificación fallida en memoria ({motivoMemoria}); la acción no se aplicó.", motivoMemoria);
                        await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId,
                            AccionEstado.Fallida, resultado.ResultMessage, motivoMemoria, ct);
                        resultados.Add(resultado);
                        break;
                    }
                    resultado = resultado with { ActionId = actionId };
                    Diagnostico($"   [{actionId}] verificación en memoria OK → aplicada");
                    batch.Add((actionId, resultado));
                    resultados.Add(resultado);
                    break;
                }

                case AccionEstado.Omitida:
                    await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId,
                        AccionEstado.Omitida, resultado.ResultMessage, null, ct);
                    resultados.Add(resultado with { ActionId = actionId });
                    break;

                case AccionEstado.Pendiente:
                    // Cancelación de diálogo: NO es fallo; vuelve a pending y continúa el lote.
                    // El snapshot NO se restaura: una cancelación no toca el .sav (el diálogo
                    // se pide ANTES de editar), pero por seguridad se restaura igualmente.
                    RestauraData(save, snapshot);
                    await _firebase.DevolverAPendienteAsync(lockeId, playerTag, actionId,
                        resultado.ResultMessage, ct);
                    resultados.Add(resultado with { ActionId = actionId });
                    break;

                default: // Fallida
                    RestauraData(save, snapshot);
                    await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId,
                        AccionEstado.Fallida, resultado.ResultMessage, resultado.ErrorMessage, ct);
                    resultados.Add(resultado with { ActionId = actionId });
                    break;
            }
        }

        // Escribir UNA vez (con Write() → checksums válidos), releer de disco y verificar
        if (batch.Count > 0)
        {
            CrearBackup(rutaSave);
                PodarBackups(rutaSave);
            backupCreado = true;
            try
            {
                // Guardado con el pipeline real de PKHeX (Write(): checksums + metadata finalizada).
                if (Serializador is null)
                    InyectorEquipo.GuardarSave(save, rutaSave);
                else
                    File.WriteAllBytes(rutaSave, Serializador(save, rutaSave));

                // Re-lectura OBLIGATORIA desde disco (evidencia real) + validación de checksums
                var releido = AbrirSave(rutaSave);
                if (!releido.ChecksumsValid)
                    throw new InvalidOperationException(
                        $"El .sav re-leído tiene checksums inválidos ({releido.ChecksumInfo}); el juego lo trataría como dañado.");

                // Verificadores semánticos de cada efecto SOBRE EL SAVE RELEÍDO.
                // Si un verificador falla solo porque una acción POSTERIOR del lote cambió ese
                // mismo valor (acumulación legítima), se acepta: ya se validó al aplicarse.
                var fallosRelectura = new List<(string Id, string Motivo)>();
                foreach (var (id, res) in batch)
                {
                    if (res.Verificador is null) continue;
                    var motivoDisco = res.Verificador(releido);
                    if (motivoDisco is null) continue;
                    var motivoMemoria = res.Verificador(save);
                    if (motivoMemoria is null)
                        fallosRelectura.Add((id, motivoDisco));   // pasaba en memoria y falla en disco → no persistió
                    else
                        AlProgresar?.Invoke($"ℹ {id[..Math.Min(8, id.Length)]}: verificación directa superada por otra acción posterior del lote (acumulación).");
                }
                if (fallosRelectura.Count > 0)
                {
                    RestaurarBackup(rutaSave);
                    foreach (var (id, res) in batch)
                    {
                        var motivo = fallosRelectura.FirstOrDefault(f => f.Id == id).Motivo;
                        var msg = motivo is not null
                            ? $"Verificación fallida tras releer el .sav ({motivo}); se restauró la copia de seguridad."
                            : "Otra acción del lote falló la verificación tras releer; se restauró la copia de seguridad.";
                        await _firebase.MarcarResultadoAsync(lockeId, playerTag, id,
                            AccionEstado.Fallida, msg, motivo, ct);
                        var idx = resultados.FindIndex(r => r.ActionId == id && r.Estado == AccionEstado.Aplicada);
                        if (idx >= 0)
                            resultados[idx] = new ResultadoAccion(id, AccionEstado.Fallida, msg, motivo);
                    }
                    AlProgresar?.Invoke($"✗ Verificación fallida tras releer: backup restaurado ({fallosRelectura.Count} acción(es)).");
                    return resultados;
                }

                RegenerarHuella(rutaSave);
                foreach (var (id, res) in batch)
                {
                    RegistrarAplicadaLocal(ClaveLocal(id));   // SOLO tras guardar y verificar
                    await _firebase.MarcarResultadoAsync(lockeId, playerTag, id,
                        AccionEstado.Aplicada, res.ResultMessage, null, ct);
                }
                AlProgresar?.Invoke($"✔ {batch.Count} acción(es) aplicadas, verificadas releyendo el .sav (checksums válidos) y guardadas.");
            }
            catch (Exception ex)
            {
                if (backupCreado) RestaurarBackup(rutaSave);
                foreach (var (id, res) in batch)
                {
                    await _firebase.MarcarResultadoAsync(lockeId, playerTag, id,
                        AccionEstado.Fallida, "Error al guardar el .sav (restaurado)", ex.Message, ct);
                    var idx = resultados.FindIndex(r => r.ActionId == id && r.Estado == AccionEstado.Aplicada);
                    if (idx >= 0)
                        resultados[idx] = new ResultadoAccion(id, AccionEstado.Fallida,
                            "Error al guardar el .sav (restaurado)", ex.Message);
                }
                AlProgresar?.Invoke($"✗ Error al guardar: {ex.Message}. Backup restaurado.");
            }
        }

        Pendientes = Math.Max(0, Pendientes - batch.Count(r => r.Resultado.Estado == AccionEstado.Aplicada));
        return resultados;
    }

    // ─────────────────────────── aplicación individual (en memoria) ───────────────────────────

    private async Task<ResultadoAccion> AplicarAccion(AccionLauncher acc, SaveFile sav)
        => acc.Type switch
        {
            AccionTipo.CardEffect => await AplicarCardEffect(acc, sav),
            AccionTipo.GachaPokemon => AplicarGachaPokemon(acc, sav),
                AccionTipo.ItemGrant => await AplicarItemGrantAsync(acc, sav),
            AccionTipo.MintUse => await AplicarMintUse(acc, sav),
            _ => new ResultadoAccion("", AccionEstado.Omitida, $"Tipo desconocido: {acc.Type}"),
        };

    private async Task<ResultadoAccion> AplicarCardEffect(AccionLauncher acc, SaveFile sav)
    {
        var cardId = acc.CardId ?? 0;

        // Cartas fuera del catálogo → skipped sin tocar el .sav
        if (cardId is not (36 or 41 or 42 or 56 or 59 or 63 or 65 or 66 or 67 or 70 or 74 or 78 or 79 or 81 or 82 or 83 or 84 or 87 or 88))
            return new ResultadoAccion("", AccionEstado.Omitida, "Efecto aún no soportado por el launcher");

        var strings = GameInfo.GetStrings("es");

        switch (cardId)
        {
            case EfectosCartas.CapsulaHabilidadDefinitiva: // 63
            {
                var objetivos = ListarObjetivos(sav).Where(o => o.Pkm.Species > 0 && o.Pkm.Species <= sav.Personal.MaxSpeciesID).ToList();
                var ubi = PedirObjetivo?.Invoke("Cápsula Habilidad: elige el Pokémon", objetivos);
                if (ubi is null) return Cancelado();
                var objetivo = objetivos.First(o => o.Ubicacion == ubi);
                var pkm = objetivo.Pkm;

                // Solo las habilidades que la especie PUEDE tener en esta generación
                // (la lista global ofrecía habilidades de juegos posteriores que no se aplicaban).
                var legales = EfectosCartas.HabilidadesDisponibles(pkm, strings.Ability);
                if (legales.Count == 0) return DatoInvalido("no encuentro habilidades válidas para esta especie");
                var idHab = PedirHabilidad?.Invoke(
                    $"Habilidad para {EtiquetaCorta(pkm)} (actual: {strings.Ability[Math.Max(0, EfectosCartas.IdHabilidadActual(pkm))]})",
                    legales);
                if (idHab is null or <= 0) return Cancelado();
                EfectosCartas.PonerHabilidadLibre(pkm, idHab.Value);
                if (EfectosCartas.IdHabilidadActual(pkm) != idHab.Value)
                    return DatoInvalido(
                        $"esta generación solo puede guardar las habilidades propias de la especie " +
                        $"({string.Join(", ", EfectosCartas.HabilidadesLegales(pkm, strings.Ability).Select(h => h.Nombre))}); " +
                        $"«{strings.Ability[idHab.Value]}» no se puede asignar");
                EscribirDeVuelta(sav, ubi, pkm);
                var esperado = idHab.Value;
                return Ok($"{EtiquetaCorta(pkm)} ahora tiene {strings.Ability[esperado]}",
                    releido =>
                    {
                        var p = LeerEn(releido, ubi);
                        return p is null ? "el Pokémon ya no está en su ubicación"
                            : EfectosCartas.IdHabilidadActual(p) == esperado ? null
                            : $"la habilidad leída ({EfectosCartas.IdHabilidadActual(p)}) no es la esperada ({esperado})";
                    });
            }
            case EfectosCartas.MaquinaMt: // 65
            {
                var bag = sav.Inventory;   // UNA sola instancia
                EfectosCartas.DarTodasLasMt(bag);
                bag.CopyTo(sav);
                return Ok("MT desbloqueadas y al máximo en la mochila",
                    releido => releido.Inventory.Pouches
                        .Where(p => p.Type.ToString().Contains("TM", StringComparison.OrdinalIgnoreCase))
                        .Sum(p => p.Items.Count(i => i.Index > 0 && i.Count > 0)) > 0
                        ? null : "las MT no quedaron en la mochila tras releer");
            }
            case EfectosCartas.MtDivina: // 66
            {
                var objetivos = ListarObjetivos(sav).Where(o => o.Pkm.Species > 0 && o.Pkm.Species <= sav.Personal.MaxSpeciesID).ToList();
                var ubi = PedirObjetivo?.Invoke("MT Divina: elige el Pokémon", objetivos);
                if (ubi is null) return Cancelado();
                var objetivo = objetivos.First(o => o.Ubicacion == ubi);
                var pkm = objetivo.Pkm;

                // Solo movimientos que EXISTEN en esta generación (máx. del formato del save).
                var maxMov = pkm.MaxMoveID;
                var mov = PedirMovimiento?.Invoke($"MT Divina: movimiento (hasta #{maxMov})", maxMov);
                if (mov is null or 0) return Cancelado();
                if (mov.Value > maxMov)
                    return DatoInvalido($"el movimiento #{mov.Value} no existe en esta generación (máx. {maxMov})");

                // Hueco libre; si no lo hay, el jugador elige qué movimiento reemplazar.
                var indice = Array.IndexOf(pkm.Moves, (ushort)0);
                if (indice < 0)
                {
                    var elegido = PedirMovimientoReemplazo?.Invoke(
                        "Sin hueco libre: ¿qué movimiento reemplazar?",
                        pkm.Moves.ToList());
                    if (elegido is null or < 0 or > 3) return Cancelado();
                    indice = elegido.Value;
                }
                EfectosCartas.EnsenarMovimientoEn(pkm, indice, mov.Value);
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"{EtiquetaCorta(pkm)} aprendió {strings.Move[mov.Value]} (hueco {indice + 1})",
                    releido =>
                    {
                        var p = LeerEn(releido, ubi);
                        return p is null ? "el Pokémon ya no está en su ubicación"
                            : p.HasMove(mov.Value) ? null
                            : "el movimiento no quedó aprendido tras releer";
                    });
            }
            case EfectosCartas.BanoDinero: // 67
            {
                // La app puede mandar params.amount (p. ej. Ruleta: 50.000). Sin amount, +25.000.
                var cantidad = acc.ParamInt("amount") is int monto and > 0 ? monto : 25_000;
                var antes = (long)sav.Money;
                var esperado = (int)Math.Min((long)sav.MaxMoney, antes + cantidad);
                EfectosCartas.DarDinero(sav, esperado);
                return Ok($"Dinero: +{cantidad:N0} (total {esperado:N0})",
                    releido => releido.Money == esperado ? null : $"el dinero leído ({releido.Money}) no es el esperado ({esperado})");
            }
            case EfectosCartas.PeonDeObra: // 70
            {
                var (ubi, pkm, objetivo) = await PedirYBuscar(sav, "Peón de obra: elige el Pokémon");
                if (ubi is null) return Cancelado();
                var stat = acc.ParamStr("stat") ?? PedirStat?.Invoke($"Peón de obra: ¿qué stat (+128 EV)? ({objetivo.Etiqueta()})");
                if (stat is null) return Cancelado();
                if (!EfectosCartas.EsStatValida(stat)) return DatoInvalido($"stat «{stat}» no válida");
                EfectosCartas.AnadirEvs(pkm, stat, 128);
                var esperado = EfectosCartas.LeerEv(pkm, stat);   // efectivo tras el tope de 510
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"+128 EV en {stat.ToUpperInvariant()} ({objetivo.Etiqueta()}) (EV final: {esperado})",
                    VerificarEv(sav, ubi, stat, esperado));
            }
            case EfectosCartas.JefeDeCuadrilla: // 78
            {
                var (ubi, pkm, objetivo) = await PedirYBuscar(sav, "Jefe de cuadrilla: elige el Pokémon");
                if (ubi is null) return Cancelado();
                var stat = acc.ParamStr("stat") ?? PedirStat?.Invoke($"Jefe de cuadrilla: ¿qué stat (+252 EV)? ({objetivo.Etiqueta()})");
                if (stat is null) return Cancelado();
                if (!EfectosCartas.EsStatValida(stat)) return DatoInvalido($"stat «{stat}» no válida");
                EfectosCartas.MaxEvs(pkm, stat);
                var esperadoEv = EfectosCartas.LeerEv(pkm, stat);
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"EV {stat.ToUpperInvariant()} a {esperadoEv} ({objetivo.Etiqueta()})",
                    VerificarEv(sav, ubi, stat, esperadoEv));
            }

            case EfectosCartas.JefeDeObra: // 81
            {
                var (ubi, pkm, objetivo) = await PedirYBuscar(sav, "Jefe de obra: elige el Pokémon");
                if (ubi is null) return Cancelado();
                var reparto = PedirRepartoEvs?.Invoke("Jefe de obra: reparte hasta 510 EVs (máx. 252 por stat)");
                if (reparto is null) return Cancelado();
                if (!RepartoValido(reparto, out var msgReparto)) return DatoInvalido(msgReparto);
                EfectosCartas.AplicarRepartoEvs(pkm, reparto);
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"EVs repartidos en {objetivo.Etiqueta()}",
                    VerificarReparto(sav, ubi, reparto));
            }
            case EfectosCartas.MedicinaExperimental: // 74
            {
                var exito = acc.ParamBool("success") ?? true;
                var (ubi, pkm, objetivo) = await PedirYBuscar(sav, "Medicina experimental: elige el Pokémon");
                if (ubi is null) return Cancelado();
                Dictionary<string, int>? reparto = null;
                if (exito)
                {
                    reparto = PedirRepartoEvs?.Invoke("Medicina experimental: reparte los EVs");
                    if (reparto is null) return Cancelado();
                    if (!RepartoValido(reparto, out var msgR)) return DatoInvalido(msgR);
                }
                EfectosCartas.Medicina(pkm, exito, reparto);
                EscribirDeVuelta(sav, ubi, pkm);
                if (exito)
                    return Ok($"IVs a 31 y EVs aplicados en {objetivo.Etiqueta()}",
                        releido =>
                        {
                            var motivoReparto = VerificarReparto(sav, ubi, reparto!)(releido);
                            if (motivoReparto is not null) return motivoReparto;
                            var p2 = LeerEn(releido, ubi);
                            return p2 is { } q && q.IV_HP == 31 ? null : "los IVs no quedaron a 31";
                        });
                return Ok($"IVs y EVs a 0 en {objetivo.Etiqueta()} (efecto fallido)",
                    VerificarReparto(sav, ubi, new Dictionary<string, int>
                    {
                        ["hp"] = 0, ["atk"] = 0, ["def"] = 0, ["spa"] = 0, ["spd"] = 0, ["spe"] = 0,
                    }));
            }
            case EfectosCartas.Mentor: // 79
            {
                var objetivos = ListarObjetivos(sav).Where(o => o.Pkm.Species > 0 && o.Pkm.Species <= sav.Personal.MaxSpeciesID).ToList();
                var ubiOrigen = PedirObjetivo?.Invoke("Mentor: elige el Pokémon ORIGEN", objetivos);
                if (ubiOrigen is null) return Cancelado();
                var ubiDestino = PedirObjetivo?.Invoke("Mentor: elige el Pokémon DESTINO", objetivos);
                if (ubiDestino is null) return Cancelado();
                if (ubiOrigen == ubiDestino) return DatoInvalido("origen y destino no pueden ser el mismo");
                var origen = objetivos.First(o => o.Ubicacion == ubiOrigen).Pkm;
                var destino = objetivos.First(o => o.Ubicacion == ubiDestino).Pkm;

                var conocidos = origen.Moves.Where(m => m > 0).Distinct().ToList();
                if (conocidos.Count == 0) return DatoInvalido("el Pokémon origen no conoce movimientos");
                var mov = PedirMovimientoDeLista?.Invoke(
                    $"Mentor: elige el movimiento de {EtiquetaCorta(origen)}", conocidos);
                if (mov is null or 0) return Cancelado();
                if (!conocidos.Contains(mov.Value)) return DatoInvalido("el origen no conoce ese movimiento");

                var indice = Array.IndexOf(destino.Moves, (ushort)0);
                if (indice < 0)
                {
                    var elegido = PedirMovimientoReemplazo?.Invoke(
                        "Sin hueco libre: ¿qué movimiento reemplazar?", destino.Moves.ToList());
                    if (elegido is null or < 0 or > 3) return Cancelado();
                    indice = elegido.Value;
                }
                EfectosCartas.EnsenarMovimientoEn(destino, indice, mov.Value);
                EscribirDeVuelta(sav, ubiDestino, destino);
                return Ok($"Movimiento {strings.Move[mov.Value]} copiado a {EtiquetaCorta(destino)}",
                    releido =>
                    {
                        var p = LeerEn(releido, ubiDestino);
                        return p is null ? "el destino ya no está en su ubicación"
                            : p.HasMove(mov.Value) ? null
                            : "el movimiento no quedó copiado tras releer";
                    });
            }
            case EfectosCartas.ModificacionGenetica: // 82
            {
                var objetivos = ListarObjetivos(sav).Where(o => o.Pkm.Species > 0 && o.Pkm.Species <= sav.Personal.MaxSpeciesID).ToList();
                var ubi1 = PedirObjetivo?.Invoke("Modificación genética: elige el PRIMER Pokémon", objetivos);
                if (ubi1 is null) return Cancelado();
                var p1 = objetivos.First(o => o.Ubicacion == ubi1).Pkm;
                var ubi2 = PedirObjetivo?.Invoke(
                    $"Modificación genética: elige el SEGUNDO Pokémon (el primero tiene {strings.Ability[Math.Max(0, EfectosCartas.IdHabilidadActual(p1))]})",
                    objetivos);
                if (ubi2 is null) return Cancelado();
                if (ubi1 == ubi2) return DatoInvalido("deben ser dos Pokémon distintos");
                var p2 = objetivos.First(o => o.Ubicacion == ubi2).Pkm;

                var hab1 = EfectosCartas.IdHabilidadActual(p1);
                var hab2 = EfectosCartas.IdHabilidadActual(p2);
                Diagnostico($"82: antes1={hab1} antes2={hab2} (byte 0x15)");
                var nombre1 = hab1 >= 0 && hab1 < strings.Ability.Count ? strings.Ability[hab1] : "?";
                var nombre2 = hab2 >= 0 && hab2 < strings.Ability.Count ? strings.Ability[hab2] : "?";

                // 1) guardar los Pokémon tal cual están en su hueco
                EscribirDeVuelta(sav, ubi1, p1);
                EscribirDeVuelta(sav, ubi2, p2);

                // 2) releer DEL SAVE el objeto ya colocado, escribir el byte crudo y volver a
                //    guardar (PKHeX normaliza la habilidad al meter el PKM en el save; así se
                //    escribe después de esa normalización, igual que hace PKHeX Hax).
                var q1 = LeerEn(sav, ubi1);
                var q2 = LeerEn(sav, ubi2);
                var ok1 = q1 is not null && EfectosCartas.PonerHabilidadLibre(q1, hab2);
                var ok2 = q2 is not null && EfectosCartas.PonerHabilidadLibre(q2, hab1);
                Diagnostico($"82: escritura1={ok1} escritura2={ok2} " +
                            $"byte1={(q1 is null ? -1 : EfectosCartas.IdHabilidadActual(q1))} " +
                            $"byte2={(q2 is null ? -1 : EfectosCartas.IdHabilidadActual(q2))}");
                if (q1 is not null) EscribirDeVuelta(sav, ubi1, q1);
                if (q2 is not null) EscribirDeVuelta(sav, ubi2, q2);
                var tras1 = LeerEn(sav, ubi1);
                var tras2 = LeerEn(sav, ubi2);
                Diagnostico($"82: tras guardar 1={(tras1 is null ? -1 : EfectosCartas.IdHabilidadActual(tras1))} 2={(tras2 is null ? -1 : EfectosCartas.IdHabilidadActual(tras2))}");
                return Ok($"Habilidades intercambiadas: {EtiquetaCorta(p1)} → {nombre2}, {EtiquetaCorta(p2)} → {nombre1}",
                    releido =>
                    {
                        var a = LeerEn(releido, ubi1);
                        var b = LeerEn(releido, ubi2);
                        if (a is null || b is null) return "alguno de los Pokémon ya no está en su ubicación";
                        var ahora1 = EfectosCartas.IdHabilidadActual(a);
                        var ahora2 = EfectosCartas.IdHabilidadActual(b);
                        if (hab2 > 0 && ahora1 != hab2) return $"la habilidad del primero quedó en {ahora1} (esperada {hab2})";
                        if (hab1 > 0 && ahora2 != hab1) return $"la habilidad del segundo quedó en {ahora2} (esperada {hab1})";
                        if (hab2 <= 0 && ahora1 != 0) return "el primero no tenía habilidad y se le asignó una";
                        if (hab1 <= 0 && ahora2 != 0) return "el segundo no tenía habilidad y se le asignó una";
                        return null;
                    });
            }
            case EfectosCartas.Zenkai: // 83
            {
                var (ubi, pkm, objetivo) = await PedirYBuscar(sav, "Zenkai: elige el Pokémon");
                if (ubi is null) return Cancelado();

                // Propuesta inicial: IV 31 y 252 EV en las stats de params.stats (o atk/spe).
                var preset = acc.ParamStrList("stats").Where(EfectosCartas.EsStatValida).Distinct().Take(2).ToList();
                if (preset.Count < 2) preset = ["atk", "spe"];
                var ivs = Enumerable.Repeat(0, 6).ToArray();
                var evs = Enumerable.Repeat(0, 6).ToArray();
                var orden = new[] { "hp", "atk", "def", "spa", "spd", "spe" };
                for (var i = 0; i < orden.Length; i++)
                {
                    ivs[i] = 31;
                    evs[i] = preset.Contains(orden[i]) ? 252 : 0;
                }

                var editado = PedirEdicionStats?.Invoke("Zenkai: ajusta IVs y EVs (máx. 252 EVs en total)", pkm, 252);
                if (editado is null) return Cancelado();
                EfectosCartas.Revivir(pkm);
                EfectosCartas.PonerIvsYevsLibre(pkm, editado.Value.Ivs, editado.Value.Evs);
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"Zenkai en {objetivo.Etiqueta()}: IVs y EVs aplicados",
                    releido =>
                    {
                        var p = LeerEn(releido, ubi);
                        if (p is null) return "el Pokémon ya no está en su ubicación";
                        for (var i = 0; i < orden.Length; i++)
                        {
                            if (EfectosCartas.LeerIv(p, orden[i]) != editado.Value.Ivs[i])
                                return $"el IV de {orden[i]} no quedó aplicado";
                            if (EfectosCartas.LeerEv(p, orden[i]) != editado.Value.Evs[i])
                                return $"el EV de {orden[i]} no quedó aplicado";
                        }
                        if (ubi.EsEquipo && p.Stat_HPCurrent != p.Stat_HPMax) return "el HP no quedó al máximo";
                        return null;
                    });
            }
            case EfectosCartas.EjemploASeguir: // 84
            {
                var (ubi, pkm, objetivo) = await PedirYBuscar(sav, "Ejemplo a seguir: elige el Pokémon");
                if (ubi is null) return Cancelado();

                var ivs = Enumerable.Repeat(31, 6).ToArray();
                var evs = new[] { 4, 252, 0, 0, 0, 252 };   // propuesta: 252 atk / 252 spe / 4 hp
                var editado = PedirEdicionStats?.Invoke("Ejemplo a seguir: ajusta IVs y EVs", pkm, 510);
                if (editado is null) return Cancelado();

                EfectosCartas.PonerIvsYevsLibre(pkm, editado.Value.Ivs, editado.Value.Evs);
                EscribirDeVuelta(sav, ubi, pkm);
                var orden = new[] { "hp", "atk", "def", "spa", "spd", "spe" };
                return Ok($"IVs y EVs aplicados en {objetivo.Etiqueta()}",
                    releido =>
                    {
                        var p = LeerEn(releido, ubi);
                        if (p is null) return "el Pokémon ya no está en su ubicación";
                        for (var i = 0; i < orden.Length; i++)
                        {
                            if (EfectosCartas.LeerIv(p, orden[i]) != editado.Value.Ivs[i])
                                return $"el IV de {orden[i]} no quedó aplicado";
                            if (EfectosCartas.LeerEv(p, orden[i]) != editado.Value.Evs[i])
                                return $"el EV de {orden[i]} no quedó aplicado";
                        }
                        return null;
                    });
            }
            case 36: // Glow up: equipo del jugador, IV 31 en TODO + shiny
            {
                var objetivos = ListarObjetivos(sav).Where(o => o.Pkm.Species > 0 && o.Pkm.Species <= sav.Personal.MaxSpeciesID).ToList();
                if (objetivos.Count == 0)
                    return new ResultadoAccion("", AccionEstado.Omitida, "Glow up: no hay Pokémon en el equipo");
                var ubi = PedirObjetivo?.Invoke("Glow up: elige el Pokémon (equipo o caja)", objetivos);
                if (ubi is null) return Cancelado();
                var objetivo = objetivos.First(o => o.Ubicacion == ubi);
                var pkm = objetivo.Pkm;
                EfectosCartas.PonerTodosIvs31(pkm);
                EfectosCartas.PonerShiny(pkm);
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"Glow up: IV 31 en las 6 stats y shiny en {objetivo.Etiqueta()}",
                    releido =>
                    {
                        var p = LeerEn(releido, ubi);
                        if (p is null) return "el Pokémon ya no está en su ubicación";
                        foreach (var st in new[] { "hp", "atk", "def", "spa", "spd", "spe" })
                            if (EfectosCartas.LeerIv(p, st) != 31) return $"el IV de {st} no quedó a 31";
                        return p.IsShiny ? null : "el Pokémon no quedó shiny";
                    });
            }
            case 56: // Cambio color: conmuta shiny en un Pokémon del equipo
            {
                var objetivos = ListarObjetivos(sav).Where(o => o.Pkm.Species > 0 && o.Pkm.Species <= sav.Personal.MaxSpeciesID).ToList();
                if (objetivos.Count == 0)
                    return new ResultadoAccion("", AccionEstado.Omitida, "Cambio color: no hay Pokémon en el equipo");
                var ubi = PedirObjetivo?.Invoke("Cambio color: elige el Pokémon (equipo o caja)", objetivos);
                if (ubi is null) return Cancelado();
                var objetivo = objetivos.First(o => o.Ubicacion == ubi);
                var pkm = objetivo.Pkm;
                var eraShiny = pkm.IsShiny;
                if (eraShiny)
                {
                    if (!EfectosCartas.QuitarShiny(pkm))
                        return new ResultadoAccion("", AccionEstado.Fallida, "No se pudo quitar el shiny (reintenta).");
                }
                else
                {
                    EfectosCartas.PonerShiny(pkm);
                }
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"Cambio color: {objetivo.Etiqueta()} ahora {(eraShiny ? "NO es shiny" : "es shiny")}",
                    releido =>
                    {
                        var p = LeerEn(releido, ubi);
                        if (p is null) return "el Pokémon ya no está en su ubicación";
                        return p.IsShiny == !eraShiny ? null : "el estado shiny no cambió al releer";
                    });
            }
            case 59: // Muda de piel: shiny debilitado del equipo → revivir y quitar shiny
            {
                // Un Pokémon de caja no tiene HP (no está debilitado): se ofrece cualquier shiny;
                // del equipo solo los shiny debilitados (HP 0).
                var objetivos = ListarObjetivos(sav)
                    .Where(o => o.Pkm.IsShiny && (!o.Ubicacion.EsEquipo || o.Pkm.Stat_HPCurrent <= 0))
                    .ToList();
                if (objetivos.Count == 0)
                    return new ResultadoAccion("", AccionEstado.Omitida,
                        "Muda de piel: no hay ningún Pokémon shiny debilitado en el equipo ni shiny en las cajas");
                var ubi = PedirObjetivo?.Invoke("Muda de piel: elige el Pokémon shiny (debilitado en el equipo o en caja)", objetivos);
                if (ubi is null) return Cancelado();
                var objetivo = objetivos.First(o => o.Ubicacion == ubi);
                var pkm = objetivo.Pkm;
                var enEquipo = ubi.EsEquipo;
                var avisos = enEquipo ? EfectosCartas.Revivir(pkm) : "";
                if (!EfectosCartas.QuitarShiny(pkm))
                    return new ResultadoAccion("", AccionEstado.Fallida, "No se pudo quitar el shiny (reintenta).");
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"Muda de piel: {objetivo.Etiqueta()} " +
                          (enEquipo ? "revivido y sin shiny" : "sin shiny (estaba en una caja: no había que revivir)") +
                          (string.IsNullOrWhiteSpace(avisos) ? "" : $" ({avisos})"),
                    releido =>
                    {
                        var p = LeerEn(releido, ubi);
                        if (p is null) return "el Pokémon ya no está en su ubicación";
                        if (p.IsShiny) return "el Pokémon sigue siendo shiny";
                        if (!ubi.EsEquipo) return null;   // en caja no hay HP que comprobar
                        return p.Stat_HPCurrent > 0 ? null : "el Pokémon no quedó curado";
                    });
            }            case 41: // Entreno diario (1 stat a IV 31)
            {
                var (ubi, pkm, objetivo) = await PedirYBuscar(sav, "Entreno diario: elige el Pokémon");
                if (ubi is null) return Cancelado();
                var stat = acc.ParamStr("stat") ?? PedirStat?.Invoke($"Entreno diario: ¿qué stat a IV 31? ({objetivo.Etiqueta()})");
                if (stat is null) return Cancelado();
                if (!EfectosCartas.EsStatValida(stat)) return DatoInvalido($"stat «{stat}» no válida");
                EfectosCartas.PonerIv31(pkm, stat);
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"IV de {stat.ToUpperInvariant()} a 31 en {objetivo.Etiqueta()}",
                    VerificarIv(sav, ubi, stat, 31));
            }
            case 42: // Entreno intenso (2 stats a IV 31)
            {
                var (ubi, pkm, objetivo) = await PedirYBuscar(sav, "Entreno intenso: elige el Pokémon");
                if (ubi is null) return Cancelado();
                var stats = acc.ParamStrList("stats").Where(EfectosCartas.EsStatValida).Distinct().Take(2).ToList();
                if (stats.Count < 2)
                {
                    var s1 = PedirStat?.Invoke($"Entreno intenso: primera stat a IV 31 ({objetivo.Etiqueta()})");
                    if (s1 is null) return Cancelado();
                    var s2 = PedirStat?.Invoke("Entreno intenso: segunda stat a IV 31");
                    if (s2 is null) return Cancelado();
                    stats = [s1, s2];
                }
                if (stats.Count < 2) return DatoInvalido("necesita 2 stats válidas");
                EfectosCartas.PonerIv31(pkm, stats[0]);
                EfectosCartas.PonerIv31(pkm, stats[1]);
                EscribirDeVuelta(sav, ubi, pkm);
                return Ok($"IVs de {stats[0].ToUpperInvariant()} y {stats[1].ToUpperInvariant()} a 31 en {objetivo.Etiqueta()}",
                    releido => VerificarIv(sav, ubi, stats[0], 31)(releido)
                            ?? VerificarIv(sav, ubi, stats[1], 31)(releido));
            }
            case EfectosCartas.SabotajeDoble:   // 87 (v1.1.b): IV 0 en 2 stats del Pokémon del rival
            case EfectosCartas.SabotajeCuadruple: // 88: IV 0 en 4 stats
            {
                var esperadas = cardId == EfectosCartas.SabotajeDoble ? 2 : 4;
                var stats = acc.ParamStrList("stats").Where(EfectosCartas.EsStatValida).Distinct().Take(esperadas).ToList();
                if (stats.Count < esperadas)
                    return DatoInvalido($"la carta necesita {esperadas} stats válidas (llegaron {stats.Count})");

                // Localizar el Pokémon objetivo por especie (y mote opcional) en equipo+cajas
                var speciesId = acc.ParamInt("targetSpeciesId") ?? 0;
                var nickname = acc.ParamStr("targetNickname");
                if (speciesId is < 1 or > 1025)
                    return DatoInvalido($"targetSpeciesId inválido ({speciesId})");
                var coincidencias = ListarObjetivos(sav)
                    .Where(o => o.Pkm.Species == speciesId)
                    .Where(o => string.IsNullOrWhiteSpace(nickname)
                                || string.Equals(o.Pkm.Nickname, nickname, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (coincidencias.Count == 0)
                {
                    // 0 coincidencias → SIGUE pending (el jugador puede conseguirlo más tarde).
                    // Se cuenta el intento: tras 3 avisos en el log no se insiste con diálogos.
                    var intentos = RegistrarIntentoObjetivo(acc, speciesId);
                    if (intentos >= 3)
                        AlProgresar?.Invoke(
                            $"⚠ La carta {cardId} no encuentra al Pokémon #{speciesId}" +
                            (string.IsNullOrWhiteSpace(nickname) ? "" : $" «{nickname}»") +
                            $" tras {intentos} intentos: revisa que esté en el equipo o en una caja de esta partida.");
                    return new ResultadoAccion("", AccionEstado.Pendiente,
                        "No encuentro el Pokémon objetivo");
                }
                UbicacionPkm ubi;
                if (coincidencias.Count == 1)
                {
                    ubi = coincidencias[0].Ubicacion;
                }
                else
                {
                    var elegida = PedirObjetivo?.Invoke(
                        $"Carta {cardId}: hay {coincidencias.Count} Pokémon de esa especie — elige el objetivo", coincidencias);
                    if (elegida is null) return Cancelado();
                    ubi = elegida;
                }
                var pkm = LeerEn(sav, ubi);
                if (pkm is null) return DatoInvalido("el objetivo desapareció al elegirlo");
                foreach (var s in stats)
                    PonerIvCero(pkm, s);
                EscribirDeVuelta(sav, ubi, pkm);
                // La app manda el mismo tag en createdBy y params.sourcePlayer (confirmado por Gemini).
                var atacante = acc.ParamStr("sourcePlayer") ?? acc.CreatedBy;
                if (string.IsNullOrWhiteSpace(atacante)) atacante = "otro jugador";
                return Ok($"Carta de {atacante}: IV a 0 en {string.Join("/", stats)} de {EtiquetaCorta(pkm)}",
                    VerificarIvsCero(sav, ubi, stats));
            }
            default:
                return new ResultadoAccion("", AccionEstado.Omitida, "Efecto aún no soportado por el launcher");
        }

        static void PonerIvCero(PKM pkm, string stat)
        {
            switch (stat.ToLowerInvariant())
            {
                case "hp": pkm.IV_HP = 0; break;
                case "atk": pkm.IV_ATK = 0; break;
                case "def": pkm.IV_DEF = 0; break;
                case "spa": pkm.IV_SPA = 0; break;
                case "spd": pkm.IV_SPD = 0; break;
                case "spe": pkm.IV_SPE = 0; break;
            }
        }
    }

    /// <summary>Diálogo + localización de un objetivo por PID (devuelve ubicación, PKM y objetivo).</summary>
    private async Task<(UbicacionPkm?, PKM, ObjetivoPkm)> PedirYBuscar(SaveFile sav, string titulo)
    {
        if (PedirObjetivo is null)
            return (null, null!, null!);
        var objetivos = ListarObjetivos(sav).Where(o => o.Pkm.Species > 0 && o.Pkm.Species <= sav.Personal.MaxSpeciesID).ToList();
        var ubi = PedirObjetivo(titulo, objetivos);
        if (ubi is null) return (null, null!, null!);
        var objetivo = objetivos.First(o => o.Ubicacion == ubi);
        // Recargar el PKM DESDE el save por ubicación: editar la instancia viva del save, no una copia
        var pkm = LeerEn(sav, ubi);
        return (ubi, pkm!, objetivo);
    }

    private ResultadoAccion AplicarGachaPokemon(AccionLauncher acc, SaveFile sav)
    {
        var speciesId = acc.SpeciesId ?? 0;
        var speciesName = acc.SpeciesName ?? $"#{speciesId}";
        if (speciesId is < 1 or > 1025)
            return new ResultadoAccion("", AccionEstado.Fallida, $"SpeciesId {speciesId} inválido.");

        // Lista cerrada acordada con la app (Gemini): POKEBALL, SUPERBALL, LUJOBALL, ULTRABALL, MASTERBALL.
        // Cualquier otro valor cae a Poké Ball, pero se avisa en resultMessage (no degradar en silencio).
        var bolaRecibida = acc.Ball ?? "POKEBALL";
        var ball = EfectosCartas.BolaDe(bolaRecibida, out var bolaDesconocida);

        // Regla del gachapón: SIEMPRE nivel 1; el mote lo elige el jugador (params.nickname o diálogo).
        var mote = acc.ParamStr("nickname");
        if (string.IsNullOrWhiteSpace(mote))
            mote = PedirMote?.Invoke($"Mote para {speciesName} (vacío = nombre de la especie)");


        PKM pkm;
        string? nota;
        try
        {
            (pkm, nota) = InyectorEquipo.ConstruirPkmGacha(sav, speciesId, acc.IsShiny ?? false, ball, mote);
        }
        catch (InvalidOperationException ex)
        {
            return new ResultadoAccion("", AccionEstado.Fallida, ex.Message, ex.Message);   // especie no disponible
        }

        // Colocar en el primer hueco libre de equipo; si no, de caja (vía slots del save)
        var huecoEquipo = Enumerable.Range(0, 6)
            .FirstOrDefault(i => sav.GetPartySlotAtIndex(i).Species == 0, -1);
        UbicacionPkm ubicacion;
        if (huecoEquipo >= 0)
        {
            sav.SetPartySlotAtIndex(pkm, huecoEquipo);
            ubicacion = UbicacionPkm.EnEquipo(huecoEquipo);
        }
        else
        {
            var (caja, slot) = BuscarHuecoCaja(sav);
            if (caja < 0)
                return new ResultadoAccion("", AccionEstado.Fallida, "No hay huecos libres en equipo ni cajas.");
            sav.SetBoxSlotAtIndex(pkm, caja, slot);
            ubicacion = UbicacionPkm.EnCaja(caja, slot);
        }

        // Registro en la Pokédex (visto + capturado)
        var msgDex = RegistrarDex(sav, pkm);

        // Aviso de legality (informativo, no bloquea)
        var la = new LegalityAnalysis(pkm);
        if (!la.Valid)
            nota = string.IsNullOrEmpty(nota) ? "aviso: el Pokémon puede ser señalado como ilegal por PKHeX (met location)" : nota + "; aviso legality";

        if (bolaDesconocida)
            nota = (nota is null ? "" : nota + "; ") + $"bola «{bolaRecibida}» no reconocida: se usó Poké Ball";

        var donde = ubicacion.EsEquipo ? $"equipo {ubicacion.PartyIndex + 1}" : $"caja {ubicacion.Box + 1} · hueco {ubicacion.Slot + 1}";
        var msg = $"{speciesName} Nv1 añadido en {donde} ({(acc.IsShiny == true ? "✨ " : "")}{acc.Ball ?? "POKEBALL"}){(nota is null ? "" : $" — {nota}")}{msgDex}";
        return Ok(msg, releido =>
        {
            var p = LeerEn(releido, ubicacion);
            if (p is null) return "el Pokémon no quedó en su ubicación tras releer";
            if (p.Species != speciesId) return $"la especie leída ({p.Species}) no es la esperada ({speciesId})";
            // En caja Stat_Level se lee 0 (es un campo de equipo); el nivel real está en CurrentLevel/EXP
            var nivelLeido = p.Stat_Level > 0 ? p.Stat_Level : p.CurrentLevel;
            if (nivelLeido != 1) return $"el nivel leído ({nivelLeido}) no es 1 (regla del gachapón)";
            if ((Ball)p.Ball != ball && ball != Ball.Poke) return $"la bola leída ({(Ball)p.Ball}) no es la esperada ({ball})";
            if (p.IsShiny != (acc.IsShiny ?? false)) return "el shiny no coincide";
            // Las stats de EQUIPO solo existen en slots de equipo; en caja se recalculan al entrar
            if (ubicacion.EsEquipo && p.Stat_HPMax <= 0) return "el HP máximo quedó a 0 (stats sin calcular)";
            return null;
        });
    }

    private async Task<ResultadoAccion> AplicarItemGrantAsync(AccionLauncher acc, SaveFile sav)
    {
        var itemName = acc.ItemName ?? "";
        var cantidad = Math.Clamp(acc.Quantity ?? 1, 1, 999);
        if (string.IsNullOrWhiteSpace(itemName))
            return new ResultadoAccion("", AccionEstado.Fallida, "ITEM_GRANT sin itemName.");

        var itemId = MapearObjeto(itemName);

        // «Cápsula Habilidad»: si el objeto no existe (o la mochila no lo admite), se elige un
        // Pokémon del equipo/cajas y se le cambia la habilidad por otra disponible del juego.
        if (EsCapsulaHabilidad(itemName) && !ObjetoAdmisible(sav, itemId))
        {
            var (ubiCapsula, pkmCapsula, objetivoCapsula) = await PedirYBuscar(sav, "Cápsula Habilidad: elige el Pokémon");
            if (ubiCapsula is null) return Cancelado();

            var nombresHabilidad = GameInfo.GetStrings("es").Ability;
            var opcionesHabilidad = EfectosCartas.HabilidadesDisponibles(pkmCapsula, nombresHabilidad);
            if (opcionesHabilidad.Count == 0)
                return DatoInvalido("No hay habilidades disponibles en esta partida.");
            var habilidadActual = IdHabilidadActual(pkmCapsula);
            // Gen 3/4/5: la cápsula no existe como objeto → habilidad ALEATORIA (no se pregunta).
            var candidatas = opcionesHabilidad.Where(o => o.Id != habilidadActual).ToList();
            if (candidatas.Count == 0) candidatas = opcionesHabilidad;
            var elegidaHabilidad = (int?)candidatas[Random.Shared.Next(candidatas.Count)].Id;
            if (elegidaHabilidad.Value == habilidadActual)
                return Ok("Solo hay una habilidad posible: sin cambios", _ => null);
            if (!EfectosCartas.PonerHabilidadLibre(pkmCapsula, elegidaHabilidad.Value))
                return DatoInvalido("No se pudo cambiar la habilidad (el formato no lo permite).");
            EscribirDeVuelta(sav, ubiCapsula, pkmCapsula);
            var nombreNuevo = elegidaHabilidad.Value < nombresHabilidad.Count ? nombresHabilidad[elegidaHabilidad.Value] : "#" + elegidaHabilidad;
            return Ok($"{objetivoCapsula.Etiqueta()}: habilidad → {nombreNuevo}",
                releido =>
                {
                    var p = LeerEn(releido, ubiCapsula);
                    if (p is null) return "el Pokémon ya no está en su ubicación";
                    var leida = IdHabilidadActual(p);
                    return leida == elegidaHabilidad.Value ? null
                        : $"la habilidad leída ({leida}) no es la esperada ({elegidaHabilidad})";
                });
        }
            // «Megapiedra» no existe como objeto genérico: hay que elegir la de un Pokémon.
            if (itemId is null && EsMegapiedra(itemName))
            {
                var megas = MegapiedrasDisponibles(sav);
                if (megas.Count == 0)
                    return DatoInvalido("Esta partida no tiene megapiedras disponibles.");
                var elegida = PedirObjetoDeLista?.Invoke("¿Qué megapiedra quieres?", megas);
                if (elegida is null or <= 0) return Cancelado();
                itemId = elegida;
                itemName = megas.FirstOrDefault(o => o.Id == elegida).Nombre ?? itemName;
            }
        if (itemId is null)
            return new ResultadoAccion("", AccionEstado.Fallida,
                $"Objeto «{itemName}» no encontrado en el catálogo de {acc.GameName}.");

        // UNA sola instancia de la mochila (CopyTo al final)
        var bag = sav.Inventory;
        InventoryPouch? pouchDestino = null;
        InventoryType tipoDestino = default;
        foreach (var tipo in Enum.GetValues<InventoryType>())
        {
            if (tipo is InventoryType.None or InventoryType.FreeSpace) continue;
            try
            {
                var permitidos = bag.Info.GetItems(tipo);
                if (!permitidos.Contains((ushort)itemId.Value)) continue;
                var pouch = bag.GetPouch(tipo);
                if (pouch is null) continue;
                pouchDestino = pouch;
                tipoDestino = tipo;
                break;
            }
            catch { }
        }
        if (pouchDestino is null)
            return new ResultadoAccion("", AccionEstado.Fallida,
                $"El objeto «{itemName}» no tiene bolsillo en {acc.GameName}.");

        if (!bag.Info.IsLegal(tipoDestino, itemId.Value, cantidad))
            return new ResultadoAccion("", AccionEstado.Fallida,
                $"Cantidad {cantidad} no legal para «{itemName}» en ese bolsillo.");

        var dados = pouchDestino.GiveItem(bag, (ushort)itemId.Value, cantidad);
        if (dados <= 0)
            return new ResultadoAccion("", AccionEstado.Fallida,
                $"Mochila llena: no hay hueco libre ni stack para «{itemName}».");
        bag.CopyTo(sav);

        return Ok($"{itemName} ×{cantidad} añadido a la mochila",
            releido =>
            {
                var pouch = releido.Inventory.GetPouch(tipoDestino);
                var slot = pouch?.Items.FirstOrDefault(i => i.Index == itemId.Value);
                if (slot is null || slot.Count < Math.Min(dados, cantidad))
                    return $"el objeto no quedó en la mochila tras releer (count={slot?.Count ?? 0})";
                return null;
            });
    }

    private async Task<ResultadoAccion> AplicarMintUse(AccionLauncher acc, SaveFile sav)
    {
        var naturalezaStr = acc.Nature ?? "";
        if (!Enum.TryParse(naturalezaStr, ignoreCase: true, out Nature naturaleza) || (int)naturaleza >= 25)
            return new ResultadoAccion("", AccionEstado.Fallida, $"Naturaleza «{naturalezaStr}» no reconocida.");

        var (ubi, pkm, _) = await PedirYBuscar(sav, $"Menta {naturalezaStr}: elige el Pokémon");
        if (ubi is null) return Cancelado();
        EfectosCartas.DarMenta(pkm, naturaleza);
        EscribirDeVuelta(sav, ubi, pkm);
        return Ok($"Naturaleza cambiada a {naturaleza}",
            releido =>
            {
                var p = LeerEn(releido, ubi);
                return p is null ? "el Pokémon ya no está en su ubicación"
                    : p.Nature == naturaleza ? null
                    : $"la naturaleza leída ({p.Nature}) no es la esperada ({naturaleza})";
            });
    }

    // ─────────────────────────── helpers de objetivos/escritura ───────────────────────────

    /// <summary>Lista TODO el contenido del .sav: equipo (hasta 6) + todas las cajas.</summary>
    private static List<ObjetivoPkm> ListarObjetivos(SaveFile sav)
    {
        var lista = new List<ObjetivoPkm>();
        for (var i = 0; i < 6; i++)
        {
            var p = sav.GetPartySlotAtIndex(i);
            if (p.Species > 0)
                lista.Add(new ObjetivoPkm(UbicacionPkm.EnEquipo(i), p));
        }
        for (var b = 0; b < sav.BoxCount; b++)
        {
            var datos = sav.GetBoxData(b);
            for (var s = 0; s < datos.Length; s++)
                if (datos[s].Species > 0)
                    lista.Add(new ObjetivoPkm(UbicacionPkm.EnCaja(b, s), datos[s]));
        }
        return lista;
    }

    /// <summary>Lee el PKM VIVO del save en esa ubicación (para editar y escribir de vuelta).</summary>
    private static PKM? LeerEn(SaveFile sav, UbicacionPkm ubi)
        => ubi.EsEquipo
            ? (ubi.PartyIndex < 6 ? sav.GetPartySlotAtIndex(ubi.PartyIndex) : null)
            : sav.GetBoxSlotAtIndex(ubi.Box, ubi.Slot);

    /// <summary>
    /// Escribe el PKM editado de vuelta en el save (aquí está la corrección clave del v1.1:
    /// GetPartySlotAtIndex/GetBoxData devuelven COPIAS; sin escribir de vuelta no se guarda nada).
    /// </summary>
    private static void EscribirDeVuelta(SaveFile sav, UbicacionPkm ubi, PKM pkm)
    {
        if (ubi.EsEquipo)
        {
            // Recalcular stats de partido para que se vean en pantalla al instante
            try
            {
                pkm.ResetPartyStats();
            }
            catch { }
            sav.SetPartySlotAtIndex(pkm, ubi.PartyIndex);
        }
        else
        {
            sav.SetBoxSlotAtIndex(pkm, ubi.Box, ubi.Slot);
        }
    }

    private static (int Caja, int Slot) BuscarHuecoCaja(SaveFile sav)
    {
        for (var b = 0; b < sav.BoxCount; b++)
        {
            var datos = sav.GetBoxData(b);
            for (var s = 0; s < datos.Length; s++)
                if (datos[s].Species == 0)
                    return (b, s);
        }
        return (-1, -1);
    }

    /// <summary>Registra visto+capturado en la Pokédex vía la propiedad Dex del save (reflection: tipo varía por gen).</summary>
    private static string RegistrarDex(SaveFile sav, PKM pkm)
    {
        try
        {
            var dex = sav.GetType().GetProperty("Dex")?.GetValue(sav);
            if (dex is null) return "";
            var tipo = dex.GetType().BaseType ?? dex.GetType();
            tipo.GetMethod("SetSeen", [typeof(ushort), typeof(bool)])?.Invoke(dex, [pkm.Species, true]);
            tipo.GetMethod("SetCaught", [typeof(ushort), typeof(bool)])?.Invoke(dex, [pkm.Species, true]);
            return " · registrado en la Pokédex";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Verificador de EV: la stat debe valer exactamente lo esperado.</summary>
    private static Func<SaveFile, string?> VerificarEv(SaveFile _, UbicacionPkm ubi, string stat, int esperado)
        => releido =>
        {
            var p = LeerEn(releido, ubi);
            return p is null ? "el Pokémon ya no está en su ubicación"
                : EfectosCartas.LeerEv(p, stat) == esperado ? null
                : $"el EV de {stat} leído ({EfectosCartas.LeerEv(p, stat)}) no es el esperado ({esperado})";
        };

    private static Func<SaveFile, string?> VerificarIv(SaveFile _, UbicacionPkm ubi, string stat, int esperado)
        => releido =>
        {
            var p = LeerEn(releido, ubi);
            return p is null ? "el Pokémon ya no está en su ubicación"
                : EfectosCartas.LeerIv(p, stat) == esperado ? null
                : $"el IV de {stat} leído ({EfectosCartas.LeerIv(p, stat)}) no es el esperado ({esperado})";
        };

    private static Func<SaveFile, string?> VerificarIvsCero(SaveFile _, UbicacionPkm ubi, List<string> stats)
        => releido =>
        {
            var p = LeerEn(releido, ubi);
            if (p is null) return "el Pokémon objetivo ya no está en su ubicación";
            foreach (var s in stats)
                if (EfectosCartas.LeerIv(p, s) != 0)
                    return $"el IV de {s} no quedó a 0";
            return null;
        };

    private static Func<SaveFile, string?> VerificarReparto(SaveFile _, UbicacionPkm ubi, Dictionary<string, int> reparto)
        => releido =>
        {
            var p = LeerEn(releido, ubi);
            if (p is null) return "el Pokémon ya no está en su ubicación";
            foreach (var (stat, esperado) in reparto)
            {
                var leido = EfectosCartas.LeerEv(p, stat);
                if (leido != esperado)
                    return $"el EV de {stat} leído ({leido}) no es el esperado ({esperado})";
            }
            return null;
        };

    /// <summary>Validación de reparto del diálogo: ≤252 por stat, ≤510 total, stats válidas.</summary>
    public static bool RepartoValido(Dictionary<string, int> reparto, out string motivo)
    {
        motivo = "";
        if (reparto.Count == 0) { motivo = "reparto vacío"; return false; }
        var total = 0;
        foreach (var (stat, valor) in reparto)
        {
            if (!EfectosCartas.EsStatValida(stat)) { motivo = $"stat «{stat}» no válida"; return false; }
            if (valor is < 0 or > 252) { motivo = $"la stat {stat} excede 252 ({valor})"; return false; }
            total += valor;
        }
        if (total > 510) { motivo = $"el total ({total}) excede 510 EVs"; return false; }
        return true;
    }

    private static string EtiquetaCorta(PKM pkm)
    {
        var strings = GameInfo.GetStrings("es");
        var nombre = string.IsNullOrWhiteSpace(pkm.Nickname)
            ? (pkm.Species > 0 && pkm.Species < strings.Species.Count ? strings.Species[pkm.Species] : "?")
            : pkm.Nickname;
        return $"{nombre} Nv{pkm.Stat_Level}";
    }

    /// <summary>Mapea el nombre del objeto de la app (español) al índice PKHeX: excepciones → exacto → normalizado.</summary>
    /// <summary>¿El nombre corresponde a Cápsula/Parche Habilidad?</summary>
    private static bool EsCapsulaHabilidad(string nombre)
    {
        var n = JuegosApp.Normalizar(nombre);
        return (n.Contains("capsula") && n.Contains("habilidad"))
            || (n.Contains("capsule") && n.Contains("ability"))
            || (n.Contains("patch") && n.Contains("ability"))
            || n == "abilitycapsule";
    }

    /// <summary>¿La mochila de esta partida admite el objeto? (si no existe, false).</summary>
    private static bool ObjetoAdmisible(SaveFile sav, int? id)
    {
        if (id is null or <= 0) return false;
        var bag = sav.Inventory;
        foreach (var tipo in Enum.GetValues<InventoryType>())
        {
            if (tipo is InventoryType.None or InventoryType.FreeSpace) continue;
            try { if (bag.Info.GetItems(tipo).Contains((ushort)id.Value)) return true; } catch { }
        }
        return false;
    }

    /// <summary>¿La app pide una «megapiedra» genérica? (no existe como objeto; hay que elegir una).</summary>
    private static bool EsMegapiedra(string nombre)
    {
        var n = JuegosApp.Normalizar(nombre);
        return n.Contains("megapiedra") || n.Contains("megastone") || n.Contains("mega stone")
            || n.Contains("piedra mega") || n == "megapiedras";
    }

    /// <summary>Todas las megapiedras que existen en esta partida (es/en), filtradas a las que admite la mochila.</summary>
    private static List<(int Id, string Nombre)> MegapiedrasDisponibles(SaveFile sav)
    {
        var permitidos = new HashSet<ushort>();
        var bag = sav.Inventory;
        foreach (var tipo in Enum.GetValues<InventoryType>())
        {
            if (tipo is InventoryType.None or InventoryType.FreeSpace) continue;
            try { foreach (var id in bag.Info.GetItems(tipo)) permitidos.Add(id); } catch { }
        }

        var excluidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "eviolita", "eviolite", "piedra", "piedra dia", "piedra noche", "piedra solar", "piedra lunar",
            "piedra trueno", "piedra fuego", "piedra agua", "piedra hoja", "piedra hielo", "piedra oval",
            "piedra dura", "piedra eterna", "piedra rey", "piedra alba", "piedra dusclop",
        };

        var es = GameInfo.GetStrings("es").Item;
        var en = GameInfo.GetStrings("en").Item;
        var lista = new List<(int Id, string Nombre)>();
        for (var id = 1; id < es.Count && id < en.Count; id++)
        {
            if (!permitidos.Contains((ushort)id)) continue;
            var nombreEs = es[id] ?? "";
            var nombreEn = en[id] ?? "";
            if (excluidas.Contains(nombreEs) || excluidas.Contains(nombreEn)) continue;
            // Las megapiedras se llaman «X-ita» en español y «X-ite» en inglés.
            var esMega = nombreEs.EndsWith("ita", StringComparison.OrdinalIgnoreCase)
                         && !nombreEs.StartsWith("piedra", StringComparison.OrdinalIgnoreCase);
            var enMega = nombreEn.EndsWith("ite", StringComparison.OrdinalIgnoreCase)
                         && !nombreEn.StartsWith("stone", StringComparison.OrdinalIgnoreCase);
            if (!esMega && !enMega) continue;
            lista.Add((id, string.IsNullOrWhiteSpace(nombreEs) ? nombreEn : nombreEs));
        }
        return lista.OrderBy(o => o.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int? MapearObjeto(string nombreApp)
    {
        var nombre = nombreApp.Trim();
        // 1) Diccionario de excepciones (nombre-app → id PKHeX)
        if (ExcepcionesObjetos.TryGetValue(nombre, out var id) && id >= 0)
            return id;

        // 2) Coincidencia exacta en ESPAÑOL y en INGLÉS (la app puede mandar cualquiera de los dos)
        foreach (var idioma in new[] { "es", "en" })
        {
            var tabla = GameInfo.GetStrings(idioma).Item;
            var exacto = InyectorEquipo.BuscarIndice(tabla, nombre);
            if (exacto > 0) return exacto;
        }

        // 3) Coincidencia normalizada (sin tildes/espacios, mayúsculas ignoradas) en ambos idiomas
        var objetivo = JuegosApp.Normalizar(nombre);
        foreach (var idioma in new[] { "es", "en" })
        {
            var tabla = GameInfo.GetStrings(idioma).Item;
            for (var i = 1; i < tabla.Count; i++)
                if (JuegosApp.Normalizar(tabla[i]) == objetivo)
                    return i;
        }
        return null;
    }

    /// <summary>Excepciones nombre-app → objeto PKHeX (nombres que no coinciden en español).</summary>
    private static readonly Dictionary<string, int> ExcepcionesObjetos = new(StringComparer.OrdinalIgnoreCase)
    {
        // La Master Ball (80 → llega como ITEM_GRANT) es el caso documentado del contrato
        ["Master Ball"] = 1,
        ["masterball"] = 1,
        // Curativos competitivos habituales (los índices se resuelven igual en las strings es;
        // se listan explícitos por si el catálogo de la app cambia el nombre)
        ["Poción"] = -1,      // -1 → resuelve por nombre (coincide)
        ["Superpoción"] = -1,
        ["Hiperpoción"] = -1,
        ["Máxima poción"] = -1,
        ["Revivir"] = -1,
        ["Revivir máximo"] = -1,
        ["Antídoto"] = -1,
        ["Cura total"] = -1,
        ["Restaurar todo"] = -1,
        ["Elixir"] = -1,
        ["Éter"] = -1,
    };

    // ─────────────────────────── validación de juego (v1.1.e) ───────────────────────────

    /// <summary>Comprueba que el juego del save está en el conjunto aceptable del nombre de la acción.</summary>
    private static bool ValidarJuego(string? gameNameAccion, SaveFile sav, string? archivo, out string msg)
    {
        msg = "";
        if (string.IsNullOrWhiteSpace(gameNameAccion)) return true;
        if (JuegosApp.AceptaSave(gameNameAccion, sav.Version)) return true;
        msg = $"El locke es de {gameNameAccion} pero la partida es de {sav.Version} ({Path.GetFileName(archivo ?? "")}).";
        return false;
    }

    // ─────────────────────────── helpers de resultado ───────────────────────────

    private static ResultadoAccion Ok(string msg, Func<SaveFile, string?> verificador)
        => new("", AccionEstado.Aplicada, msg, null, verificador);

    /// <summary>Cancelación (v1.1.c): estado Pendiente — NO marca failed en Firebase.</summary>
    private static ResultadoAccion Cancelado()
        => new("", AccionEstado.Pendiente, "Cancelado por el usuario; la acción sigue pendiente.");

    private static ResultadoAccion DatoInvalido(string motivo)
        => new("", AccionEstado.Fallida, motivo, motivo);

    private static ResultadoAccion FaltaParametro(string p)
        => DatoInvalido($"Falta el parámetro: {p}.");

    private static int IdHabilidadActual(PKM pkm)
    {
        var slot = pkm.Ability;
        var personal = pkm.PersonalInfo;
        if (pkm.Format <= 6 && slot >= 0 && slot < personal.AbilityCount)
        {
            var id = personal.GetAbilityAtIndex(slot);
            if (id >= 0) return id;
        }
        return pkm.Ability;
    }

    // ─────────────────────────── persistencia ───────────────────────────

    /// <summary>Abre un archivo de partida con PKHeX (o con el abridor inyectado en tests).</summary>
    private SaveFile AbrirSave(string ruta)
        => Abridor?.Invoke(ruta)
           ?? SaveUtil.GetSaveFile(ruta)
           ?? throw new InvalidOperationException($"PKHeX no reconoce el archivo de partida: {ruta}");

    /// <summary>Restaura el buffer del save al snapshot (rollback por acción).</summary>
    private static void RestauraData(SaveFile sav, byte[] snapshot)
    {
        if (snapshot.Length != sav.Data.Length) return;
        snapshot.AsSpan().CopyTo(sav.Data);   // SaveFile.Data ya es Span<byte>
    }

    private string RutaHuellas => Path.Combine(_datosDir, "huellas.json");

    /// <summary>Regenera la huella del .sav para que la integridad no lo vea como modificación externa.</summary>
    private void RegenerarHuella(string rutaSave)
    {
        var contenido = File.Exists(RutaHuellas) ? File.ReadAllText(RutaHuellas) : "{}";
        var nodo = JsonNode.Parse(contenido)?.AsObject() ?? new JsonObject();
        var bytes = File.ReadAllBytes(rutaSave);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        nodo[rutaSave.ToLowerInvariant()] = new JsonObject
        {
            ["RomHash"] = hash,
            ["RomSize"] = bytes.Length,
            ["RomMtime"] = File.GetLastWriteTimeUtc(rutaSave).Ticks,
            ["RomMotivo"] = "launcher-aplicó",
            ["RomFecha"] = DateTime.UtcNow.ToString("O"),
            ["SavePath"] = rutaSave,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(RutaHuellas)!);
        File.WriteAllText(RutaHuellas, nodo.ToJsonString());
    }

    private static void CrearBackup(string rutaSave)
    {
        var dir = Path.GetDirectoryName(rutaSave)!;
        var nombre = Path.GetFileNameWithoutExtension(rutaSave);
        File.Copy(rutaSave, Path.Combine(dir, $"{nombre}.bak-{DateTime.Now:yyyyMMdd-HHmmss}"), overwrite: true);
    }

    private static void RestaurarBackup(string rutaSave)
    {
        var dir = Path.GetDirectoryName(rutaSave)!;
        var nombre = Path.GetFileNameWithoutExtension(rutaSave);
        var masReciente = Directory.GetFiles(dir, $"{nombre}.bak-*")
            .Select(f => (Ruta: f, Fecha: File.GetLastWriteTime(f)))
            .OrderByDescending(x => x.Fecha)
            .FirstOrDefault();
        if (masReciente.Ruta is not null)
            File.Copy(masReciente.Ruta, rutaSave, overwrite: true);
    }

    // ─────────────────────────── idempotencia local ───────────────────────────

    private string RutaAplicadasLocal => Path.Combine(_datosDir, "acciones-aplicadas.txt");

    private string ClaveLocal(string actionId) => actionId;

    private HashSet<string> LeerAplicadasLocales()
    {
        try
        {
            return File.Exists(RutaAplicadasLocal) ? File.ReadAllLines(RutaAplicadasLocal).ToHashSet() : [];
        }
        catch { return []; }
    }

    // ─────────────────────────── intentos de objetivo (cartas 87/88) ───────────────────────────

    private string RutaIntentos => Path.Combine(_datosDir, "intentos-objetivo.txt");

    /// <summary>
    /// Cuenta cuántas veces una acción 87/88 no encontró su objetivo. A partir de 3 se avisa
    /// en el log del launcher (la acción sigue pending; no se repiten diálogos).
    /// </summary>
    private int RegistrarIntentoObjetivo(AccionLauncher acc, int speciesId)
    {
        try
        {
            var clave = $"{acc.CardId}|{speciesId}|{acc.CreatedBy}";
            var lineas = File.Exists(RutaIntentos) ? File.ReadAllLines(RutaIntentos).ToList() : [];
            var indice = lineas.FindIndex(l => l.StartsWith(clave + "=", StringComparison.Ordinal));
            int intentos;
            if (indice >= 0)
            {
                var partes = lineas[indice].Split('=');
                intentos = (int.TryParse(partes.LastOrDefault(), out var n) ? n : 0) + 1;
                lineas[indice] = $"{clave}={intentos}";
            }
            else
            {
                intentos = 1;
                lineas.Add($"{clave}={intentos}");
            }
            Directory.CreateDirectory(_datosDir);
            File.WriteAllLines(RutaIntentos, lineas);
            return intentos;
        }
        catch
        {
            return 1;
        }
    }

    private void RegistrarAplicadaLocal(string clave)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaAplicadasLocal)!);
            File.AppendAllText(RutaAplicadasLocal, clave + Environment.NewLine);
        }
        catch { }
    }

    // ─────────────────────────── diagnóstico ───────────────────────────
    private string RutaDiagnostico => Path.Combine(_datosDir, "cola-diagnostico.log");

    /// <summary>Log a archivo (independiente de la UI) para diagnosticar en el PC del jugador.</summary>
    internal void Diagnostico(string linea)
    {
        try
        {
            Directory.CreateDirectory(_datosDir);
            File.AppendAllText(RutaDiagnostico, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {linea}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Deja solo los <see cref="MaxBackups"/> backups más recientes de esta partida.</summary>
    private void PodarBackups(string rutaSave)
    {
        try
        {
            var carpeta = Path.GetDirectoryName(rutaSave);
            var patron = Path.GetFileName(rutaSave) + ".bak-*";
            if (carpeta is null) return;
            var backups = Directory.GetFiles(carpeta, patron).OrderByDescending(File.GetLastWriteTime).ToList();
            foreach (var viejo in backups.Skip(Math.Max(1, MaxBackups)))
                File.Delete(viejo);
        }
        catch { }
    }
}