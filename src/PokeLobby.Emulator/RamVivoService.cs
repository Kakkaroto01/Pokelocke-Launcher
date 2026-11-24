using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PokeLobby.Emulator;

/// <summary>Pokémon en vivo leído de la RAM (registro Vivo del original: Pid, Ec, Especie, Mote, Nivel, Hp, HpMax).</summary>
public sealed record Vivo(uint Pid, uint Ec, int Especie, string Mote, int Nivel, int Hp, int HpMax);

/// <summary>Perfil de direcciones del juego (ruta actual, flag de combate y mon rival) — solo Pokémon Y (000400000011C500).</summary>
public sealed record PerfilJuego(uint Ruta, uint EnCombate, uint Rival);

/// <summary>
/// Lectura en vivo del equipo desde la RAM del emulador (3DS) vía el stub GDB de Azahar.
/// Réplica fiel del <c>RamVivoService</c> original, verificada contra su código descompilado:
/// puerto 24689, entradas de equipo de 484 bytes (PKM[0..232] + estado + tráiler de 28 bytes en
/// 344), exploración del heap 3DS 0x08000000→0x0A000000 en páginas de 0x1004 buscando la
/// EncryptionConstant del primer Pokémon del .sav, validación de todo el equipo por EC,
/// decodificación PK6 con chequeos de checksum/especie/nivel/HP, detección de caídos y
/// encuentros (rival capturado/derrotado) con envío por lotes cada 5 s.
/// </summary>
public sealed class RamVivoService
{
    public const int PuertoGdb = 24689;
    public const int ObjStride = 484;                 // bytes por entrada de equipo en vivo
    public const int ObjStats = 344;                  // tráiler de 28 bytes del PK6 dentro de la entrada
    public const uint HeapIni = 0x08000000;
    public const uint HeapFin = 0x0A000000;
    public const int LecturaPagina = 0x1004;          // páginas de 4100 bytes
    public const int TamanoEquipo = 6 * ObjStride;    // 2904
    public const int ColaMaxima = 200;
    public const int IntervaloEnvioSegundos = 5;
    public const int FallosAntesDeRelocalizar = 5;

    /// <summary>Perfiles por TitleId (direcciones de ruta/combate/rival). Solo Pokémon Y de momento.</summary>
    public static readonly IReadOnlyDictionary<string, PerfilJuego> Perfiles =
        new Dictionary<string, PerfilJuego>(StringComparer.OrdinalIgnoreCase)
        {
            ["000400000011C500"] = new(Ruta: 148072308u, EnCombate: 147973104u, Rival: 142622448u),
        };

    private readonly Func<int, List<object>, Task<bool>> _enviarEventos;
    private readonly object _colaLock = new();
    private readonly List<object> _cola = [];
    private CancellationTokenSource? _cts;

    public RamVivoService(Func<int, List<object>, Task<bool>> enviarEventos)
        => _enviarEventos = enviarEventos;

    // ─────────────────────── estáticos puros (verificables sin emulador) ───────────────────────

    /// <summary>Decodifica una entrada de equipo en vivo de 484 bytes (PK6[0..232]=entrada[0..232], PK6[232..260]=entrada[344..372]).</summary>
    public static Vivo? Decodificar(byte[] entrada, int gen)
        => TryDecodificar(entrada, gen, out _);

    /// <summary>Como <see cref="Decodificar"/> pero reporta el motivo del rechazo (diagnóstico).</summary>
    public static Vivo? TryDecodificar(byte[] entrada, int gen, out string? motivo)
    {
        try
        {
            if (gen != 6 || entrada.Length < 372)
            {
                motivo = $"gen {gen} ≠ 6 o entrada de {entrada.Length} bytes (< 372)";
                return null;
            }
            var pkm6 = new byte[260];
            entrada.AsSpan(0, 232).CopyTo(pkm6);              // datos principales del PKM (la EC queda al inicio)
            entrada.AsSpan(ObjStats, 28).CopyTo(pkm6.AsSpan(232));   // tráiler de 28 bytes del PK6

            var pkm = new PK6(pkm6);
            if (!pkm.ChecksumValid) { motivo = "checksum inválido"; return null; }
            if (pkm.Species == 0 || pkm.Species > 721) { motivo = $"especie {pkm.Species} fuera de 1..721"; return null; }
            if (pkm.Stat_Level < 1 || pkm.Stat_Level > 100 || pkm.Stat_Level != pkm.CurrentLevel)
            {
                motivo = $"nivel almacenado {pkm.Stat_Level} vs real {pkm.CurrentLevel}";
                return null;
            }
            if (pkm.Stat_HPMax < 1 || pkm.Stat_HPMax > 999 || pkm.Stat_HPCurrent > pkm.Stat_HPMax)
            {
                motivo = $"HP {pkm.Stat_HPCurrent}/{pkm.Stat_HPMax} inválidas";
                return null;
            }
            motivo = null;
            return new Vivo(pkm.PID, pkm.EncryptionConstant, pkm.Species, pkm.Nickname, pkm.Stat_Level, pkm.Stat_HPCurrent, pkm.Stat_HPMax);
        }
        catch (Exception ex)
        {
            motivo = $"excepción: {ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// ¿Es este bloque el equipo en vivo? Cada entrada de 484 bytes debe empezar con la
    /// EncryptionConstant del Pokémon correspondiente del .sav, y la primera debe decodificar
    /// con la misma especie (EsEquipoVivo del original). <paramref name="bloque"/> debe medir
    /// 484 × ecs.Length bytes.
    /// </summary>
    public static bool EsEquipoVivo(byte[] bloque, uint[] ecs, PKM referencia)
    {
        if (bloque.Length < ObjStride * ecs.Length) return false;
        for (var i = 0; i < ecs.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bloque.AsSpan(i * ObjStride)) != ecs[i])
                return false;
        }
        var vivo = Decodificar(bloque.AsSpan(0, ObjStride).ToArray(), referencia.Format);
        return vivo is not null && vivo.Especie == referencia.Species;
    }

    /// <summary>Lee los 6 espacios del bloque de equipo (LeerEquipo: corta en el primer vacío o no decodificable).</summary>
    public static List<Vivo> LeerEquipo(byte[] bloque, int gen = 6)
    {
        var equipo = new List<Vivo>();
        if (bloque.Length < TamanoEquipo) return equipo;
        for (var i = 0; i < 6; i++)
        {
            var entrada = bloque.AsSpan(i * ObjStride, ObjStride).ToArray();
            if (BinaryPrimitives.ReadUInt32LittleEndian(entrada) == 0)
                break;
            var vivo = Decodificar(entrada, gen);
            if (vivo is null)
                break;
            equipo.Add(vivo);
        }
        return equipo;
    }

    /// <summary>
    /// Explora la RAM buscando el bloque del equipo en vivo (BuscarEquipo del original):
    /// aguja = 4 bytes de la EncryptionConstant del primer Pokémon del .sav, páginas de
    /// 0x1004 bytes, cada candidato se valida con EsEquipoVivo.
    /// </summary>
    public static uint? BuscarEquipo(GdbCliente gdb, IList<PKM> equipoGuardado, CancellationToken ct = default)
    {
        if (equipoGuardado.Count == 0) return null;
        var ecs = equipoGuardado.Select(p => p.EncryptionConstant).ToArray();
        var aguja = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(aguja, ecs[0]);

        for (var dir = HeapIni; dir < HeapFin; dir += 0x1000)
        {
            ct.ThrowIfCancellationRequested();
            byte[] pagina;
            try
            {
                pagina = gdb.LeerMemoria(dir, LecturaPagina);
            }
            catch (IOException)
            {
                continue;   // página no mapeada
            }
            var desde = 0;
            while (true)
            {
                var rel = pagina.AsSpan(desde).IndexOf(aguja);
                if (rel < 0 || desde + rel >= 0x1000)
                    break;
                var candidato = dir + (uint)(desde + rel);
                desde += rel + 4;
                if (EsEquipoVivoGdb(gdb, candidato, equipoGuardado, ecs))
                    return candidato;
            }
        }
        return null;
    }

    private static bool EsEquipoVivoGdb(GdbCliente gdb, uint candidato, IList<PKM> equipo, uint[] ecs)
    {
        byte[] bloque;
        try
        {
            bloque = gdb.LeerMemoria(candidato, ObjStride * ecs.Length);
        }
        catch (IOException)
        {
            return false;
        }
        return EsEquipoVivo(bloque, ecs, equipo[0]);
    }

    /// <summary>Lee el bloque de equipo en vivo desde la RAM (variante con GdbCliente).</summary>
    public static List<Vivo> LeerEquipo(GdbCliente gdb, uint direccion, int gen = 6)
        => LeerEquipo(gdb.LeerMemoria(direccion, TamanoEquipo), gen);

    /// <summary>Construye la entrada viva sintética inversa de Decodificar (para pruebas sin emulador).</summary>
    public static byte[] ConstruirEntradaSintetica(PKM pkm)
    {
        var entrada = new byte[ObjStride];
        var datos = pkm.Data;
        datos.Slice(0, 232).CopyTo(entrada.AsSpan(0));          // datos principales (EC incluida al inicio)
        datos.Slice(232, 28).CopyTo(entrada.AsSpan(ObjStats));  // tráiler
        return entrada;
    }

    /// <summary>
    /// Lee el TitleId de una ROM 3DS (NCCH: «Title ID» a 8 bytes en 0x110; en .3ds/.cci el
    /// NCCH empieza en 0x4000). Hex en mayúsculas, como lo usa RomIdentifierService.
    /// </summary>
    public static string? TitleId3ds(string rutaRom)
    {
        try
        {
            var extension = Path.GetExtension(rutaRom).ToLowerInvariant();
            var desplazamientoNcch = extension is ".3ds" or ".cci" ? 0x4000 : 0;
            using var fs = new FileStream(rutaRom, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(desplazamientoNcch + 0x110, SeekOrigin.Begin);
            var id = new byte[8];
            if (fs.Read(id) != 8) return null;
            return Convert.ToHexString(id);
        }
        catch
        {
            return null;
        }
    }

    // ─────────────────────── servicio (bucle de eventos en vivo) ───────────────────────

    public void Iniciar(string rutaRom, Func<string?> rutaSave, int salaId, string consola)
    {
        Detener();
        if (salaId != 0 && consola == "3DS")
        {
            var cts = new CancellationTokenSource();
            _cts = cts;
            _ = Task.Run(() => BucleAsync(rutaRom, rutaSave, salaId, cts.Token));
        }
    }

    public void Detener()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    private async Task BucleAsync(string rutaRom, Func<string?> rutaSave, int salaId, CancellationToken ct)
    {
        var titleId = TitleId3ds(rutaRom);
        Perfiles.TryGetValue(titleId ?? "", out var perfil);
        Console.Error.WriteLine($"[ramvivo] titleId {titleId}, perfil de ruta/combate: {(perfil is null ? "no (solo caídas)" : "sí")}");

        using var gdb = new GdbCliente();
        while (!gdb.Conectado)
        {
            ct.ThrowIfCancellationRequested();
            try { gdb.Conectar(puerto: PuertoGdb); }
            catch (IOException) { await Task.Delay(3000, ct); }
        }

        uint? baseEquipo = null;
        SaveFile? sav = null;
        var hpAnterior = new Dictionary<uint, int>();
        var fallos = 0;
        var enCombate = false;
        Vivo? rival = null;
        string? rutaActual = null;
        var ultimoEnvio = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            if (baseEquipo is null)
            {
                sav = CargarSave(rutaSave());
                if (sav is null || sav.Generation != 6)
                {
                    await Task.Delay(5000, ct);
                    continue;
                }
                baseEquipo = BuscarEquipo(gdb, sav.PartyData, ct);
                if (baseEquipo is null)
                {
                    await Task.Delay(15000, ct);
                    continue;
                }
                Console.Error.WriteLine($"[ramvivo] equipo vivo en 0x{baseEquipo:X8}");
                hpAnterior.Clear();
                fallos = 0;
            }

            var equipo = LeerEquipo(gdb, baseEquipo.Value, sav!.Generation);
            if (equipo.Count == 0)
            {
                fallos++;
                if (fallos >= FallosAntesDeRelocalizar)
                {
                    Console.Error.WriteLine("[ramvivo] el equipo ya no decodifica: se vuelve a localizar");
                    baseEquipo = null;
                }
            }
            else
            {
                fallos = 0;
                foreach (var miembro in equipo)
                {
                    if (hpAnterior.TryGetValue(miembro.Pid, out var hpPrevio) && hpPrevio > 0 && miembro.Hp == 0)
                    {
                        Encolar(new
                        {
                            tipo = "caido",
                            pid = (long)miembro.Pid,
                            especie = miembro.Especie,
                            mote = miembro.Mote,
                            nivel = miembro.Nivel,
                            ruta = rutaActual,
                            ocurridoEn = DateTime.UtcNow,
                        });
                    }
                    hpAnterior[miembro.Pid] = miembro.Hp;
                }
            }

            if (perfil is not null && baseEquipo is not null)
            {
                try
                {
                    var ruta = gdb.LeerMemoria(perfil.Ruta, 2);
                    var enCombateDatos = gdb.LeerMemoria(perfil.EnCombate, 4);
                    var ubicacion = BinaryPrimitives.ReadUInt16LittleEndian(ruta);
                    var nombreRuta = ubicacion == 0 ? null
                        : GameInfo.GetLocationName(isEggLocation: false, ubicacion, sav.Generation, sav.Generation, sav.Version);
                    if (!string.IsNullOrWhiteSpace(nombreRuta)) rutaActual = nombreRuta;

                    var enCombateAhora = BinaryPrimitives.ReadUInt32LittleEndian(enCombateDatos) == 0;
                    if (enCombateAhora && !enCombate)
                    {
                        rival = Decodificar(gdb.LeerMemoria(perfil.Rival, ObjStride), sav.Generation);
                    }
                    else if (!enCombateAhora && enCombate && rival is not null)
                    {
                        var capturado = equipo.Any(p => p.Ec == rival.Ec);
                        var vivoRival = Decodificar(gdb.LeerMemoria(perfil.Rival, ObjStride), sav.Generation);
                        var resultado = capturado ? "capturado"
                            : vivoRival is not null && vivoRival.Ec == rival.Ec && vivoRival.Hp == 0 ? "derrotado"
                            : "sin_determinar";
                        Encolar(new
                        {
                            tipo = "encuentro",
                            pid = (long)rival.Pid,
                            especie = rival.Especie,
                            nivel = rival.Nivel,
                            ruta = rutaActual,
                            ocurridoEn = DateTime.UtcNow,
                            detalle = new { resultado },
                        });
                        rival = null;
                    }
                    enCombate = enCombateAhora;
                }
                catch (IOException)
                {
                    perfil = null;   // direcciones no legibles: solo caídas a partir de ahora
                    Console.Error.WriteLine("[ramvivo] perfil: direcciones no legibles, se desactiva ruta/combate");
                }
            }

            if ((DateTime.UtcNow - ultimoEnvio).TotalSeconds >= IntervaloEnvioSegundos)
            {
                await EnviarAsync(salaId);
                ultimoEnvio = DateTime.UtcNow;
            }
            await Task.Delay(1000, ct);
        }
    }

    private void Encolar(object evento)
    {
        lock (_colaLock)
        {
            if (_cola.Count < ColaMaxima)
                _cola.Add(evento);
        }
    }

    private async Task EnviarAsync(int salaId)
    {
        List<object> lote;
        lock (_colaLock)
        {
            if (_cola.Count == 0) return;
            lote = _cola.ToList();
        }
        if (await _enviarEventos(salaId, lote))
        {
            lock (_colaLock)
            {
                _cola.RemoveRange(0, Math.Min(lote.Count, _cola.Count));
            }
        }
    }

    private static SaveFile? CargarSave(string? ruta)
    {
        try
        {
            if (string.IsNullOrEmpty(ruta) || !File.Exists(ruta)) return null;
            byte[] datos;
            using (var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                datos = new byte[fs.Length];
                fs.ReadExactly(datos);
            }
            return SaveUtil.TryGetSaveFile(datos, out var sav, ruta) && sav.PartyCount > 0 ? sav : null;
        }
        catch
        {
            return null;
        }
    }
}
