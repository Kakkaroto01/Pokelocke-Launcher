using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Partida de **Pokémon Añil** (fangame de RPG Maker XP / Essentials con Pokédex hasta gen 9).
/// El guardado es un <c>.rxdata</c> serializado con Ruby Marshal; se parsea/escribe con el código
/// adaptado de AnilHeX (CC BY-NC-SA 4.0) y los datos del juego (PBS) para resolver nombres
/// internos de especie, habilidad, movimiento y objeto.
/// </summary>
public sealed class PartidaAnil
{
    public string Ruta { get; }
    public SaveDataModel Modelo { get; }
    public PBSReader Pbs { get; }
    /// <summary>Carpeta PBS desde la que se cargaron los datos del juego (null si no se encontró).</summary>
    public string? CarpetaPbs { get; private set; }
    private readonly SaveWriter _writer;

    public IReadOnlyList<Pokemon> Equipo => Modelo.Party;
    public IReadOnlyList<PCBox> Cajas => Modelo.Boxes;
    public IReadOnlyList<ItemSlot> Mochila => Modelo.Bag;

    private PartidaAnil(string ruta, SaveDataModel modelo, PBSReader pbs, SaveWriter writer)
    {
        Ruta = ruta; Modelo = modelo; Pbs = pbs; _writer = writer;
    }

    /// <summary>Carpeta PBS del juego a partir de la ruta del .exe o de la carpeta del juego.</summary>
    public static string? PbsDe(string? rutaJuego)
    {
        if (string.IsNullOrWhiteSpace(rutaJuego)) return null;
        try
        {
            var carpeta = File.Exists(rutaJuego) ? Path.GetDirectoryName(rutaJuego)! : rutaJuego;
            var pbs = Path.Combine(carpeta, "PBS");
            if (Directory.Exists(pbs)) return pbs;
            // por si apuntan a la raíz del juego con otro nombre
            var padre = Path.GetDirectoryName(carpeta);
            if (padre is not null)
            {
                var alt = Path.Combine(padre, "PBS");
                if (Directory.Exists(alt)) return alt;
            }
        }
        catch { }
        return null;
    }

    public static PartidaAnil Cargar(string rutaRxdata, string? carpetaPbs)
    {
        var pbs = new PBSReader();
        if (!string.IsNullOrWhiteSpace(carpetaPbs)) pbs.LoadPBSDirectory(carpetaPbs!);
        var modelo = new SaveParser(rutaRxdata, pbs).ParseSave();
        var writer = new SaveWriter(rutaRxdata, modelo, pbs);
        return new PartidaAnil(rutaRxdata, modelo, pbs, writer);
    }

    public void Guardar() => _writer.Save();

    /// <summary>
    /// Vuelve a mapear equipo/cajas/mochila desde el mismo árbol Ruby (necesario tras añadir un
    /// Pokémon: AddPokemon toca el árbol pero no la lista mapeada del modelo).
    /// </summary>
    public void Refrescar()
    {
        var nuevo = new SaveParser(Ruta, Pbs).ParseSave(Modelo);
        Modelo.Party.Clear(); Modelo.Party.AddRange(nuevo.Party);
        Modelo.Boxes.Clear(); Modelo.Boxes.AddRange(nuevo.Boxes);
        Modelo.Bag.Clear(); Modelo.Bag.AddRange(nuevo.Bag);
    }

    /// <summary>Convierte números de Ruby Marshal (int, long, BigInteger…) a long.</summary>
    private static long ALong(object? v) => v switch
    {
        null => 0,
        System.Numerics.BigInteger bi => (long)bi,
        byte b => b,
        sbyte sb => sb,
        short s => s,
        ushort us => us,
        int i => i,
        uint ui => ui,
        long l => l,
        ulong ul => (long)Math.Min(ul, long.MaxValue),
        double d => (long)d,
        float f => (long)f,
        _ => long.TryParse(v.ToString(), out var p) ? p : 0,
    };

    // ─────────────────────────── dinero ───────────────────────────

    public uint Dinero
    {
        get
        {
            var v = Modelo.RubyTrainer?.Get("@money");
            var n = ALong(v);
            return (uint)Math.Clamp(n, 0, 9_999_999);
        }
        set
        {
            var nuevo = Math.Clamp((long)value, 0, 9_999_999);
            _writer.SyncMoney(nuevo);
        }
    }

    // ─────────────────────────── nombres internos ───────────────────────────

    private static string Normaliza(string? s) => (s ?? "").ToUpperInvariant()
        .Replace(" ", "").Replace("-", "").Replace(".", "").Replace("'", "").Replace(":", "")
        .Replace("É", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U").Replace("Á", "A")
        .Replace("Ñ", "N").Replace("Ü", "U");

    private Dictionary<string, string>? _porNombre;
    private Dictionary<string, string>? _porInterno;

    private void ConstruirIndices()
    {
        if (_porNombre is not null) return;
        _porNombre = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _porInterno = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in Pbs.Species)
        {
            _porInterno[Normaliza(kv.Key)] = kv.Key;          // interno
            _porNombre[Normaliza(kv.Value)] = kv.Key;          // nombre visible del PBS
        }
    }

    /// <summary>Especie interna (símbolo) a partir del id nacional de la app o de su nombre.</summary>
    public string? ResolverEspecie(int speciesId, string? nombreApp)
    {
        ConstruirIndices();
        // 1) por nombre visible del PBS (el que manda la app, p. ej. "Eevee")
        if (!string.IsNullOrWhiteSpace(nombreApp) && _porNombre!.TryGetValue(Normaliza(nombreApp), out var porNombre))
            return porNombre;
        // 2) nombre inglés de PKHeX para ese id nacional
        try
        {
            var en = PKHeX.Core.GameInfo.GetStrings("en").Species;
            if (speciesId > 0 && speciesId < en.Count && _porNombre!.TryGetValue(Normaliza(en[speciesId]), out var porDex))
                return porDex;
        }
        catch { }
        // 3) el propio nombre como interno
        if (!string.IsNullOrWhiteSpace(nombreApp) && _porInterno!.TryGetValue(Normaliza(nombreApp), out var interno))
            return interno;
        return null;
    }

    public string? NombreVisible(string interno) => Pbs.Species.TryGetValue(interno, out var n) ? n : interno;

    public string? ResolverHabilidad(string? nombre) => ResolverDe(Pbs.Abilities, nombre);
    public string? ResolverMovimiento(string? nombre) => ResolverDe(Pbs.Moves, nombre);
    public string? ResolverObjeto(string? nombre) => ResolverDe(Pbs.Items, nombre);

    private static string? ResolverDe(Dictionary<string, string> tabla, string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var objetivo = Normaliza(nombre);
        foreach (var kv in tabla)
            if (Normaliza(kv.Key) == objetivo || Normaliza(kv.Value) == objetivo) return kv.Key;
        return null;
    }

    // ─────────────────────────── gachapón ───────────────────────────

    private static readonly Random Azar = new();

    /// <summary>
    /// Crea el Pokémon del gachapón: nivel 1, naturaleza/2 ataques/habilidad al azar del juego,
    /// IVs 0-31, shiny opcional y mote. Va al primer hueco libre del equipo; si no, a la caja.
    /// Devuelve la ubicación («equipo N» / «caja B · hueco S») o null con el motivo.
    /// </summary>
    public string? CrearGacha(int speciesId, string? nombreApp, bool shiny, string? mote, out string? nota)
    {
        nota = null;
        var interno = ResolverEspecie(speciesId, nombreApp);
        if (interno is null) { nota = $"no encuentro la especie #{speciesId} en el PBS del juego"; return null; }

        var esEquipo = Modelo.Party.Count < 6;
        var caja = -1; var slot = -1;
        if (!esEquipo)
        {
            for (var b = 0; b < Modelo.Boxes.Count && caja < 0; b++)
            {
                var slots = Modelo.Boxes[b].Slots;
                for (var s = 0; s < 30; s++)
                {
                    if (slots[s] is null) { caja = b; slot = s; break; }
                }
            }
            if (caja < 0) { nota = "no hay hueco libre ni en el equipo ni en las cajas"; return null; }
        }

        var visible = NombreVisible(interno);
        if (!_writer.AddPokemon(esEquipo, caja, interno, visible, 1, 0, "AUTO")) { nota = "el juego rechazó el Pokémon nuevo"; return null; }

        // AddPokemon escribe en el árbol Ruby; hay que volver a mapear para tener el objeto nuevo.
        Refrescar();

        // localizar el Pokémon recién creado
        Pokemon? p;
        int indiceEquipo = -1, cajaReal = caja, slotReal = slot;
        if (esEquipo)
        {
            indiceEquipo = Modelo.Party.Count - 1;   // el recién añadido va al final
            p = Modelo.Party[indiceEquipo];
        }
        else
        {
            p = Modelo.Boxes[caja].Slots[slot];
        }
        if (p is null) { nota = "no encuentro el Pokémon creado"; return null; }

        // valores aleatorios del juego
        var ivs = Enumerable.Range(0, 6).Select(_ => Azar.Next(0, 32)).ToArray();
        p.IVs = ivs;
        p.EVs = new int[6];
        p.Level = 1;
        var naturaleza = Naturalezas().OrderBy(_ => Azar.Next()).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(naturaleza)) { p.InternalNature = naturaleza; p.Nature = naturaleza; }
        var movimientos = MovimientosAleatorios(2);
        p.Moves.Clear();
        foreach (var mov in movimientos)
            p.Moves.Add(new PokemonMove { InternalName = mov, Name = Pbs.Moves.TryGetValue(mov, out var n) ? n : mov, PP = Pbs.MovePPs.TryGetValue(mov, out var pp) ? pp : 5, PPUp = 0 });
        p.IsShiny = shiny;
        p.IsSuperShiny = false;
        if (!string.IsNullOrWhiteSpace(mote)) p.Nickname = mote!.Trim();
        // habilidad: FULL RANDOM entre todas las del juego (el motor de Añil acepta símbolos)
        var habilidades = Pbs.Abilities.Keys.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (habilidades.Count > 0)
        {
            var hab = habilidades[Azar.Next(habilidades.Count)];
            p.InternalAbility = hab;
            p.Ability = Pbs.Abilities.TryGetValue(hab, out var nombreHab) ? nombreHab : hab;
        }
        else
        {
            p.InternalAbility = "AUTO";
            p.Ability = "Auto";
        }
        p.Happiness = 70;

        _writer.SyncPokemon(p, esEquipo, esEquipo ? -1 : cajaReal, esEquipo ? indiceEquipo : slotReal);
        return esEquipo ? $"equipo {indiceEquipo + 1}" : $"caja {cajaReal + 1} · hueco {slotReal + 1}";
    }

    private IEnumerable<string> Naturalezas()
        => Pbs.Natures.Count > 0 ? Pbs.Natures.Keys : new[] { "HARDY", "LONELY", "BRAVE", "ADAMANT", "NAUGHTY", "BOLD", "DOCILE", "RELAXED", "IMPISH", "LAX", "TIMID", "HASTY", "SERIOUS", "JOLLY", "NAIVE", "MODEST", "MILD", "QUIET", "BASHFUL", "RASH", "CALM", "GENTLE", "SASSY", "CAREFUL", "QUIRKY" };

    private List<string> MovimientosAleatorios(int cuantos)
    {
        var todos = Pbs.Moves.Keys.ToList();
        if (todos.Count == 0) return new List<string> { "TACKLE" };
        var elegidos = new List<string>();
        for (var i = 0; i < cuantos * 20 && elegidos.Count < cuantos; i++)
        {
            var m = todos[Azar.Next(todos.Count)];
            if (!elegidos.Contains(m)) elegidos.Add(m);
        }
        return elegidos;
    }

    // ─────────────────────────── edición de un Pokémon ───────────────────────────

    /// <summary>Ubicación de un Pokémon del modelo: equipo o caja/hueco (0-based).</summary>
    public static (bool EsEquipo, int Caja, int Slot) Ubicar(PartidaAnil partida, Pokemon p)
    {
        for (var i = 0; i < partida.Modelo.Party.Count; i++)
            if (ReferenceEquals(partida.Modelo.Party[i], p)) return (true, -1, i);
        for (var b = 0; b < partida.Modelo.Boxes.Count; b++)
        {
            var slots = partida.Modelo.Boxes[b].Slots;
            for (var s = 0; s < slots.Length; s++)
                if (ReferenceEquals(slots[s], p)) return (false, b, s);
        }
        return (false, -1, -1);
    }

    public void Sincronizar(Pokemon p)
    {
        var (esEquipo, caja, slot) = Ubicar(this, p);
        if (slot < 0) return;
        _writer.SyncPokemon(p, esEquipo, caja, slot);
    }

    public bool PonerIvsYevs(Pokemon p, int[] ivs, int[] evs)
    {
        p.IVs = ivs;
        p.EVs = evs;
        Sincronizar(p);
        return p.IVs.SequenceEqual(ivs) && p.EVs.SequenceEqual(evs);
    }

    public bool PonerHabilidad(Pokemon p, string? nombreHabilidad)
    {
        var interno = ResolverHabilidad(nombreHabilidad);
        if (interno is null) return false;
        p.InternalAbility = interno;
        p.Ability = nombreHabilidad!;
        Sincronizar(p);
        return string.Equals(p.InternalAbility, interno, StringComparison.OrdinalIgnoreCase);
    }

    public bool EnsenarMovimiento(Pokemon p, int indice, string? nombreMovimiento)
    {
        var interno = ResolverMovimiento(nombreMovimiento);
        if (interno is null || indice is < 0 or > 3) return false;
        while (p.Moves.Count <= indice) p.Moves.Add(new PokemonMove { InternalName = "TACKLE", Name = "Placaje", PP = 35 });
        p.Moves[indice].InternalName = interno;
        p.Moves[indice].Name = nombreMovimiento!;
        p.Moves[indice].PP = Pbs.MovePPs.TryGetValue(interno, out var pp) ? pp : 5;
        Sincronizar(p);
        return string.Equals(p.Moves[indice].InternalName, interno, StringComparison.OrdinalIgnoreCase);
    }

    public bool CambiarNaturaleza(Pokemon p, string? naturaleza)
    {
        var nat = (naturaleza ?? "").ToUpperInvariant();
        if (nat.Length == 0) return false;
        p.InternalNature = nat;
        p.Nature = nat;
        Sincronizar(p);
        return string.Equals(p.InternalNature, nat, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Revisa el estado y deja el Pokémon curado (HP al máximo). Devuelve si cambió algo.</summary>
    public bool Curar(Pokemon p)
    {
        if (p.RubyObjectRef is not RubyObject ro) return false;
        var total = ALong(ro.Get("@totalhp") ?? ro.Get("@total_hp") ?? ro.Get("@hp"));
        if (total <= 0) return false;
        ro.Set("@hp", total);
        ro.Set("@status", null);
        if (ro.Attributes.ContainsKey("@status_count")) ro.Set("@status_count", 0);
        return true;
    }

    public bool PonerShiny(Pokemon p, bool shiny)
    {
        p.IsShiny = shiny;
        if (!shiny) p.IsSuperShiny = false;
        Sincronizar(p);
        return p.IsShiny == shiny;
    }

    // ─────────────────────────── mochila ───────────────────────────

    /// <summary>Añade un objeto a la mochila (respetando el bolsillo del PBS). Devuelve cuántos puso.</summary>
    public int AnadirObjeto(string? nombreObjeto, int cantidad)
    {
        var interno = ResolverObjeto(nombreObjeto);
        if (interno is null) return -1;
        var bolsillo = AjustarBolsillo(Pbs.ItemPockets.TryGetValue(interno, out var pk) ? pk : 1);
        var existente = Modelo.Bag.FirstOrDefault(i => string.Equals(i.InternalName, interno, StringComparison.OrdinalIgnoreCase));
        if (existente is not null)
            existente.Quantity += Math.Max(0, cantidad);
        else
            Modelo.Bag.Add(new ItemSlot { InternalName = interno, Name = nombreObjeto!, Quantity = Math.Max(1, cantidad), Pocket = bolsillo });
        _writer.SyncBag(Modelo.Bag);
        return Math.Max(0, cantidad);
    }

    /// <summary>El PBS puede dar un bolsillo que la partida no tenga: se recorta al existente.</summary>
    private int AjustarBolsillo(int bolsillo)
    {
        try
        {
            if (Modelo.RubyBag?.Get("@pockets") is System.Collections.IList pockets && pockets.Count > 1)
                return Math.Clamp(bolsillo, 1, pockets.Count - 1);
        }
        catch { }
        return Math.Max(1, bolsillo);
    }

    /// <summary>Desbloquea todas las MT/MO del juego (como el botón de AnilHeX). Devuelve cuántas.</summary>
    public int DesbloquearMts()
    {
        var movidos = 0;
        foreach (var kv in Pbs.Items)
        {
            var esMt = kv.Key.StartsWith("TM", StringComparison.OrdinalIgnoreCase) || kv.Key.StartsWith("HM", StringComparison.OrdinalIgnoreCase);
            if (!esMt) continue;
            if (Modelo.Bag.Any(i => string.Equals(i.InternalName, kv.Key, StringComparison.OrdinalIgnoreCase))) continue;
            var bolsillo = AjustarBolsillo(Pbs.ItemPockets.TryGetValue(kv.Key, out var pk) ? pk : 4);
            Modelo.Bag.Add(new ItemSlot { InternalName = kv.Key, Name = kv.Value, Quantity = 1, Pocket = bolsillo });
            movidos++;
        }
        _writer.SyncBag(Modelo.Bag);
        return movidos;
    }
}
