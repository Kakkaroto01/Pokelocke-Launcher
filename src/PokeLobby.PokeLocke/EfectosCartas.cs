using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PKHeX.Core;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Efectos de las cartas del juego (cartas 1..88).
/// Cada método es autónomo, no depende de estado externo salvo el <see cref="SaveFile"/>.
/// Se usan inmutables de PKHeX (copia, modifica, escribe).
/// </summary>
public static class EfectosCartas
{
    // IDs de carta usados por la cola de acciones.
    public const int CapsulaHabilidadDefinitiva = 63;
    public const int MaquinaMt = 65;
    public const int MtDivina = 66;
    public const int BanoDinero = 67;
    public const int PeonDeObra = 70;
    public const int MedicinaExperimental = 74;
    public const int JefeDeCuadrilla = 78;
    public const int Mentor = 79;
    public const int JefeDeObra = 81;
    public const int ModificacionGenetica = 82;
    public const int Zenkai = 83;
    public const int EjemploASeguir = 84;
    public const int SabotajeDoble = 87;
    public const int SabotajeCuadruple = 88;

    private static readonly string[] StatsValidas = { "hp", "atk", "def", "spa", "spd", "spe" };

    private static readonly Dictionary<string, int> Todos31 = StatsValidas.ToDictionary(s => s, _ => 31);
    private static readonly Dictionary<string, int> Ceros = StatsValidas.ToDictionary(s => s, _ => 0);

    /// <summary>Lista de stats válidas.</summary>
    public static IReadOnlyList<string> Stats => StatsValidas;

    /// <summary>ID de la habilidad actual del Pokémon (compatibilidad entre generaciones).</summary>
    public static int IdHabilidadActual(PKM pkm)
    {
        switch (pkm.Format)
        {
            case 4:
            case 5: return pkm.Data[0x15];
            case 6:
            case 7: return pkm.Data[0x14];
            case 8:
            case 9: return pkm.Data[0x14] | (pkm.Data[0x15] << 8);
            default: return pkm.Ability;
        }
    }

    /// <summary>Obtiene el PP máximo de un movimiento (busca en los datos del juego; fallback a 15).</summary>
    private static int MaxPPDeMovimiento(ushort idMovimiento)
    {
        if (idMovimiento == 0) return 0;
        try
        {
            var moveDataType = typeof(GameInfo).Assembly.GetType("PKHeX.Core.MoveData");
            if (moveDataType != null)
            {
                var getMoveData = moveDataType.GetMethod("GetMoveData", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
                if (getMoveData != null)
                {
                    var moveData = getMoveData.Invoke(null, new object[] { (int)idMovimiento });
                    if (moveData != null)
                    {
                        var ppProp = moveData.GetType().GetProperty("PP");
                        if (ppProp != null)
                        {
                            var pp = (int)ppProp.GetValue(moveData)!;
                            if (pp > 0) return pp;
                        }
                    }
                }
            }
        }
        catch { }
        // Fallback: valor conservador
        return 15;
    }

    /// <summary>Peón de obra (70): +128 EVs en la estadística elegida.</summary>
    public static void AnadirEvs(PKM pkm, string stat, int cantidad)
        => ModificarEv(pkm, stat, cantidad);

    /// <summary>Jefe de cuadrilla (78): +252 EVs en la estadística elegida.</summary>
    public static void MaxEvs(PKM pkm, string stat)
        => ModificarEv(pkm, stat, 252);

    /// <summary>Jefe de obra (81): aplica el reparto de EVs elegido por el jugador.</summary>
    public static void AplicarRepartoEvs(PKM pkm, Dictionary<string, int> reparto)
        => PonerIvsYevs(pkm, ivs: null, evs: reparto);

    /// <summary>Maximiza IVs (31) y reparte los EVs indicados (por defecto 252/252/4).</summary>
    public static void MaximizarIvsYevs(PKM pkm, Dictionary<string, int>? reparto = null)
        => PonerIvsYevs(pkm, Todos31, reparto ?? new Dictionary<string, int>
        {
            ["atk"] = 252,
            ["spe"] = 252,
            ["hp"] = 4,
        });

    /// <summary>Medicina experimental (74): con éxito pone IVs a 31; si falla los deja a 0.</summary>
    public static void Medicina(PKM pkm, bool exito, Dictionary<string, int>? evsAsignados = null)
        => PonerIvsYevs(pkm, exito ? Todos31 : Ceros, exito ? (evsAsignados ?? Ceros) : Ceros);

    /// <summary>Zenkai (83): revive y maximiza IVs/EVs de las dos stats elegidas.</summary>
    public static void AplicarZenkai(PKM pkm, IReadOnlyList<string> stats)
    {
        Revivir(pkm);
        if (stats.Count < 2)
            throw new ArgumentException("Zenkai necesita 2 stats (hp|atk|def|spa|spd|spe).");
        PonerIvsYevs(pkm,
            new Dictionary<string, int> { [stats[0]] = 31, [stats[1]] = 31 },
            new Dictionary<string, int> { [stats[0]] = 252, [stats[1]] = 252 });
    }

    /// <summary>Revive al Pokémon (PS y estado) y devuelve los avisos aplicables.</summary>
    public static string Revivir(PKM pkm)
    {
        var avisos = new List<string>();
        try
        {
            pkm.Heal();
        }
        catch
        {
            if (pkm.Stat_HPMax > 0) pkm.Stat_HPCurrent = pkm.Stat_HPMax;
            try { pkm.Status_Condition = 0; } catch { }
            avisos.Add("PP no restaurados en el revivir de emergencia");
        }
        pkm.RefreshChecksum();
        avisos.Add("el registro de caídos de la app/la partida (cementerio) no se modifica");
        return string.Join("; ", avisos);
    }

    /// <summary>Asigna una habilidad por ID.</summary>
    public static void AsignarHabilidad(PKM pkm, int idHabilidad)
        => pkm.SetAbility(idHabilidad);

    /// <summary>Asigna una habilidad por nombre (tabla de nombres indicada).</summary>
    public static void AsignarHabilidad(PKM pkm, string nombreHabilidad, IEnumerable<string> nombres)
    {
        var id = InyectorEquipo.BuscarIndice(nombres, nombreHabilidad);
        if (id > 0) pkm.SetAbility(id);
    }

    /// <summary>MT Divina (66): enseña cualquier movimiento (rellena el primer hueco, si no sustituye el 4.º).</summary>
    public static void EnsenarMovimiento(PKM pkm, ushort idMovimiento)
    {
        var movimientos = pkm.Moves.ToArray();
        var hueco = Array.IndexOf(movimientos, (ushort)0);
        if (hueco < 0) hueco = 3;
        movimientos[hueco] = idMovimiento;
        pkm.SetMoves(movimientos);
        // Cap PP of the new move to its max PP
        var maxPP = MaxPPDeMovimiento(idMovimiento);
        var prop = typeof(PKM).GetProperty($"Move{hueco + 1}_PP");
        if (prop != null && prop.CanWrite)
        {
            var currentPP = (int)prop.GetValue(pkm)!;
            if (currentPP > maxPP) prop.SetValue(pkm, maxPP);
        }
        pkm.RefreshChecksum();
    }

    /// <summary>Mentor (79): copia un movimiento de un Pokémon a otro (por índice de slot del equipo).</summary>
    public static void CopiarMovimiento(IList<PKM> equipo, int origen, int destino, int slotMovimiento)
    {
        if (origen >= equipo.Count || destino >= equipo.Count) return;
        var movimientosOrigen = equipo[origen].Moves.ToArray();
        if (slotMovimiento < 0 || slotMovimiento >= movimientosOrigen.Length) return;
        var id = movimientosOrigen[slotMovimiento];
        if (id == 0) return;
        EnsenarMovimiento(equipo[destino], id);
    }

    /// <summary>Modificación genética (82): intercambia las habilidades entre dos Pokémon.</summary>
    public static void IntercambiarHabilidades(IList<PKM> equipo, int a, int b)
    {
        if (a >= equipo.Count || b >= equipo.Count || a == b) return;
        var p1 = equipo[a];
        var p2 = equipo[b];
        var hab1 = IdHabilidadActual(p1);
        var hab2 = IdHabilidadActual(p2);
        if (hab1 > 0) p2.SetAbility(hab1);
        if (hab2 > 0) p1.SetAbility(hab2);
        p1.RefreshChecksum();
        p2.RefreshChecksum();
    }

    /// <summary>Baño de dinero (67): 999.999 en la partida.</summary>
    public static void DarDinero(SaveFile sav, int cantidad = 999_999)
        => sav.Money = (uint)Math.Clamp(cantidad, 0, (int)Math.Min(uint.MaxValue, sav.MaxMoney));

    /// <summary>Cultivos productivos (73): la menta de naturaleza correspondiente (cambia la naturaleza).</summary>
    public static bool DarMenta(PKM pkm, Nature naturaleza)
    {
        if (pkm.Nature == naturaleza) return true;
        try
        {
            // 1) Campo propio (gen 5 en adelante): asignación directa.
            pkm.Nature = naturaleza;
            pkm.RefreshChecksum();
            if (pkm.Nature == naturaleza) return true;

            // 2) SetNature (algunos formatos lo implementan).
            pkm.SetNature(naturaleza);
            pkm.RefreshChecksum();
            if (pkm.Nature == naturaleza) return true;

            // 3) gen 3-4: la naturaleza va en el PID; se regenera respetando género/especie/forma.
            var rnd = new Random(unchecked((int)(pkm.PID ^ (uint)pkm.Species)));
            for (var intento = 0; intento < 3000; intento++)
            {
                pkm.PID = EntityPID.GetRandomPID(rnd, pkm.Species, (byte)pkm.Gender, pkm.Version,
                    naturaleza, pkm.Form, pkm.PID);
                pkm.RefreshChecksum();
                if (pkm.Nature == naturaleza) return true;
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>Máquina MT (65): botón «Todos al máximo» del apartado MT de PKHeX.</summary>
    public static void DarTodasLasMt(PlayerBag bag)
    {
        foreach (var pouch in bag.Pouches)
        {
            if (!pouch.Type.ToString().Contains("TM", StringComparison.OrdinalIgnoreCase)) continue;
            try { pouch.GiveAllItems(bag, Math.Max(1, (int)pouch.MaxCount)); }
            catch { }
        }
    }

    public static void PonerIv31(PKM pkm, string stat)
    {
        switch (stat.ToLowerInvariant())
        {
            case "hp": pkm.IV_HP = 31; break;
            case "atk": pkm.IV_ATK = 31; break;
            case "def": pkm.IV_DEF = 31; break;
            case "spa": pkm.IV_SPA = 31; break;
            case "spd": pkm.IV_SPD = 31; break;
            case "spe": pkm.IV_SPE = 31; break;
        }
        pkm.RefreshChecksum();
    }

    /// <summary>
    /// Sabotaje (87/88, v1.1.b): IV de las stats indicadas a 0 en el Pokémon objetivo
    /// (puede ser del rival: la acción llega en la cola del afectado).
    /// </summary>
    public static void PonerIvsACero(PKM pkm, IReadOnlyList<string> stats)
    {
        foreach (var stat in stats)
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
        pkm.RefreshChecksum();
    }

    /// <summary>Lee el IV de una stat (para verificadores).</summary>
    public static int LeerIv(PKM pkm, string stat) => stat.ToLowerInvariant() switch
    {
        "hp" => pkm.IV_HP,
        "atk" => pkm.IV_ATK,
        "def" => pkm.IV_DEF,
        "spa" => pkm.IV_SPA,
        "spd" => pkm.IV_SPD,
        "spe" => pkm.IV_SPE,
        _ => -1,
    };

    /// <summary>Lee el EV de una stat (para verificadores).</summary>
    public static int LeerEv(PKM pkm, string stat) => stat.ToLowerInvariant() switch
    {
        "hp" => pkm.EV_HP,
        "atk" => pkm.EV_ATK,
        "def" => pkm.EV_DEF,
        "spa" => pkm.EV_SPA,
        "spd" => pkm.EV_SPD,
        "spe" => pkm.EV_SPE,
        _ => -1,
    };

    /// <summary>Stats válidas del contrato (hp|atk|def|spa|spd|spe).</summary>
    public static bool EsStatValida(string stat)
        => StatsValidas.Contains(stat.ToLowerInvariant());

    private static void ModificarEv(PKM pkm, string stat, int delta)
    {
        var actual = stat.ToLowerInvariant() switch
        {
            "hp" => pkm.EV_HP,
            "atk" => pkm.EV_ATK,
            "def" => pkm.EV_DEF,
            "spa" => pkm.EV_SPA,
            "spd" => pkm.EV_SPD,
            "spe" => pkm.EV_SPE,
            _ => throw new ArgumentException("stat desconocida: " + stat),
        };
        AsignarEv(pkm, stat, actual + delta);
    }

    private static void AsignarEv(PKM pkm, string stat, int valor)
    {
        var nuevo = Math.Clamp(valor, 0, 252);
        switch (stat.ToLowerInvariant())
        {
            case "hp": pkm.EV_HP = nuevo; break;
            case "atk": pkm.EV_ATK = nuevo; break;
            case "def": pkm.EV_DEF = nuevo; break;
            case "spa": pkm.EV_SPA = nuevo; break;
            case "spd": pkm.EV_SPD = nuevo; break;
            case "spe": pkm.EV_SPE = nuevo; break;
            default: throw new ArgumentException("stat desconocida: " + stat);
        }
        var total = pkm.EV_HP + pkm.EV_ATK + pkm.EV_DEF + pkm.EV_SPA + pkm.EV_SPD + pkm.EV_SPE;
        if (total > 510)
        {
            var exceso = total - 510;
            AsignarEvSinComprobar(pkm, stat, Math.Max(0, valor - exceso));
        }
        pkm.RefreshChecksum();
    }

    private static void AsignarEvSinComprobar(PKM pkm, string stat, int valor)
    {
        var nuevo = Math.Clamp(valor, 0, 252);
        switch (stat.ToLowerInvariant())
        {
            case "hp": pkm.EV_HP = nuevo; break;
            case "atk": pkm.EV_ATK = nuevo; break;
            case "def": pkm.EV_DEF = nuevo; break;
            case "spa": pkm.EV_SPA = nuevo; break;
            case "spd": pkm.EV_SPD = nuevo; break;
            case "spe": pkm.EV_SPE = nuevo; break;
        }
    }

    private static void PonerIvsYevs(PKM pkm, Dictionary<string, int>? ivs, Dictionary<string, int>? evs)
    {
        if (ivs != null)
        {
            if (ivs.TryGetValue("hp", out var v)) pkm.IV_HP = Math.Clamp(v, 0, 31);
            if (ivs.TryGetValue("atk", out v)) pkm.IV_ATK = Math.Clamp(v, 0, 31);
            if (ivs.TryGetValue("def", out v)) pkm.IV_DEF = Math.Clamp(v, 0, 31);
            if (ivs.TryGetValue("spa", out v)) pkm.IV_SPA = Math.Clamp(v, 0, 31);
            if (ivs.TryGetValue("spd", out v)) pkm.IV_SPD = Math.Clamp(v, 0, 31);
            if (ivs.TryGetValue("spe", out v)) pkm.IV_SPE = Math.Clamp(v, 0, 31);
        }
        if (evs != null)
        {
            pkm.EV_HP = 0;
            pkm.EV_ATK = 0;
            pkm.EV_DEF = 0;
            pkm.EV_SPA = 0;
            pkm.EV_SPD = 0;
            pkm.EV_SPE = 0;
            foreach (var (stat, valor) in evs)
                AsignarEv(pkm, stat, valor);
        }
        pkm.RefreshChecksum();
    }

    /// <summary>Enseña un movimiento en un hueco concreto (0..3) recalculando PP.</summary>
    public static void EnsenarMovimientoEn(PKM pkm, int indice, ushort movimiento)
    {
        if (indice is < 0 or > 3) return;
        pkm.SetMove(indice, movimiento);
        // Cap PP of the new move to its max PP
        var maxPP = MaxPPDeMovimiento(movimiento);
        var prop = typeof(PKM).GetProperty($"Move{indice + 1}_PP");
        if (prop != null && prop.CanWrite)
        {
            var currentPP = (int)prop.GetValue(pkm)!;
            if (currentPP > maxPP) prop.SetValue(pkm, maxPP);
        }
        pkm.RefreshChecksum();
    }

    /// <summary>Reparto libre de IVs (0..31) y EVs (0..252, total ≤ 510).</summary>
    public static void PonerIvsYevsLibre(PKM pkm, int[] ivs, int[] evs)
    {
        var st = StatsValidas;
        for (var i = 0; i < st.Length; i++)
        {
            var iv = i < ivs.Length ? Math.Clamp(ivs[i], 0, 31) : 0;
            switch (st[i])
            {
                case "hp": pkm.IV_HP = iv; break;
                case "atk": pkm.IV_ATK = iv; break;
                case "def": pkm.IV_DEF = iv; break;
                case "spa": pkm.IV_SPA = iv; break;
                case "spd": pkm.IV_SPD = iv; break;
                case "spe": pkm.IV_SPE = iv; break;
            }
        }
        for (var i = 0; i < st.Length; i++)
        {
            var ev = i < evs.Length ? Math.Clamp(evs[i], 0, 252) : 0;
            switch (st[i])
            {
                case "hp": pkm.EV_HP = ev; break;
                case "atk": pkm.EV_ATK = ev; break;
                case "def": pkm.EV_DEF = ev; break;
                case "spa": pkm.EV_SPA = ev; break;
                case "spd": pkm.EV_SPD = ev; break;
                case "spe": pkm.EV_SPE = ev; break;
            }
        }
        pkm.RefreshChecksum();
    }

    /// <summary>Convierte el nombre de una bola en el valor de PKHeX (por defecto Poké Ball).</summary>
    public static Ball BolaDe(string? nombre, out bool desconocida)
    {
        var texto = string.IsNullOrWhiteSpace(nombre) ? "POKEBALL" : nombre.Trim().ToUpperInvariant();
        desconocida = texto is not ("POKEBALL" or "SUPERBALL" or "LUJOBALL" or "ULTRABALL" or "MASTERBALL");
        return texto switch
        {
            "SUPERBALL" => Ball.Great,
            "LUJOBALL" => Ball.Luxury,
            "ULTRABALL" => Ball.Ultra,
            "MASTERBALL" => Ball.Master,
            _ => Ball.Poke,
        };
    }

    /// <summary>Pone los seis IVs a 31.</summary>
    public static void PonerTodosIvs31(PKM pkm)
    {
        foreach (var stat in StatsValidas)
            PonerIv31(pkm, stat);
    }

    /// <summary>Hace shiny al Pokémon.</summary>
    public static void PonerShiny(PKM pkm)
    {
        pkm.SetShiny();
        pkm.RefreshChecksum();
    }

    /// <summary>Quita el shiny regenerando el PID hasta que deje de serlo.</summary>
    public static bool QuitarShiny(PKM pkm)
    {
        if (!pkm.IsShiny) return true;
        var rnd = new Random(unchecked((int)(pkm.PID ^ (uint)(pkm.TID16 << 16) ^ pkm.SID16)));
        for (var intento = 0; intento < 1000 && pkm.IsShiny; intento++)
        {
            pkm.PID = EntityPID.GetRandomPID(rnd, pkm.Species, pkm.Gender, pkm.Version,
                pkm.Nature, pkm.Form, pkm.PID);
        }
        pkm.RefreshChecksum();
        return !pkm.IsShiny;
    }

    /// <summary>
    /// Habilidades que la especie puede tener SEGÚN LA TABLA DEL JUEGO (no la lista global):
    /// evita ofrecer habilidades de generaciones posteriores que PKHeX rechazaría en silencio.
    /// </summary>
    public static List<(int Id, string Nombre)> HabilidadesLegales(PKM pkm, IReadOnlyList<string> nombres)
    {
        var lista = new List<(int, string)>();
        try
        {
            var personal = pkm.PersonalInfo;
            for (var i = 0; i < personal.AbilityCount; i++)
            {
                var id = personal.GetAbilityAtIndex(i);
                if (id <= 0 || id == 25 || lista.Any(x => x.Item1 == id)) continue;   // 25 = Superguarda (no usable)
                lista.Add((id, id < nombres.Count ? nombres[id] : "Habilidad " + id));
            }
        }
        catch { /* tabla no disponible */ }
        return lista;
    }

    /// <summary>¿La especie puede llevar esa habilidad en esta generación?</summary>
    public static bool PuedeTenerHabilidad(PKM pkm, int idHabilidad)
    {
        try { return pkm.PersonalInfo.GetIndexOfAbility(idHabilidad) >= 0; }
        catch { return false; }
    }

    /// <summary>
    /// Habilidades que EXISTEN en la generación del save: primero las propias de la especie
    /// (marcadas), después el resto hasta el máximo admitido por el formato del Pokémon.
    /// </summary>
    public static List<(int Id, string Nombre)> HabilidadesDisponibles(PKM pkm, IReadOnlyList<string> nombres)
    {
        var propias = HabilidadesLegales(pkm, nombres);
        var maximo = Math.Max(1, pkm.MaxAbilityID);
        var lista = new List<(int, string)>(propias);
        for (var id = 1; id <= maximo && id < nombres.Count; id++)
        {
            if (id == 25 || string.IsNullOrWhiteSpace(nombres[id])) continue;   // 25 = Superguarda
            if (lista.Any(x => x.Item1 == id)) continue;
            lista.Add((id, nombres[id]));
        }
        return lista;
    }

    /// <summary>
    /// FULL RANDOM: escribe CUALQUIER habilidad del juego, aunque la especie no la tenga.
    /// En gen 4 la habilidad se guarda como byte crudo en el PKM (0x15), igual que hace
    /// PKHeX Hax; <c>SetAbility</c> solo acepta las de la tabla vanilla, por eso se escribe directo.
    /// </summary>
    public static bool PonerHabilidadLibre(PKM pkm, int idHabilidad)
    {
        if (idHabilidad <= 0 || idHabilidad > pkm.MaxAbilityID) return false;
        try
        {
            switch (pkm.Format)
            {
                case 4:
                case 5:   // PK4/PK5: byte en 0x15
                    pkm.Data[0x15] = (byte)idHabilidad;
                    pkm.RefreshChecksum();
                    return pkm.Data[0x15] == idHabilidad;

                case 6:
                case 7:   // PK6/PK7: byte en 0x14 (número en 0x15)
                    pkm.Data[0x14] = (byte)idHabilidad;
                    if (pkm.Data[0x15] == 0) pkm.Data[0x15] = 1;
                    pkm.RefreshChecksum();
                    return pkm.Data[0x14] == idHabilidad;

                case 8:
                case 9:   // PK8/PK9: ushort en 0x14 (número en 0x16)
                    pkm.Data[0x14] = (byte)(idHabilidad & 0xFF);
                    pkm.Data[0x15] = (byte)((idHabilidad >> 8) & 0xFF);
                    if (pkm.Data[0x16] == 0) pkm.Data[0x16] = 1;
                    pkm.RefreshChecksum();
                    return (pkm.Data[0x14] | (pkm.Data[0x15] << 8)) == idHabilidad;

                default:  // PK3 y otros: solo lo que permita la tabla/PID
                    pkm.SetAbility(idHabilidad);
                    pkm.RefreshChecksum();
                    return pkm.Ability == idHabilidad;
            }
        }
        catch { return false; }
    }

    /// <summary>
    /// Mete un objeto en la mochila hasta <paramref name="cantidad"/> (respetando el máximo por
    /// hueco del juego). Devuelve cuántos se añadieron realmente.
    /// </summary>
    public static int AnadirObjetos(PlayerBag bag, int idObjeto, int cantidad)
    {
        var restante = Math.Max(0, cantidad);
        var total = 0;
        foreach (var pouch in bag.Pouches)
        {
            if (restante == 0) break;
            if (!pouch.Items.Any(i => i.Index == idObjeto)) continue;
            var tope = Math.Max(1, (int)pouch.MaxCount);
            foreach (var item in pouch.Items)
            {
                if (restante == 0) break;
                if (item.Index != idObjeto) continue;
                var hueco = Math.Max(0, tope - item.Count);
                if (hueco <= 0) continue;
                var meter = Math.Min(hueco, restante);
                item.Count += (ushort)meter;
                restante -= meter;
                total += meter;
            }
        }
        return total;
    }

    /// <summary>
    /// Da un objeto a la mochila buscándolo por su nombre en inglés de PKHeX (p. ej. "Rare Candy",
    /// "Heart Scale"). Usa <c>GiveItem</c> del propio PKHeX, que funciona aunque la partida no
    /// tuviera ya ese objeto (en X/Y el hueco de Carameloraro puede no existir todavía).
    /// Devuelve cuántas unidades se añadieron de verdad.
    /// </summary>
    public static int DarObjetoPorNombre(SaveFile sav, IReadOnlyList<string> nombres, int cantidad)
    {
        var tabla = GameInfo.GetStrings("en").Item;
        var id = -1;
        foreach (var nombre in nombres)
        {
            for (var i = 1; i < tabla.Count; i++)
                if (string.Equals(tabla[i], nombre, StringComparison.OrdinalIgnoreCase)) { id = i; break; }
            if (id > 0) break;
        }
        if (id <= 0) return 0;

        var bag = sav.Inventory;
        var total = 0;
        foreach (var pouch in bag.Pouches)
        {
            bool tiene;
            try { tiene = bag.Info.GetItems(pouch.Type).ToArray().Any(x => x == id); }
            catch { tiene = pouch.Items.Any(x => x.Index == id); }
            if (!tiene) continue;

            total = pouch.GiveItem(bag, (ushort)id, cantidad);
            if (total > 0) break;
        }
        bag.CopyTo(sav);
        return total;
    }

    /// <summary>Cápsula Habilidad (106): cambia la habilidad del Pokémon a una aleatoria.</summary>
    public static bool PonerHabilidadAleatoria(PKM pkm)
    {
        var nombres = GameInfo.GetStrings("es").Ability;
        var opciones = HabilidadesDisponibles(pkm, nombres);
        if (opciones.Count == 0) return false;
        var actual = pkm.Ability;
        var candidatas = opciones.Where(o => o.Id != actual).ToList();
        if (candidatas.Count == 0) return false;
        var elegida = candidatas[Random.Shared.Next(candidatas.Count)];
        return PonerHabilidadLibre(pkm, elegida.Id);
    }
}
