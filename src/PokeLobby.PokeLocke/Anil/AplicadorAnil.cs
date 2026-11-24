using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Aplica las acciones de la cola sobre una partida de **Pokémon Añil** (`.rxdata`).
/// Los diálogos se inyectan como delegados para poder probarlo sin UI.
/// </summary>
public sealed class AplicadorAnil
{
    /// <summary>Elige un Pokémon: recibe la lista (etiqueta, Pokémon) y devuelve el índice o null.</summary>
    public Func<string, IReadOnlyList<(string Etiqueta, Pokemon Mon)>, int?>? PedirPokemon { get; set; }

    /// <summary>Elige una estadística (0..5).</summary>
    public Func<string, int?>? PedirStat { get; set; }

    /// <summary>Elige una habilidad del juego (nombre interno) o null.</summary>
    public Func<string, IReadOnlyList<string>, string?>? PedirHabilidad { get; set; }

    /// <summary>Elige un movimiento del juego (nombre interno) o null.</summary>
    public Func<string, IReadOnlyList<string>, string?>? PedirMovimiento { get; set; }

    /// <summary>Editor de IVs/EVs; recibe IVs/EVs actuales y el tope de EVs.</summary>
    public Func<string, int[], int[], int, (int[] Ivs, int[] Evs)?>? PedirEdicionStats { get; set; }

    private static readonly string[] Stats = ["hp", "atk", "def", "spa", "spd", "spe"];

    public ResultadoAccion Aplicar(AccionLauncher acc, PartidaAnil partida)
    {
        try
        {
            return acc.Type switch
            {
                AccionTipo.GachaPokemon => Gacha(acc, partida),
                AccionTipo.ItemGrant => Objeto(acc, partida),
                AccionTipo.MintUse => Menta(acc, partida),
                AccionTipo.CardEffect => Carta(acc, partida),
                _ => new ResultadoAccion("", AccionEstado.Omitida, $"Tipo {acc.Type} no soportado en Añil"),
            };
        }
        catch (Exception ex)
        {
            return new ResultadoAccion("", AccionEstado.Fallida, "Error al aplicar en Añil", ex.Message);
        }
    }

    // ─────────────────────────── gacha / objeto / menta ───────────────────────────

    private ResultadoAccion Gacha(AccionLauncher acc, PartidaAnil partida)
    {
        var speciesId = acc.SpeciesId ?? 0;
        if (speciesId is < 1 or > 1025)
            return new ResultadoAccion("", AccionEstado.Fallida, $"SpeciesId {speciesId} inválido");
        var mote = acc.ParamStr("nickname");
        var donde = partida.CrearGacha(speciesId, acc.SpeciesName, acc.IsShiny ?? false, mote, out var nota);
        if (donde is null)
            return new ResultadoAccion("", AccionEstado.Fallida, $"No se pudo añadir: {nota}");

        return new ResultadoAccion("", AccionEstado.Aplicada,
            $"{acc.SpeciesName ?? "#" + speciesId} Nv1 añadido en {donde} (Añil)" + (nota is null ? "" : $" — {nota}"));
    }

    private ResultadoAccion Objeto(AccionLauncher acc, PartidaAnil partida)
    {
        var nombre = acc.ItemName;
        var cantidad = Math.Clamp(acc.Quantity ?? 1, 1, 999);
        var puestos = partida.AnadirObjeto(nombre, cantidad);
        if (puestos < 0)
            return new ResultadoAccion("", AccionEstado.Fallida, $"Objeto «{nombre}» no encontrado en el PBS de Añil");
        return new ResultadoAccion("", AccionEstado.Aplicada, $"+{puestos} «{nombre}» en la mochila (Añil)");
    }

    private ResultadoAccion Menta(AccionLauncher acc, PartidaAnil partida)
    {
        var nat = acc.Nature;
        if (string.IsNullOrWhiteSpace(nat))
            return new ResultadoAccion("", AccionEstado.Fallida, "MINT_USE sin naturaleza");
        if (PedirPokemon is null) return new ResultadoAccion("", AccionEstado.Fallida, "falta el diálogo de Pokémon");
        var elegido = PedirPokemon("Menta: elige el Pokémon", Listar(partida));
        if (elegido is null) return Cancelado();
        var p = PokemonEn(partida, elegido.Value);
        if (p is null) return new ResultadoAccion("", AccionEstado.Fallida, "Pokémon no válido");
        var ok = partida.CambiarNaturaleza(p, nat);
        return ok
            ? new ResultadoAccion("", AccionEstado.Aplicada, $"Naturaleza cambiada a {nat} ({p.Species})")
            : new ResultadoAccion("", AccionEstado.Fallida, "No se pudo cambiar la naturaleza");
    }

    private static ResultadoAccion Cancelado() => new("", AccionEstado.Pendiente, "Cancelado por el jugador");

    // ─────────────────────────── cartas ───────────────────────────

    private ResultadoAccion Carta(AccionLauncher acc, PartidaAnil partida)
    {
        switch (acc.CardId)
        {
            case 67:   // Baño de dinero: +25.000
            {
                partida.Dinero = partida.Dinero + 25_000;
                return new ResultadoAccion("", AccionEstado.Aplicada, $"+25.000 ₽ (total: {partida.Dinero:N0}) (Añil)");
            }
            case 65:   // Máquina MT: desbloquear todas
            {
                var cuantas = partida.DesbloquearMts();
                return new ResultadoAccion("", AccionEstado.Aplicada, $"MT/MO desbloqueadas: {cuantas} (Añil)");
            }
            case 41 or 42:  // Entreno: 1 o 2 stats a IV 31
                return Entreno(acc, partida);
            case 70 or 78:  // EV +128 / al máximo
                return Evs(acc, partida);
            case 81:        // reparto de EVs
                return Reparto(acc, partida);
            case 83 or 84:  // editor de IVs/EVs
                return Editor(acc, partida);
            case 63:        // Cápsula de habilidad
                return Habilidad(acc, partida);
            case 66:        // MT divina
                return Movimiento(acc, partida);
            case 36 or 56 or 59:  // shiny
                return Shiny(acc, partida);
            case 79:        // Mentos: copiar movimiento
                return Mentor(partida);
            case 82:        // Modificación genética: intercambiar habilidades
                return Intercambio(partida);
            default:
                return new ResultadoAccion("", AccionEstado.Omitida, $"Carta {acc.CardId} aún no soportada en Añil");
        }
    }

    private ResultadoAccion Entreno(AccionLauncher acc, PartidaAnil partida)
    {
        var (p, err) = Elegir(partida, "Entreno: elige el Pokémon");
        if (err is not null) return err!;
        var stats = acc.ParamStrList("stats").Concat(acc.ParamStr("stat") is { } s ? new[] { s } : Array.Empty<string>())
            .Where(x => Stats.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct().Take(2).ToList();
        while (stats.Count < (acc.CardId == 41 ? 1 : 2))
        {
            if (PedirStat is null) return new ResultadoAccion("", AccionEstado.Fallida, "falta el diálogo de estadística");
            var idx = PedirStat($"¿Qué stat a IV 31? ({NombreDe(partida, p!)})");
            if (idx is null) return Cancelado();
            if (idx < 0 || idx > 5) return new ResultadoAccion("", AccionEstado.Fallida, "stat inválida");
            if (!stats.Contains(Stats[idx.Value])) stats.Add(Stats[idx.Value]);
        }
        var ivs = (int[])p!.IVs.Clone();
        foreach (var st in stats) ivs[Array.IndexOf(Stats, st)] = 31;
        partida.PonerIvsYevs(p, ivs, p.EVs);
        return new ResultadoAccion("", AccionEstado.Aplicada,
            $"IV 31 en {string.Join("/", stats)} de {p.Species} (Añil)");
    }

    private ResultadoAccion Evs(AccionLauncher acc, PartidaAnil partida)
    {
        var (p, err) = Elegir(partida, "EVs: elige el Pokémon");
        if (err is not null) return err!;
        int idx;
        if (PedirStat is null) return new ResultadoAccion("", AccionEstado.Fallida, "falta el diálogo de estadística");
        var elegido = PedirStat($"¿Qué stat? ({NombreDe(partida, p!)})");
        if (elegido is null) return Cancelado();
        idx = elegido.Value;
        if (idx is < 0 or > 5) return new ResultadoAccion("", AccionEstado.Fallida, "stat inválida");
        var evs = (int[])p!.EVs.Clone();
        evs[idx] = acc.CardId == 70 ? Math.Min(252, evs[idx] + 128) : 252;
        partida.PonerIvsYevs(p, p.IVs, evs);
        return new ResultadoAccion("", AccionEstado.Aplicada, $"EV {Stats[idx]} = {evs[idx]} en {p.Species} (Añil)");
    }

    private ResultadoAccion Reparto(AccionLauncher acc, PartidaAnil partida)
    {
        var (p, err) = Elegir(partida, "Jefe de obra: elige el Pokémon");
        if (err is not null) return err!;
        if (PedirEdicionStats is null) return new ResultadoAccion("", AccionEstado.Fallida, "falta el editor de IVs/EVs");
        var editado = PedirEdicionStats("Jefe de obra: reparte EVs (máx. 510)", p!.IVs, p.EVs, 510);
        if (editado is null) return Cancelado();
        partida.PonerIvsYevs(p, editado.Value.Ivs, editado.Value.Evs);
        return new ResultadoAccion("", AccionEstado.Aplicada, $"IVs/EVs repartidos en {p.Species} (Añil)");
    }

    private ResultadoAccion Editor(AccionLauncher acc, PartidaAnil partida)
    {
        var (p, err) = Elegir(partida, "Elige el Pokémon");
        if (err is not null) return err!;
        if (PedirEdicionStats is null) return new ResultadoAccion("", AccionEstado.Fallida, "falta el editor de IVs/EVs");
        var tope = acc.CardId == 83 ? 252 : 510;
        var propuesta = acc.CardId == 83
            ? (p!.IVs.Select(_ => 31).ToArray(), new[] { 0, 252, 0, 0, 0, 252 })
            : (p!.IVs.Select(_ => 31).ToArray(), new[] { 4, 252, 0, 0, 0, 252 });
        var editado = PedirEdicionStats($"Ajusta IVs y EVs (máx. {tope})", propuesta.Item1, propuesta.Item2, tope);
        if (editado is null) return Cancelado();
        partida.PonerIvsYevs(p, editado.Value.Ivs, editado.Value.Evs);
        return new ResultadoAccion("", AccionEstado.Aplicada, $"IVs/EVs aplicados en {p.Species} (Añil)");
    }

    private ResultadoAccion Habilidad(AccionLauncher acc, PartidaAnil partida)
    {
        var (p, err) = Elegir(partida, "Cápsula: elige el Pokémon");
        if (err is not null) return err!;
        var opciones = partida.Pbs.Abilities.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (opciones.Count == 0) return new ResultadoAccion("", AccionEstado.Fallida, "el PBS no tiene habilidades");
        var hab = acc.ParamStr("ability") ?? PedirHabilidad?.Invoke($"Habilidad para {NombreDe(partida, p!)}", opciones);
        if (hab is null) return Cancelado();
        var ok = partida.PonerHabilidad(p!, hab);
        return ok
            ? new ResultadoAccion("", AccionEstado.Aplicada, $"{p!.Species} ahora tiene {(partida.Pbs.Abilities.TryGetValue(hab, out var n) ? n : hab)} (Añil)")
            : new ResultadoAccion("", AccionEstado.Fallida, $"no se pudo asignar «{hab}»");
    }

    private ResultadoAccion Movimiento(AccionLauncher acc, PartidaAnil partida)
    {
        var (p, err) = Elegir(partida, "MT Divina: elige el Pokémon");
        if (err is not null) return err!;
        var opciones = partida.Pbs.Moves.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (opciones.Count == 0) return new ResultadoAccion("", AccionEstado.Fallida, "el PBS no tiene movimientos");
        var mov = acc.ParamStr("move") ?? PedirMovimiento?.Invoke($"Movimiento para {NombreDe(partida, p!)}", opciones);
        if (mov is null) return Cancelado();
        var indice = p!.Moves.Count < 4 ? p.Moves.Count : 0;   // primer hueco libre o el primero
        var ok = partida.EnsenarMovimiento(p, indice, mov);
        return ok
            ? new ResultadoAccion("", AccionEstado.Aplicada, $"{p.Species} aprendió {(partida.Pbs.Moves.TryGetValue(mov, out var n) ? n : mov)} (Añil)")
            : new ResultadoAccion("", AccionEstado.Fallida, $"no se pudo enseñar «{mov}»");
    }

    private ResultadoAccion Shiny(AccionLauncher acc, PartidaAnil partida)
    {
        var (p, err) = Elegir(partida, "Shiny: elige el Pokémon");
        if (err is not null) return err!;
        var objetivo = acc.CardId switch
        {
            36 => true,          // Glow up: shiny
            56 => !p!.IsShiny,   // Cambio color: conmutar
            59 => false,         // Muda de piel: quitar
            _ => p!.IsShiny,
        };
        if (acc.CardId == 59) partida.Curar(p!);   // Muda de piel: además lo revive
        var ok = partida.PonerShiny(p!, objetivo);
        return ok
            ? new ResultadoAccion("", AccionEstado.Aplicada, $"{p!.Species} {(objetivo ? "ahora es shiny" : "ya no es shiny")} (Añil)")
            : new ResultadoAccion("", AccionEstado.Fallida, "no se pudo cambiar el shiny");
    }

    private ResultadoAccion Mentor(PartidaAnil partida)
    {
        var lista = Listar(partida);
        if (PedirPokemon is null) return new ResultadoAccion("", AccionEstado.Fallida, "falta el diálogo de Pokémon");
        var oi = PedirPokemon("Mentor: ORIGEN", lista);
        if (oi is null) return Cancelado();
        var di = PedirPokemon("Mentor: DESTINO", lista);
        if (di is null) return Cancelado();
        if (oi == di) return new ResultadoAccion("", AccionEstado.Fallida, "origen y destino no pueden ser el mismo");
        var origen = PokemonEn(partida, oi.Value);
        var destino = PokemonEn(partida, di.Value);
        if (origen is null || destino is null) return new ResultadoAccion("", AccionEstado.Fallida, "Pokémon no válido");
        var mov = origen.Moves.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.InternalName));
        if (mov is null) return new ResultadoAccion("", AccionEstado.Fallida, "el origen no conoce movimientos");
        var indice = destino.Moves.Count < 4 ? destino.Moves.Count : 0;
        var ok = partida.EnsenarMovimiento(destino, indice, mov.InternalName);
        return ok
            ? new ResultadoAccion("", AccionEstado.Aplicada, $"{destino.Species} copió {mov.Name} de {origen.Species} (Añil)")
            : new ResultadoAccion("", AccionEstado.Fallida, "no se pudo copiar el movimiento");
    }

    private ResultadoAccion Intercambio(PartidaAnil partida)
    {
        var lista = Listar(partida);
        if (PedirPokemon is null) return new ResultadoAccion("", AccionEstado.Fallida, "falta el diálogo de Pokémon");
        var i1 = PedirPokemon("Modificación genética: PRIMER Pokémon", lista);
        if (i1 is null) return Cancelado();
        var i2 = PedirPokemon("Modificación genética: SEGUNDO Pokémon", lista);
        if (i2 is null) return Cancelado();
        if (i1 == i2) return new ResultadoAccion("", AccionEstado.Fallida, "deben ser dos Pokémon distintos");
        var p1 = PokemonEn(partida, i1.Value);
        var p2 = PokemonEn(partida, i2.Value);
        if (p1 is null || p2 is null) return new ResultadoAccion("", AccionEstado.Fallida, "Pokémon no válido");
        var h1 = p1.InternalAbility;
        var h2 = p2.InternalAbility;
        if (string.IsNullOrWhiteSpace(h1) || string.IsNullOrWhiteSpace(h2))
            return new ResultadoAccion("", AccionEstado.Fallida, "alguno de los dos no tiene habilidad asignada");
        partida.PonerHabilidad(p1, h2);
        partida.PonerHabilidad(p2, h1);
        return new ResultadoAccion("", AccionEstado.Aplicada,
            $"Habilidades intercambiadas: {p1.Species} ↔ {p2.Species} (Añil)");
    }

    // ─────────────────────────── utilidades ───────────────────────────

    private (Pokemon? P, ResultadoAccion? Error) Elegir(PartidaAnil partida, string titulo)
    {
        if (PedirPokemon is null)
            return (null, new ResultadoAccion("", AccionEstado.Fallida, "falta el diálogo de Pokémon"));
        var elegido = PedirPokemon(titulo, Listar(partida));
        if (elegido is null) return (null, Cancelado());
        var p = PokemonEn(partida, elegido.Value);
        return p is null
            ? (null, new ResultadoAccion("", AccionEstado.Fallida, "Pokémon no válido"))
            : (p, null);
    }

    /// <summary>Lista plana: equipo primero y después todas las cajas con Pokémon.</summary>
    public static List<(string Etiqueta, Pokemon Mon)> Listar(PartidaAnil partida)
    {
        var lista = new List<(string, Pokemon)>();
        for (var i = 0; i < partida.Equipo.Count; i++)
        {
            var p = partida.Equipo[i];
            lista.Add(($"Equipo {i + 1} · {p.Species} Nv{p.Level}", p));
        }
        for (var b = 0; b < partida.Cajas.Count; b++)
        {
            var caja = partida.Cajas[b];
            for (var s = 0; s < caja.Slots.Length; s++)
            {
                var p = caja.Slots[s];
                if (p is not null) lista.Add(($"Caja {b + 1} · hueco {s + 1} · {p.Species} Nv{p.Level}", p));
            }
        }
        return lista;
    }

    public static Pokemon? PokemonEn(PartidaAnil partida, int indice)
    {
        var lista = Listar(partida);
        return indice >= 0 && indice < lista.Count ? lista[indice].Mon : null;
    }

    private static string NombreDe(PartidaAnil partida, Pokemon p) => $"{p.Species} Nv{p.Level}";
}