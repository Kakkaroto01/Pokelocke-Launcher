using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PKHeX.Core;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Inyector del equipo de PokeLocke Android en la partida guardada (.sav) del emulador:
/// lee <c>/lockes/{lockeId}/players/{tag}/myTeam</c> (Firebase) o un JSON equivalente,
/// construye los PKM con PKHeX (especie, nivel, naturaleza, habilidad, movimientos en
/// español, IVs, EVs, shiny) y los escribe en el equipo de la partida.
/// </summary>
public static class InyectorEquipo
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Construye un PKM del formato de la partida a partir de un slot de la app.</summary>
    public static PKM ConstruirPkm(SaveFile sav, MonEquipo slot, GameStrings strings)
    {
        var pkm = sav.BlankPKM;
        pkm.Version = sav.Version;   // imprescindible: con Version=Any, GetRandomPID ignora naturaleza/habilidad (visto en EntityPID)
        pkm.Species = (ushort)Math.Clamp(slot.Id, 1, sav.MaxSpeciesID);
        pkm.CurrentLevel = (byte)Math.Clamp(slot.Level, 1, 100);
        pkm.Stat_Level = pkm.CurrentLevel;

        // Naturaleza: en gen 3-5 se deriva del PID (PID % 25), así que se re-lanza el PID con
        // SetPIDNature (que conserva la habilidad vía el hint del PID, como el original).
        // Se hace DESPUÉS de fijar la habilidad, y SetShiny (que preserva naturaleza y
        // habilidad, según el decompilado) va al final.
        var naturalezaOk = Enum.TryParse(slot.Nature, ignoreCase: true, out Nature naturaleza);

        // IVs y EVs (mapas hp/atk/def/spa/spd/spe del contrato de la app)
        AplicarIvs(pkm, slot.Ivs);
        AplicarEvs(pkm, slot.Evs);

        // Movimientos por nombre en español → id (reverse lookup con GameStrings)
        var ids = slot.Moves
            .Select(nombre => BuscarIndice(strings.Move, nombre))
            .Where(id => id > 0)
            .Take(4)
            .Select(id => (ushort)id)
            .ToArray();
        if (ids.Length > 0)
            pkm.SetMoves(ids);

        // Habilidad por nombre en español → id → slot según generación
        var idHabilidad = BuscarIndice(strings.Ability, slot.Ability);
        if (idHabilidad > 0)
            pkm.SetAbility(idHabilidad);

        if (naturalezaOk)
        {
            if (pkm.Format is >= 3 and <= 5)
                pkm.SetPIDNature(naturaleza);
            else
                pkm.Nature = naturaleza;
        }

        // Variocolor: re-lanza el PID conservando naturaleza y habilidad
        if (slot.IsShiny)
            pkm.SetShiny();

        // HP del estado de party al máximo (mon sano al inyectar)
        pkm.Stat_HPCurrent = pkm.Stat_HPMax > 0 ? pkm.Stat_HPMax : pkm.Stat_HPCurrent;
        pkm.RefreshChecksum();
        return pkm;
    }

    /// <summary>
    /// Inyecta el equipo de un jugador en la partida y escribe el .sav. Devuelve los PKM
    /// inyectados (en el orden de myTeam.slots). Las especies fuera de la generación se
    /// omiten (el .sav no puede contenerlas).
    /// </summary>
    public static List<PKM> Inyectar(SaveFile sav, PlayerLocke jugador, string idioma = "es")
    {
        var strings = GameInfo.GetStrings(idioma);
        var slots = jugador.MyTeam.Slots.Take(6).ToList();
        var equipo = new List<PKM>();
        foreach (var slot in slots)
        {
            if (slot.Id is < 1 or > 1025) continue;
            if (slot.Id > sav.MaxSpeciesID) continue;   // especie no existe en esta generación
            equipo.Add(ConstruirPkm(sav, slot, strings));
        }
        if (equipo.Count > 0)
            sav.PartyData = equipo;
        return equipo;
    }

    /// <summary>Carga el JSON de un locke desde archivo (el mismo esquema de Firebase).</summary>
    public static Locke CargarLockeDesdeJson(string rutaJson)
    {
        var locke = JsonSerializer.Deserialize<Locke>(File.ReadAllText(rutaJson), Json)
            ?? throw new InvalidOperationException($"No se pudo leer el locke de {rutaJson}");
        return locke;
    }

    /// <summary>
    /// Serializa el .sav con el pipeline de exportación de PKHeX: <c>Write()</c> recalcula los
    /// checksums (<c>SetChecksums</c>) y finaliza la metadata (<c>Metadata.Finalize</c>),
    /// conservando cabeceras/pies del formato original (DeSmuME, 3DS…).
    /// Escribir <c>sav.Data</c> a pelo deja los checksums INVÁLIDOS y el juego trataría la
    /// partida como dañada.
    /// </summary>
    public static byte[] SerializarSave(SaveFile sav, BinaryExportSetting modo = BinaryExportSetting.None)
        => sav.Write(modo).ToArray();

    /// <summary>Escribe el .sav modificado a disco con checksums válidos (copia del original por parte del llamador).</summary>
    public static void GuardarSave(SaveFile sav, string rutaDestino)
        => File.WriteAllBytes(rutaDestino, SerializarSave(sav));

    /// <summary>Construye un PKM para el Gachapón (v1.1): especie con presencia en el juego,
    /// versión CONCRETA, PID/EC según generación respetando naturaleza y shiny, IVs aleatorios,
    /// naturaleza y habilidad válidas, bola aplicada, OT/TID/SID/género/idioma del entrenador del
    /// save, EXP para el nivel, movimientos legales por nivel y stats de equipo recalculadas.
    /// Devuelve también una nota si algo se adaptó (bola no soportada…).</summary>
    public static (PKM Pkm, string? Nota) ConstruirPkmGacha(SaveFile sav, int speciesId, bool isShiny, Ball ball, string? mote)
    {
        var nota = new List<string>();
        var random = new Random();

        // Presencia real en el juego (no basta MaxSpeciesID)
        if (!sav.Personal.IsPresentInGame((ushort)speciesId, 0))
            throw new InvalidOperationException($"La especie #{speciesId} no está presente en {sav.Version}.");

        var pkm = sav.BlankPKM;

        // Versión CONCRETA: si el save devuelve una combinada (DP/RS/HGSS…), elegir una de sus dos
        var version = sav.Version;
        if (version == GameVersion.DP) version = random.Next(2) == 0 ? GameVersion.D : GameVersion.P;
        else if (version == GameVersion.RS) version = random.Next(2) == 0 ? GameVersion.R : GameVersion.S;
        else if (version == GameVersion.HGSS) version = random.Next(2) == 0 ? GameVersion.HG : GameVersion.SS;
        else if (version == GameVersion.FRLG) version = random.Next(2) == 0 ? GameVersion.FR : GameVersion.LG;
        else if (version == GameVersion.BW) version = random.Next(2) == 0 ? GameVersion.B : GameVersion.W;
        else if (version == GameVersion.B2W2) version = random.Next(2) == 0 ? GameVersion.B2 : GameVersion.W2;
        else if (version == GameVersion.XY) version = random.Next(2) == 0 ? GameVersion.X : GameVersion.Y;
        else if (version == GameVersion.SM) version = random.Next(2) == 0 ? GameVersion.SN : GameVersion.MN;
        else if (version == GameVersion.USUM) version = random.Next(2) == 0 ? GameVersion.US : GameVersion.UM;
        else if (version == GameVersion.SWSH) version = random.Next(2) == 0 ? GameVersion.SW : GameVersion.SH;
        else if (version == GameVersion.BDSP) version = random.Next(2) == 0 ? GameVersion.BD : GameVersion.SP;
        else if (version == GameVersion.SV) version = random.Next(2) == 0 ? GameVersion.SL : GameVersion.VL;
        else if (version == GameVersion.ORAS) version = random.Next(2) == 0 ? GameVersion.OR : GameVersion.AS;
        pkm.Version = version;

        var personal = sav.Personal.GetFormEntry((ushort)speciesId, 0);
        pkm.Species = (ushort)speciesId;
        pkm.Form = 0;   // forma por defecto

        // Nivel 1 SIEMPRE (regla del gachapón): EXP acorde al crecimiento de la especie.
        const int nivel = 1;
        pkm.CurrentLevel = nivel;
        pkm.Stat_Level = nivel;
        pkm.EXP = Experience.GetEXP(nivel, (byte)personal.EXPGrowth);

        // Naturaleza aleatoria válida (0..24; en gen 3-5 va dentro del PID)
        var naturaleza = (Nature)random.Next(25);

        // Género válido para la especie
        var genero = ElegirGenero(random, personal.Gender);

        // FULL RANDOM (randomlocke): habilidad cualquiera del juego, no solo las de la especie.
        var idHabilidad = random.Next(1, Math.Max(2, pkm.MaxAbilityID + 1));
        EfectosCartas.PonerHabilidadLibre(pkm, idHabilidad);

        // PID: en gen 3-5 la naturaleza y el género van dentro del PID.
        // ¡OJO! oldPID debe ser coherente con el género pedido o GetRandomPID entra en bucle infinito
        // (usa el bit de género del PID viejo como hint y lo exige también en el nuevo).
        var hintViejo = pkm.Format is >= 3 and <= 5
            ? (genero == EntityGender.Female ? (pkm.Format == 5 ? 0x10000u : 1u) : 0u)
            : 0u;
        pkm.PID = EntityPID.GetRandomPID(random, pkm.Species, genero, pkm.Version, naturaleza, pkm.Form, hintViejo);
        if (pkm.Format is >= 3 and <= 5)
        {
            pkm.Stat_Level = (byte)nivel;
        }
        else
        {
            pkm.Nature = naturaleza;
            pkm.Gender = genero;
        }

        // Entrenador del save: TID/SID ANTES de SetShiny (IsShiny = PID xor TID xor SID)
        pkm.OriginalTrainerName = sav.OT;
        pkm.TID16 = sav.TID16;
        pkm.SID16 = sav.SID16;
        try { pkm.Language = sav.Language; } catch { }

        // Shiny: re-lanza el PID conservando naturaleza/habilidad/género
        if (isShiny)
            pkm.SetShiny();

        // Bola (si el formato no la admite, la más cercana)
        var bolaAplicada = AplicarBola(pkm, ball);
        if (bolaAplicada != ball)
            nota.Add($"bola adaptada de {ball} a {bolaAplicada} (formato del juego)");

        // Datos de captura coherentes
        pkm.MetLevel = (byte)nivel;
        pkm.MetLocation = 0;   // «conocido en la ruta ?» — la legality lo avisa
        try { pkm.MetDate = DateOnly.FromDateTime(DateTime.Now); } catch { }

        // IVs aleatorios (no escondidos)
        pkm.IV_HP = random.Next(32);
        pkm.IV_ATK = random.Next(32);
        pkm.IV_DEF = random.Next(32);
        pkm.IV_SPA = random.Next(32);
        pkm.IV_SPD = random.Next(32);
        pkm.IV_SPE = random.Next(32);

        // Movimientos: 2 ATAQUES aleatorios que existan en esta generación.
        // Fuente: sugeridos legales de PKHeX (respeta especie/nivel/generación);
        // reserva: Placaje (33) si no hubiera ninguno.
        Span<ushort> sugeridos = stackalloc ushort[4];
        var la = new LegalityAnalysis(pkm);
        la.GetSuggestedCurrentMoves(sugeridos);
        // FULL RANDOM: 2 ataques cualesquiera que EXISTAN en esta generación (no hace falta
        // que la especie los aprenda: está pensado para randomlocke).
        var maxMov = pkm.MaxMoveID;
        var elegidos = new List<ushort>();
        for (var intento = 0; intento < 40 && elegidos.Count < 2; intento++)
        {
            var id = (ushort)random.Next(1, maxMov + 1);
            if (!elegidos.Contains(id)) elegidos.Add(id);
        }
        if (elegidos.Count == 0) elegidos.Add(33);   // Placaje si el rango fuese inválido
        pkm.SetMoves(elegidos.ToArray());

        // Mote elegido por el jugador (si lo hay); si no, el nombre de la especie.
        if (!string.IsNullOrWhiteSpace(mote))
        {
            pkm.Nickname = mote!.Trim();
            try { pkm.IsNicknamed = true; } catch { }
        }

        // Stats de equipo calculadas (HP actual = máximo; sin esto el mon «llega caído»)
        pkm.ResetPartyStats();
        pkm.RefreshChecksum();
        return (pkm, nota.Count > 0 ? string.Join("; ", nota) : null);
    }

    /// <summary>Aplica la bola al PKM respetando el máximo del formato. Devuelve la bola finalmente aplicada.</summary>
    private static Ball AplicarBola(PKM pkm, Ball pedida)
    {
        var id = (int)pedida;
        if (id < 0 || id > pkm.MaxBallID)
            id = (int)Ball.Poke;   // la más cercana universal
        try { pkm.Ball = (byte)id; return (Ball)id; }
        catch { pkm.Ball = (byte)Ball.Poke; return Ball.Poke; }
    }

    private static byte ElegirGenero(Random random, byte ratio)
    {
        if (ratio == byte.MaxValue) return EntityGender.Genderless;   // sin género
        if (ratio == 254) return EntityGender.Female;                 // solo hembra
        if (ratio == 0) return EntityGender.Male;                     // solo macho
        return random.Next(256) < ratio ? EntityGender.Female : EntityGender.Male;
    }

    // ─────────────────────────── internos ───────────────────────────

    private static void AplicarIvs(PKM pkm, Dictionary<string, int> ivs)
    {
        if (ivs.TryGetValue("hp", out var v)) pkm.IV_HP = Math.Clamp(v, 0, 31);
        if (ivs.TryGetValue("atk", out v)) pkm.IV_ATK = Math.Clamp(v, 0, 31);
        if (ivs.TryGetValue("def", out v)) pkm.IV_DEF = Math.Clamp(v, 0, 31);
        if (ivs.TryGetValue("spa", out v)) pkm.IV_SPA = Math.Clamp(v, 0, 31);
        if (ivs.TryGetValue("spd", out v)) pkm.IV_SPD = Math.Clamp(v, 0, 31);
        if (ivs.TryGetValue("spe", out v)) pkm.IV_SPE = Math.Clamp(v, 0, 31);
    }

    private static void AplicarEvs(PKM pkm, Dictionary<string, int> evs)
    {
        if (evs.TryGetValue("hp", out var v)) pkm.EV_HP = Math.Clamp(v, 0, 252);
        if (evs.TryGetValue("atk", out v)) pkm.EV_ATK = Math.Clamp(v, 0, 252);
        if (evs.TryGetValue("def", out v)) pkm.EV_DEF = Math.Clamp(v, 0, 252);
        if (evs.TryGetValue("spa", out v)) pkm.EV_SPA = Math.Clamp(v, 0, 252);
        if (evs.TryGetValue("spd", out v)) pkm.EV_SPD = Math.Clamp(v, 0, 252);
        if (evs.TryGetValue("spe", out v)) pkm.EV_SPE = Math.Clamp(v, 0, 252);
    }

    /// <summary>Reverse lookup: nombre localizado → índice (id) en la lista de GameStrings.</summary>
    public static int BuscarIndice(IEnumerable<string>? lista, string nombre)
    {
        if (lista is null || string.IsNullOrWhiteSpace(nombre)) return -1;
        var i = 0;
        foreach (var elemento in lista)
        {
            if (string.Equals(elemento, nombre, StringComparison.OrdinalIgnoreCase))
                return i;
            i++;
        }
        return -1;
    }
}
