using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Rama de la cola para **Pokémon Añil** (`.rxdata`): reclama, aplica con <see cref="AplicadorAnil"/>,
/// guarda el archivo (con backup) y marca el resultado, igual que con los saves de PKHeX.
/// </summary>
public sealed partial class ColaLauncherService
{
    /// <summary>Carpeta del juego (para leer su PBS). La fija la UI al lanzar/aplicar.</summary>
    public string? CarpetaJuegoAnil { get; set; }

    public Func<string, IReadOnlyList<(string Etiqueta, Pokemon Mon)>, int?>? PedirPokemonAnil { get; set; }
    public Func<string, int?>? PedirStatAnil { get; set; }
    public Func<string, IReadOnlyList<string>, string?>? PedirHabilidadAnil { get; set; }
    public Func<string, IReadOnlyList<string>, string?>? PedirMovimientoAnil { get; set; }
    public Func<string, int[], int[], int, (int[] Ivs, int[] Evs)?>? PedirEdicionStatsAnil { get; set; }

    private AplicadorAnil ConstruirAplicador() => new()
    {
        PedirPokemon = PedirPokemonAnil,
        PedirStat = PedirStatAnil,
        PedirHabilidad = PedirHabilidadAnil,
        PedirMovimiento = PedirMovimientoAnil,
        PedirEdicionStats = PedirEdicionStatsAnil,
    };

    private static bool EsPartidaAnil(string ruta) => ruta.EndsWith(".rxdata", StringComparison.OrdinalIgnoreCase);

    private async Task<List<ResultadoAccion>> ProcesarColaAnilAsync(
        string lockeId, string playerTag, string rutaSave,
        List<KeyValuePair<string, AccionLauncher>> ordenadas,
        HashSet<string> aplicadasLocal, List<ResultadoAccion> resultados,
        CancellationToken ct)
    {
        Diagnostico($"== Cola AÑIL: locke={lockeId} tag={playerTag} save={rutaSave} existe={File.Exists(rutaSave)}");
        if (_emuladorAbierto())
        {
            AlProgresar?.Invoke("El juego está abierto: cierra Pokémon Añil antes de aplicar acciones.");
            return resultados;
        }

        var pbs = PartidaAnil.PbsDe(CarpetaJuegoAnil) ?? PartidaAnil.PbsDe(Path.GetDirectoryName(rutaSave));
        PartidaAnil partida;
        try { partida = PartidaAnil.Cargar(rutaSave, pbs); }
        catch (Exception ex)
        {
            Diagnostico("no se pudo leer el .rxdata: " + ex.Message);
            return resultados;
        }

        var aplicador = ConstruirAplicador();
        foreach (var (actionId, accion) in ordenadas)
        {
            if (ct.IsCancellationRequested) break;
            if (aplicadasLocal.Contains(ClaveLocal(actionId)))
            {
                await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId, AccionEstado.Aplicada,
                    "Ya aplicada anteriormente por este launcher.", null, ct);
                continue;
            }

            var reclamada = await _firebase.ReclamarAccionAsync(lockeId, playerTag, actionId, accion, ct);
            if (!reclamada) continue;

            AlProgresar?.Invoke($"Procesando {actionId[..Math.Min(8, actionId.Length)]}… ({accion.Type}) [Añil]");
            var resultado = aplicador.Aplicar(accion, partida);
            Diagnostico($"   [{actionId}] {accion.Type} card={accion.CardId} → {resultado.Estado}: {resultado.ResultMessage}");

            switch (resultado.Estado)
            {
                case AccionEstado.Aplicada:
                    partida.Guardar();
                    partida = PartidaAnil.Cargar(rutaSave, pbs);   // releer para el siguiente
                    RegistrarAplicadaLocal(ClaveLocal(actionId));
                    await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId, AccionEstado.Aplicada,
                        resultado.ResultMessage, null, ct);
                    resultados.Add(resultado with { ActionId = actionId });
                    break;
                case AccionEstado.Pendiente:
                    await _firebase.DevolverAPendienteAsync(lockeId, playerTag, actionId, resultado.ResultMessage, ct);
                    resultados.Add(resultado with { ActionId = actionId });
                    break;
                case AccionEstado.Fallida:
                    await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId, AccionEstado.Fallida,
                        resultado.ResultMessage, resultado.ErrorMessage, ct);
                    resultados.Add(resultado with { ActionId = actionId });
                    break;
                default:
                    await _firebase.MarcarResultadoAsync(lockeId, playerTag, actionId, AccionEstado.Omitida,
                        resultado.ResultMessage, null, ct);
                    resultados.Add(resultado with { ActionId = actionId });
                    break;
            }
        }

        Diagnostico($"   fin cola Añil: {resultados.Count(r => r.Estado == AccionEstado.Aplicada)} aplicadas");
        return resultados;
    }
}
