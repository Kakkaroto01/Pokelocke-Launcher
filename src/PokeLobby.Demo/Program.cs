// PokeLobby.Demo — verificación del cliente recreado de Pokelobby.
//
// Uso:
//   PokeLobby.Demo                                  → sonda inocua contra el servidor real (sin credenciales tuyas)
//   PokeLobby.Demo --watch <dir>                    → demuestra el vigilante de partidas sobre una carpeta
//   PokeLobby.Demo --resumen <save.sav>             → resumen PKHeX (JSON compatible con huellas.json) + Showdown por stderr
//   PokeLobby.Demo --emu-info [vendorDir]           → estado de los emuladores vendor + config de azahar + test de fusión INI
//   PokeLobby.Demo --gdb-test                       → test del cliente GDB contra un stub simulado (protocolo RSP)
//   PokeLobby.Demo --login <email>                  → login REAL (pide contraseña), lista tus salas y escucha cartas
//
// La URL del servidor se toma de la variable de entorno POKELOBBY_API
// (igual que el launcher original); por defecto apunta al servidor real de Pokelobby.

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PKHeX.Core;
using PokeLobby.Client;
using PokeLobby.Emulator;

if (args.Length >= 2 && args[0] == "--watch")
{
    WatchDemo(args[1]);
    return 0;
}

if (args.Length >= 2 && args[0] == "--resumen")
{
    return ResumenDemo(args[1]);
}

if (args.Length >= 1 && args[0] == "--emu-info")
{
    return EmuInfo(args.Length >= 2 ? args[1] : null);
}

if (args.Length >= 1 && args[0] == "--gdb-test")
{
    return GdbTest();
}

if (args.Length >= 1 && args[0] == "--randomizer-info")
{
    return RandomizerInfo(args.Length >= 2 ? args[1] : null);
}

if (args.Length >= 3 && args[0] == "--cola-test")
{
    return await ColaTestDemo(args[1], args[2]);
}

if (args.Length >= 3 && args[0] == "--randomizar")
{
    return await RandomizarDemo(args[1], args[2], args.Length >= 4 ? args[3] : null, args.Length >= 5 ? args[4] : null);
}

if (args.Length >= 2 && args[0] == "--ramvivo-test")
{
    return RamVivoTest(args[1]);
}

if (args.Length >= 4 && args[0] == "--inyectar-equipo")
{
    return InyectarEquipoDemo(args[1], args[2], args[3]);
}

if (args.Length >= 2 && args[0] == "--pokenete-leer")
{
    return await PokeLockeLeer(args[1], args.Length >= 3 ? args[2] : null);
}

if (args.Length >= 2 && args[0] == "--login")
{
    return await LoginDemo(args[1]);
}

return await VerificacionInocua();

// ───────────────────────────── emuladores ─────────────────────────────

static int EmuInfo(string? vendorDir)
{
    var vendor = vendorDir ?? System.IO.Path.Combine("PokeLockeLauncher", "current", "vendor");
    var servicio = new EmulatorService(vendor);
    Console.WriteLine($"Carpeta vendor: {vendor} (existe: {Directory.Exists(vendor)})");
    foreach (var consola in Enum.GetValues<ConsolaJuego>())
    {
        if (consola == ConsolaJuego.Switch)
        {
            var externo = servicio.EmuladorSwitch();
            Console.WriteLine($"Switch (externo): {(externo is null ? "no configurado" : $"{externo.Ruta} args={externo.Argumentos}")}");
            continue;
        }
        var ruta = servicio.RutaExe(consola);
        Console.WriteLine($"{consola,-7}: {ruta} (existe: {File.Exists(ruta)})");
    }
    var abierto = EmulatorService.EmuladorAbierto();
    Console.WriteLine($"Emulador abierto ahora mismo: {(abierto is null ? "ninguno" : $"{abierto.ProcessName} (pid {abierto.Id})")}");

    var azahar = new AzaharConfig();
    Console.WriteLine($"\nConfig real de Azahar: {azahar.Ruta} (existe: {azahar.Existe}) — no se toca en este modo");
    if (azahar.Existe)
    {
        Console.WriteLine($"  use_gdbstub actual:        {azahar.Leer("[Debugging]", "use_gdbstub") ?? "-"}");
        Console.WriteLine($"  custom_textures actual:   {azahar.Leer("[Utility]", "custom_textures") ?? "-"}");
        Console.WriteLine($"  check_for_update actual:  {azahar.Leer("[Miscellaneous]", "check_for_update_on_start") ?? "-"}");
    }

    // TEST de fusión del INI sobre un archivo temporal (no toca la config real)
    var rutaTest = System.IO.Path.Combine(".rea-setup", "azahar-test", "qt-config.ini");
    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(rutaTest)!);
    File.WriteAllText(rutaTest, "[Utility]\ncustom_textures=false\notra_clave=42\n\n[Debugging]\n");
    var cfg = new AzaharConfig(rutaTest);
    cfg.AplicarAjustesLauncher();
    var comprobaciones = new (string Seccion, string Clave, string? Esperado)[]
    {
        ("[Debugging]", "use_gdbstub", "true"),
        ("[Debugging]", "use_gdbstub\\default", "false"),
        ("[Utility]", "custom_textures", "true"),
        ("[Utility]", "custom_textures\\default", "false"),
        ("[Utility]", "otra_clave", "42"),
        ("[Miscellaneous]", "check_for_update_on_start", "false"),
    };
    var fallos = 0;
    foreach (var (seccion, clave, esperado) in comprobaciones)
    {
        var actual = cfg.Leer(seccion, clave);
        var ok = actual == esperado;
        if (!ok) fallos++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {seccion} → {clave} = {actual ?? "-"} (esperado {esperado})");
    }
    Console.WriteLine(fallos == 0 ? "Fusión de qt-config.ini: 6/6 ✔" : $"Fusión de qt-config.ini: {fallos} fallos ✗");
    return fallos == 0 ? 0 : 1;
}

static int GdbTest()
{
    Console.WriteLine("Test del cliente GDB (Remote Serial Protocol) contra un stub simulado…");
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        var puerto = ((IPEndPoint)listener.LocalEndpoint!).Port;
        var memoria = new byte[0x10000];
        for (var i = 0; i < memoria.Length; i++)
            memoria[i] = (byte)(i * 7 + 3);

        _ = Task.Run(() => AtenderGdb(listener, memoria));

        var pasadas = 0;
        var fallos = 0;

        using var gdb = new GdbCliente();
        try
        {
            gdb.Conectar("127.0.0.1", puerto);
            Console.WriteLine("[PASS] conexión 127.0.0.1 + qSupported:multiprocess-;swbreak+ + «?» + «c»");
            pasadas++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] handshake: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        try
        {
            var leidos = gdb.LeerMemoria(0x1234, 8200);   // > 4096 → dos trozos
            var esperado = new byte[8200];
            Array.Copy(memoria, 0x1234, esperado, 0, 8200);
            var coincide = leidos.AsSpan().SequenceEqual(esperado);
            Console.WriteLine($"[{(coincide ? "PASS" : "FAIL")}] lectura de 8200 bytes en trozos de ≤4096 (m/x + checksum)");
            if (coincide) pasadas++; else fallos++;
        }
        catch (Exception ex)
        {
            fallos++;
            Console.WriteLine($"[FAIL] lectura larga: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var corta = gdb.LeerMemoria(0xFFF0, 16);
            var ok = corta.Length == 16 && corta[0] == memoria[0xFFF0];
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] lectura corta de 16 bytes");
            if (ok) pasadas++; else fallos++;
        }
        catch (Exception ex)
        {
            fallos++;
            Console.WriteLine($"[FAIL] lectura corta: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine($"\nCliente GDB: {pasadas}/{pasadas + fallos} pruebas superadas" + (fallos == 0 ? " ✔" : " ✗"));
        return fallos == 0 ? 0 : 1;
    }
    finally
    {
        listener.Stop();
    }
}

static void AtenderGdb(TcpListener listener, byte[] memoria)
{
    using var cliente = listener.AcceptTcpClient();
    var flujo = cliente.GetStream();
    while (LeerTramaGdb(flujo) is { } datos)
    {
        if (datos.StartsWith("qSupported", StringComparison.Ordinal))
        {
            EnviarTramaGdb(flujo, "PacketSize=20000");
        }
        else if (datos == "?")
        {
            EnviarTramaGdb(flujo, "S05");
        }
        else if (datos == "c")
        {
            // continuar: el stub solo respondería en la próxima parada
        }
        else if (datos.StartsWith('m') && datos.IndexOf(',') is var coma && coma > 1)
        {
            var dir = Convert.ToInt64(datos[1..coma], 16);
            var largo = Convert.ToInt32(datos[(coma + 1)..], 16);
            if (dir < 0 || largo < 0 || dir + largo > memoria.Length)
            {
                EnviarTramaGdb(flujo, "E01");
            }
            else
            {
                var sb = new StringBuilder(largo * 2);
                for (var i = 0; i < largo; i++)
                    sb.Append(memoria[dir + i].ToString("x2"));
                EnviarTramaGdb(flujo, sb.ToString());
            }
        }
        else
        {
            EnviarTramaGdb(flujo, "");
        }
    }
}

static string? LeerTramaGdb(NetworkStream flujo)
{
    try
    {
        int c;
        do
        {
            c = flujo.ReadByte();
        } while (c >= 0 && c != '$');
        if (c < 0) return null;
        var sb = new StringBuilder();
        while ((c = flujo.ReadByte()) >= 0 && c != '#')
            sb.Append((char)c);
        if (c < 0) return null;
        _ = flujo.ReadByte();
        _ = flujo.ReadByte();
        flujo.WriteByte((byte)'+');
        return sb.ToString();
    }
    catch (IOException)
    {
        return null;
    }
}

static void EnviarTramaGdb(NetworkStream flujo, string datos)
{
    var suma = 0;
    foreach (var ch in datos)
        suma += ch;
    flujo.Write(Encoding.ASCII.GetBytes("$" + datos + "#" + (suma & 0xFF).ToString("x2")));
}

// ───────────────────────────── randomizer ─────────────────────────────

static int RandomizerInfo(string? vendorDir)
{
    var vendor = vendorDir ?? System.IO.Path.Combine("PokeLockeLauncher", "current", "vendor");
    var servicio = new PokeLobby.Randomizer.RandomizerService(vendor);
    Console.WriteLine($"Carpeta vendor: {vendor}");
    Console.WriteLine($"JRE propio:     {servicio.RutaJreJava} (existe: {servicio.JreDisponible})");
    Console.WriteLine($"PokeRandoZX.jar: {File.Exists(servicio.RutaJar)}");
    Console.WriteLine($"RandoBridge:    {File.Exists(servicio.RutaRandoBridgeClass)}");
    Console.WriteLine($"Instalación completa: {servicio.InstalacionCompleta}");
    var presets = servicio.Presets();
    Console.WriteLine($"Presets:        {(presets.Length == 0 ? "(ninguno)" : string.Join(", ", presets))}");

    // Versión real del JRE del vendor (ejecutando su java.exe -version)
    if (servicio.JreDisponible)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(servicio.RutaJreJava)
            {
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            Console.WriteLine("JRE -version:   " + salida.Split('\n').FirstOrDefault(l => l.Contains("version", StringComparison.Ordinal))?.Trim());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JRE -version:   ERROR {ex.Message}");
        }
    }

    // TEST de ida y vuelta del formato .rnqs (properties)
    var fallos = 0;
    if (presets.Length > 0)
    {
        var original = System.IO.Path.Combine(servicio.DirRandomizer, "presets", presets[0] + ".rnqs");
        var ajustes = PokeLobby.Randomizer.RandomizerSettings.Cargar(original);
        var texto = ajustes.ToPropertiesText();
        var releido = PokeLobby.Randomizer.RandomizerSettings.Cargar(
            EscribirTemporal("roundtrip.rnqs", texto));
        var iguales = ajustes.Valores.Count == releido.Valores.Count
            && ajustes.Valores.All(p => releido.Get(p.Key) == p.Value);
        Console.WriteLine($"[{(iguales ? "PASS" : "FAIL")}] ida y vuelta .rnqs ({presets[0]}): {ajustes.Valores.Count} claves");
        if (!iguales) fallos++;

        ajustes.Set("types.mod", "UNCHANGED");
        ajustes.RaceMode = true;
        Console.WriteLine($"[{(ajustes.Get("types.mod") == "UNCHANGED" && ajustes.RaceMode ? "PASS" : "FAIL")}] setter tipado (types.mod + misc.raceMode)");
        if (!(ajustes.Get("types.mod") == "UNCHANGED" && ajustes.RaceMode)) fallos++;
    }
    else
    {
        fallos++;
    }
    return fallos == 0 ? 0 : 1;
}

static string EscribirTemporal(string nombre, string contenido)
{
    var dir = System.IO.Path.Combine(".rea-setup", "randomizer-test");
    Directory.CreateDirectory(dir);
    var ruta = System.IO.Path.Combine(dir, nombre);
    File.WriteAllText(ruta, contenido);
    return ruta;
}

static async Task<int> RandomizarDemo(string romEntrada, string preset, string? romSalida, string? vendorDir)
{
    var vendor = vendorDir ?? System.IO.Path.Combine("PokeLockeLauncher", "current", "vendor");
    var servicio = new PokeLobby.Randomizer.RandomizerService(vendor);
    romSalida ??= RandomizerDemo_RutaSalida(romEntrada);
    var ajustes = PokeLobby.Randomizer.RandomizerSettings.DesdePreset(servicio.DirRandomizer, preset);

    Console.WriteLine($"ROM entrada:   {romEntrada}");
    Console.WriteLine($"ROM salida:    {romSalida}");
    Console.WriteLine($"Preset:        {preset} ({ajustes.Valores.Count} claves)");
    Console.WriteLine($"Java:          {servicio.ResolverJava()}");
    Console.WriteLine("Lanzando el randomizador… (esto puede tardar varios minutos)\n");

    var reloj = System.Diagnostics.Stopwatch.StartNew();
    var resultado = await servicio.RandomizarAsync(romEntrada, romSalida, ajustes);
    reloj.Stop();

    Console.WriteLine($"\nexit={resultado.ExitCode} ({reloj.Elapsed.TotalSeconds:N0} s) → {resultado.Motivo}");
    var ultimas = resultado.Salida.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var linea in ultimas.TakeLast(6))
        Console.WriteLine("  | " + linea);
    if (resultado.SalidaError.Length > 0)
        foreach (var linea in resultado.SalidaError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(4))
            Console.WriteLine("  ! " + linea);
    Console.WriteLine(resultado.Ok ? "RANDOMIZACIÓN OK ✔" : "RANDOMIZACIÓN FALLÓ ✗");
    return resultado.Ok ? 0 : 1;
}

static string RandomizerDemo_RutaSalida(string romEntrada)
{
    var dir = System.IO.Path.Combine(".rea-setup", "randomizer-test");
    Directory.CreateDirectory(dir);
    return System.IO.Path.Combine(dir, PokeLobby.Randomizer.RandomizerService.NombreRomSalida(romEntrada));
}

// ───────────────────────────── RAM-vivo ─────────────────────────────

static int RamVivoTest(string rutaSave)
{
    Console.WriteLine("Test de la RAM-vivo: decodificación reconstruida con un Pokémon REAL de tu partida…");
    var sav = PokeLobby.Watcher.ResumenPartidaService.Abrir(rutaSave);
    var original = sav.PartyData[0]
        ?? throw new InvalidOperationException("El .sav no tiene equipo.");
    Console.WriteLine($"Pokémon de referencia: {original.Nickname} ({original.Species}) nivel {original.Stat_Level} PID {original.PID}");

    // La RAM-vivo vive en 3DS (PK6): convertimos el Pokémon del .sav a PK6 como estaría en la RAM.
    PK6 pk6;
    var convertido = EntityConverter.ConvertToType(original, typeof(PK6), out _);
    if (convertido is PK6 ok)
    {
        pk6 = ok;
        // La conversión puede dejar el byte de nivel almacenado desincronizado (0) respecto al
        // nivel real derivado del EXP: en RAM ambos son coherentes, así que los sincronizamos.
        pk6.Stat_Level = pk6.CurrentLevel == 0 ? (byte)1 : pk6.CurrentLevel;
        // El HP actual/máx vive en el tráiler de party (28 bytes) que la RAM real mantiene
        // actualizado: lo poblamos con la fórmula estándar de HP y sus IVs/EVs reales.
        var hpMaxima = (pk6.PersonalInfo.HP * 2 + pk6.IV_HP + pk6.EV_HP / 4) * pk6.Stat_Level / 100 + pk6.Stat_Level + 10;
        pk6.Stat_HPMax = hpMaxima;
        pk6.Stat_HPCurrent = hpMaxima;
        pk6.RefreshChecksum();
    }
    else
    {
        Console.WriteLine("EntityConverter no pudo convertir; construyendo PK6 manual para el test…");
        pk6 = new PK6
        {
            Species = original.Species,
            PID = original.PID,
            EncryptionConstant = original.EncryptionConstant,
            Nickname = original.Nickname,
            CurrentLevel = original.Stat_Level,
            Stat_Level = original.Stat_Level,
        };
        pk6.RefreshChecksum();
    }
    Console.WriteLine($"Convertido a PK6: especie {pk6.Species}, nivel {pk6.Stat_Level}/{pk6.CurrentLevel}, checksum válido: {pk6.ChecksumValid}");

    int pasadas = 0, fallos = 0;
    void Comprobar(string nombre, bool ok)
    {
        if (ok) pasadas++; else fallos++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {nombre}");
    }

    // 1) Construir la entrada viva sintética y decodificarla (ruta inversa de Decodificar)
    var entrada = PokeLobby.Emulator.RamVivoService.ConstruirEntradaSintetica(pk6);
    var vivo = PokeLobby.Emulator.RamVivoService.TryDecodificar(entrada, gen: 6, out var motivo);
    Comprobar($"Decodificar(entrada sintética) ≠ null {(vivo is null ? $"— motivo: {motivo}" : "")}", vivo is not null);
    if (vivo is not null)
    {
        Comprobar($"PID conservado ({vivo.Pid} == {pk6.PID})", vivo.Pid == pk6.PID);
        Comprobar($"EC conservada ({vivo.Ec} == {pk6.EncryptionConstant})", vivo.Ec == pk6.EncryptionConstant);
        Comprobar($"especie conservada ({vivo.Especie} == {pk6.Species})", vivo.Especie == pk6.Species);
        Comprobar($"nivel válido ({vivo.Nivel})", vivo.Nivel is >= 1 and <= 100 && vivo.Nivel == pk6.Stat_Level);
        Comprobar($"HP coherentes ({vivo.Hp}/{vivo.HpMax})", vivo.Hp >= 0 && vivo.Hp <= vivo.HpMax && vivo.HpMax <= 999);
        Comprobar($"mote conservado («{vivo.Mote}»)", vivo.Mote == pk6.Nickname);
    }

    // 2) EsEquipoVivo: el bloque debe empezar por la EC de cada Pokémon del .sav
    var ecs = new uint[] { pk6.EncryptionConstant };
    var bloqueEquipo = new byte[PokeLobby.Emulator.RamVivoService.ObjStride];
    Array.Copy(entrada, bloqueEquipo, entrada.Length);
    Comprobar("EsEquipoVivo con EC correcta → true",
        PokeLobby.Emulator.RamVivoService.EsEquipoVivo(bloqueEquipo, ecs, pk6));
    Comprobar("EsEquipoVivo con EC falsa → false",
        !PokeLobby.Emulator.RamVivoService.EsEquipoVivo(bloqueEquipo, [pk6.EncryptionConstant + 1], pk6));

    // 3) LeerEquipo sobre un bloque de 2904 bytes: 1 Pokémon en el espacio 0, el resto vacíos
    var bloque = new byte[PokeLobby.Emulator.RamVivoService.TamanoEquipo];
    Array.Copy(entrada, bloque, entrada.Length);
    var equipo = PokeLobby.Emulator.RamVivoService.LeerEquipo(bloque);
    Comprobar($"LeerEquipo → 1 Pokémon (obtenidos: {equipo.Count})", equipo.Count == 1 && equipo[0].Pid == pk6.PID);

    Console.WriteLine($"\nRAM-vivo: {pasadas}/{pasadas + fallos} comprobaciones superadas" + (fallos == 0 ? " ✔" : " ✗"));
    return fallos == 0 ? 0 : 1;
}

// ───────────────────────────── PokeLocke Android (Firebase) ─────────────────────────────

static async Task<int> ColaTestDemo(string rutaSave, string rutaAccionesJson)
{
    Console.WriteLine("Test de la cola launcherQueue — servicio REAL con Firebase simulado; verificación releyendo el .sav de disco…");

    // Copia del .sav (nunca destructiva)
    var dir = Path.Combine(AppContext.BaseDirectory, "cola-test");
    Directory.CreateDirectory(dir);
    var saveCopia = Path.Combine(dir, "save-cola-test.sav");
    File.Copy(rutaSave, saveCopia, overwrite: true);
    Console.WriteLine($"Copia del .sav: {saveCopia}");

    // Firebase simulado con el JSON tal cual (formato Android: appItemId número, params, isShiny…)
    var fake = new FirebaseSimulado(rutaAccionesJson);
    var datosDir = Path.Combine(dir, "datos");
    Directory.CreateDirectory(datosDir);

    // Servicio REAL con respuestas automáticas de los diálogos
    var servicio = new PokeLobby.PokeLocke.ColaLauncherService(fake, datosDir, emuladorAbierto: () => false)
    {
        AlProgresar = msg => Console.WriteLine($"  · {msg}"),
        PedirObjetivo = (titulo, objetivos) => objetivos.FirstOrDefault()?.Ubicacion,
        PedirStat = _ => "atk",
        PedirDosStats = _ => ["atk", "spe"],
        PedirMovimiento = (_, maxMov) => (ushort)Math.Min(85, (int)maxMov),
        PedirHabilidad = (_, opciones) => opciones.Count > 0 ? opciones[0].Id : null,
        PedirMovimientoReemplazo = (_, _) => 0,
        PedirMovimientoDeLista = (_, lista) => lista.Count > 0 ? lista[0] : null,
        PedirEdicionStats = (_, _, _) => (new[] { 31, 31, 31, 31, 31, 31 }, new[] { 4, 252, 0, 0, 0, 252 }),
        PedirMote = _ => null,
        PedirRepartoEvs = _ => new Dictionary<string, int> { ["atk"] = 252, ["spe"] = 252, ["hp"] = 6 },
        PedirNivel = _ => null,     // null = nivel medio del equipo
    };

    var resultados = await servicio.ProcesarColaAsync("locke-demo", "jugador_demo", saveCopia, CancellationToken.None);

    Console.WriteLine();
    int aplicadas = 0, fallidas = 0, omitidas = 0, pendientes = 0;
    foreach (var r in resultados)
    {
        Console.WriteLine($"  [{r.ActionId[..Math.Min(8, r.ActionId.Length)]}] {r.Estado}: {r.ResultMessage}"
            + (r.ErrorMessage is null ? "" : $" — {r.ErrorMessage}"));
        switch (r.Estado)
        {
            case PokeLobby.PokeLocke.AccionEstado.Aplicada: aplicadas++; break;
            case PokeLobby.PokeLocke.AccionEstado.Fallida: fallidas++; break;
            case PokeLobby.PokeLocke.AccionEstado.Omitida: omitidas++; break;
            case PokeLobby.PokeLocke.AccionEstado.Pendiente: pendientes++; break;
        }
    }

    // Evidencia adicional: re-leer el .sav de disco y resumir cambios
    var releido = SaveUtil.GetSaveFile(saveCopia);
    Console.WriteLine($"\nRe-lectura de disco: {releido!.GetType().Name} | OT={releido.OT} | dinero={releido.Money} | equipo={releido.PartyData.Count(p => p.Species > 0)} | checksums={(releido.ChecksumsValid ? "VÁLIDOS" : "INVÁLIDOS: " + releido.ChecksumInfo)}");
    Console.WriteLine($"Marcadas como aplicadas en el Firebase simulado: {fake.Aplicadas.Count}");
    Console.WriteLine($"Idempotencia local (acciones-aplicadas.txt): {File.Exists(Path.Combine(datosDir, "acciones-aplicadas.txt"))}");

    Console.WriteLine($"\nCola test: {aplicadas} aplicadas, {fallidas} fallidas, {omitidas} omitidas, {pendientes} devueltas a pendiente");
    return fallidas == 0 ? 0 : 1;
}

static int InyectarEquipoDemo(string rutaSave, string rutaJson, string playerTag)
{
    var locke = PokeLobby.PokeLocke.InyectorEquipo.CargarLockeDesdeJson(rutaJson);
    Console.WriteLine($"Locke {locke.Id}: {locke.Juego} ({locke.Modo}), jugadores {locke.Players.Count}");
    if (!locke.Players.TryGetValue(playerTag, out var jugador))
    {
        Console.WriteLine($"[FAIL] no existe el jugador «{playerTag}» (jugadores: {string.Join(", ", locke.Players.Keys)})");
        return 1;
    }
    var slots = jugador.MyTeam.Slots;
    Console.WriteLine($"Equipo de {playerTag}: {slots.Count} Pokémon — {string.Join(", ", slots.Select(s => $"#{s.Id} {s.Name} Nv{s.Level}"))}");

    // Copia del .sav (no destructiva) + inyección
    var dir = System.IO.Path.Combine(".rea-setup", "pokenete-test");
    Directory.CreateDirectory(dir);
    var destino = System.IO.Path.Combine(dir, "save-inyectado.sav");
    File.Copy(rutaSave, destino, overwrite: true);
    var sav = PokeLobby.Watcher.ResumenPartidaService.Abrir(destino);
    var inyectados = PokeLobby.PokeLocke.InyectorEquipo.Inyectar(sav, jugador);
    foreach (var p in inyectados)
        Console.WriteLine($"[diag] tras inyección: PID={p.PID:X8} version={p.Version} nature={p.Nature} ability={p.Ability}");
    PokeLobby.PokeLocke.InyectorEquipo.GuardarSave(sav, destino);
    Console.WriteLine($"Inyectados {inyectados.Count} Pokémon en la copia del .sav: {destino}\n");

    // Verificación: reabrir el .sav guardado y comprobar cada slot contra la app
    var sav2 = PokeLobby.Watcher.ResumenPartidaService.Abrir(destino);
    var strings = GameInfo.GetStrings("es");
    int pasadas = 0, fallos = 0;
    void Comprobar(string nombre, bool ok)
    {
        if (ok) pasadas++; else fallos++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {nombre}");
    }
    var n = Math.Min(slots.Count, sav2.PartyData.Count);
    for (var i = 0; i < n; i++)
    {
        var slot = slots[i];
        var pkm = sav2.PartyData[i];
        Comprobar($"slot {i}: especie {pkm.Species} == {slot.Id}", pkm.Species == slot.Id);
        Comprobar($"slot {i}: nivel {pkm.Stat_Level} == {slot.Level}", pkm.Stat_Level == slot.Level);
        if (Enum.TryParse(slot.Nature, ignoreCase: true, out Nature naturaleza))
            Comprobar($"slot {i}: naturaleza {pkm.Nature} == {naturaleza}", pkm.Nature == naturaleza);
        var evsOk = pkm.EV_SPA == slot.Evs.GetValueOrDefault("spa") && pkm.EV_SPE == slot.Evs.GetValueOrDefault("spe") && pkm.EV_SPD == slot.Evs.GetValueOrDefault("spd");
        Comprobar($"slot {i}: EVs 252/4/252 en SpA/SpD/Spe", evsOk);
        var ivsOk = pkm.IV_HP == slot.Ivs.GetValueOrDefault("hp", 0) && pkm.IV_SPA == slot.Ivs.GetValueOrDefault("spa", 0);
        Comprobar($"slot {i}: IVs del JSON", ivsOk);
        var idsEsperados = slot.Moves.Select(m => PokeLobby.PokeLocke.InyectorEquipo.BuscarIndice(strings.Move, m)).Where(id => id > 0).Select(id => (ushort)id).ToArray();
        var movimientosActuales = pkm.Moves.Take(idsEsperados.Length).ToArray();
        Comprobar($"slot {i}: movimientos ({string.Join("/", idsEsperados)})", movimientosActuales.SequenceEqual(idsEsperados));
        var idEsperado = PokeLobby.PokeLocke.InyectorEquipo.BuscarIndice(strings.Ability, slot.Ability);
        var slotHab = Math.Clamp(pkm.Ability, 0, Math.Max(0, pkm.PersonalInfo.AbilityCount - 1));
        var idActual = idEsperado > 0 && slotHab < pkm.PersonalInfo.AbilityCount ? pkm.PersonalInfo.GetAbilityAtIndex(slotHab) : -1;
        Comprobar($"slot {i}: habilidad «{slot.Ability}» (id {idEsperado}) aplicada (actual {idActual})",
            idEsperado <= 0 || idActual == idEsperado);
    }
    Console.WriteLine($"\nInyección App→.sav: {pasadas}/{pasadas + fallos} comprobaciones superadas" + (fallos == 0 ? " ✔" : " ✗"));
    return fallos == 0 ? 0 : 1;
}

static async Task<int> PokeLockeLeer(string lockeId, string? playerTag)
{
    try
    {
        using var cliente = new PokeLobby.PokeLocke.FirebaseRtdbClient();
        Console.WriteLine($"Firebase: {cliente.BaseUrl}");
        var locke = await cliente.ObtenerLockeAsync(lockeId);
        if (locke is null)
        {
            Console.WriteLine($"El locke «{lockeId}» no existe en la base de datos.");
            return 1;
        }
        Console.WriteLine($"{locke.Id}: {locke.Juego} ({locke.Modo}), {locke.Players.Count} jugadores, PKHeX: {locke.IsPkhexEnabled}");
        foreach (var (tag, jugador) in locke.Players)
        {
            Console.WriteLine($"  {tag}: monedas {jugador.Monedas}, oro {jugador.PrioridadOro}, premium {jugador.TicketsPremium}, mentas {jugador.TicketsMentas}, cartas {jugador.Inventario.Count(i => i.Type == "CARTA")}, sobres {jugador.Inventario.Count(i => i.Type == "SOBRE")}");
            foreach (var p in jugador.MyTeam?.Slots ?? [])
                Console.WriteLine($"    · #{p.Id} {p.Name} Nv{p.Level} {p.Nature} shiny={p.IsShiny}");
        }
        return 0;
    }
    catch (System.Text.Json.JsonException jex)
    {
        Console.WriteLine($"JSON inválido: {jex.Message}");
        Console.WriteLine($"Path JSON: {jex.Path}");
        return 1;
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException is not null) Console.WriteLine($"Interna: {ex.InnerException.GetType().Name}: {ex.InnerException.Message} / Path: {(ex.InnerException as System.Text.Json.JsonException)?.Path}");
        Console.WriteLine("Ejemplo:  $env:POKELOCKE_FIREBASE_URL = \"https://tu-proyecto-default-rtdb.europe-west1.firebasedatabase.app\"");
        return 2;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error leyendo Firebase: {ex.GetType().Name}: {ex.Message}");
        if (ex is System.Text.Json.JsonException jex) Console.WriteLine($"Path JSON: {jex.Path}");
        return 1;
    }
}

// ───────────────────────────── watcher / resumen ─────────────────────────────

static void WatchDemo(string dir)
{
    using var watcher = new PokeLobby.Watcher.SaveWatcher(
        new PokeLobby.Watcher.HuellasStore(System.IO.Path.Combine(dir, "huellas-demo.json")),
        conResumen: false);
    watcher.GuardadoCambiado += (_, c) =>
    {
        var s = c.Sha256 ?? "-";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {System.IO.Path.GetFileName(c.FilePath)} motivo={c.Motivo} sha256={s[..Math.Min(s.Length, 12)]}… size={c.Size}");
    };
    watcher.Start(dir);
    Console.WriteLine($"Vigilando partidas en: {dir}");
    Console.WriteLine("Crea o modifica un .sav/.dsv/.sa1/.sa2/main en esa carpeta. Ctrl+C para salir.");
    Thread.Sleep(Timeout.Infinite);
}

static int ResumenDemo(string ruta)
{
    var sav = PokeLobby.Watcher.ResumenPartidaService.Abrir(ruta);
    var resumen = PokeLobby.Watcher.ResumenPartidaService.Extraer(sav);
    Console.Error.WriteLine($"[info] save={sav.GetType().Name} juego={sav.Version} OT={sav.OT}");
    Console.Error.WriteLine($"[info] medallas={resumen.Medallas} segundos={resumen.Segundos} dinero={resumen.Dinero} pokemon={resumen.Pokemon.Count} objetos={resumen.Objetos.Count}");
    Console.Error.WriteLine("[info] Equipo en formato Showdown:\n" + PokeLobby.Watcher.ResumenPartidaService.ExportarShowdown(sav));
    // stdout: solo el JSON del resumen (formato compatible con el SaveResumen de huellas.json)
    Console.WriteLine(JsonSerializer.Serialize(resumen));
    return 0;
}

// ───────────────────────────── login real + verificación ─────────────────────────────

static async Task<int> LoginDemo(string email)
{
    Console.Write($"Contraseña de {email}: ");
    var password = Console.ReadLine();
    if (string.IsNullOrEmpty(password)) return 2;

    using var client = new PokelobbyClient();
    try
    {
        Console.WriteLine($"Entrando contra {client.BaseUrl} …");
        var respuesta = await client.LoginAsync(email, password);
        Console.WriteLine("Login OK. Respuesta (observa el campo refreshToken/accessToken):");
        Console.WriteLine(respuesta?.ToJsonString() ?? "(vacía)");
    }
    catch (PokelobbyApiException ex)
    {
        Console.WriteLine($"Login rechazado: {ex.StatusCode} «{ex.ErrorCode ?? ex.Message}»");
        return 1;
    }

    var salas = await client.GetMisSalasAsync();
    Console.WriteLine($"\nTus salas ({salas?.Count ?? 0}):");
    foreach (var sala in salas ?? [])
        Console.WriteLine($"  [{sala.Id}] {sala.Nombre} (juego {sala.JuegoId})");

    await using var hub = new PokelobbyNotificationsHub();
    try
    {
        await hub.ConnectAsync(
            () => Task.FromResult(client.AccessToken),
            carta => Console.WriteLine($"\a📩 CARTA de {carta.RemitenteNombre} en «{carta.SalaNombre}»: {carta.CartaEmoji} {carta.CartaTitulo}"),
            log => Console.WriteLine($"[hub] {log}"));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[hub] no se pudo conectar: {ex.Message}");
    }
    Console.WriteLine("\nEscuchando cartas… Ctrl+C para salir.");
    Thread.Sleep(Timeout.Infinite);
    return 0;
}

static async Task<int> VerificacionInocua()
{
    Console.WriteLine("PokeLobby.Recreado — verificación del cliente del protocolo");
    Console.WriteLine($"Servidor: {PokelobbyClient.ResolveBaseUrl()} (variable POKELOBBY_API)\n");

    int pasadas = 0, fallos = 0;

    using var client = new PokelobbyClient();

    // 1) POST /api/auth/login con datos inventados → 401 + {"error": "..."}
    try
    {
        await client.LoginAsync("probe@example.invalid", "probe-not-a-real-login");
        fallos++;
        Console.WriteLine("[FAIL] POST /api/auth/login (datos falsos) → no debería aceptar credenciales inventadas");
    }
    catch (PokelobbyApiException ex)
    {
        var ok = ex.StatusCode == 401 && (ex.ErrorCode?.Contains("Email o contraseña") ?? false);
        if (ok) pasadas++; else fallos++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] POST /api/auth/login (datos falsos) → {ex.StatusCode} «{ex.ErrorCode ?? ex.Message}»");
    }
    catch (Exception ex)
    {
        fallos++;
        Console.WriteLine($"[FAIL] POST /api/auth/login (datos falsos) → excepción inesperada: {ex.GetType().Name}: {ex.Message}");
    }

    // 2) POST /api/auth/refresh con token falso → 401 + {"error": "..."}
    try
    {
        using var c2 = new PokelobbyClient(refreshToken: "probe-invalid-token");
        await c2.RefreshAsync();
        fallos++;
        Console.WriteLine("[FAIL] POST /api/auth/refresh (token falso) → no debería refrescar");
    }
    catch (PokelobbyApiException ex)
    {
        var ok = ex.StatusCode == 401 && (ex.ErrorCode?.Contains("Refresh token") ?? false);
        if (ok) pasadas++; else fallos++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] POST /api/auth/refresh (token falso) → {ex.StatusCode} «{ex.ErrorCode ?? ex.Message}»");
    }
    catch (Exception ex)
    {
        fallos++;
        Console.WriteLine($"[FAIL] POST /api/auth/refresh (token falso) → excepción inesperada: {ex.GetType().Name}: {ex.Message}");
    }

    // 3) GET /api/salas/mis-salas sin sesión → 401 Bearer
    try
    {
        await client.GetMisSalasAsync();
        fallos++;
        Console.WriteLine("[FAIL] GET /api/salas/mis-salas sin sesión → no debería responder sin autenticar");
    }
    catch (PokelobbyApiException ex)
    {
        var ok = ex.IsUnauthorized;
        if (ok) pasadas++; else fallos++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] GET /api/salas/mis-salas sin sesión → {ex.StatusCode} {ex.Message}");
    }
    catch (Exception ex)
    {
        fallos++;
        Console.WriteLine($"[FAIL] GET /api/salas/mis-salas sin sesión → excepción inesperada: {ex.GetType().Name}: {ex.Message}");
    }

    // 4) POST /hubs/notificaciones/negotiate sin token → 401 + WWW-Authenticate: Bearer
    try
    {
        var (estado, www) = await client.ProbeHubNegotiateAsync();
        var ok = estado == 401 && (www?.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase) ?? false);
        if (ok) pasadas++; else fallos++;
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] POST /hubs/notificaciones/negotiate sin token → {estado} www-auth={www ?? "-"}");
    }
    catch (Exception ex)
    {
        fallos++;
        Console.WriteLine($"[FAIL] POST /hubs/notificaciones/negotiate sin token → excepción inesperada: {ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine($"\nResultado: {pasadas}/{pasadas + fallos} verificaciones del protocolo superadas" +
        (fallos == 0 ? " — cliente del protocolo VALIDADO ✔" : " — hay fallos ✗"));
    return fallos == 0 ? 0 : 1;
}


sealed class FirebaseSimulado : PokeLobby.PokeLocke.IColaFirebase
{
    private readonly Dictionary<string, PokeLobby.PokeLocke.AccionLauncher> _cola = new();
    private readonly Dictionary<string, string> _etags = new();

    public Action<string>? Registro { get; set; }
    public List<string> Aplicadas { get; } = [];
    public List<string> Fallidas { get; } = [];
    public List<(string Id, string Status, string ResultMessage)> Resultados { get; } = [];

    public FirebaseSimulado(string rutaJson)
    {
        var nodo = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(rutaJson));
        var parseada = PokeLobby.PokeLocke.ColaDeserializador.Parsear(nodo);
        foreach (var (clave, motivo) in parseada.Errores)
            Registro?.Invoke($"entrada ilegible «{clave}»: {motivo}");
        _cola = parseada.Acciones;
        foreach (var id in _cola.Keys)
            _etags[id] = $"\"etag-sim-{id}\"";
    }

    public Task<Dictionary<string, PokeLobby.PokeLocke.AccionLauncher>> LeerColaAsync(string lockeId, string playerTag, CancellationToken ct = default)
        => Task.FromResult(new Dictionary<string, PokeLobby.PokeLocke.AccionLauncher>(_cola));

    public Task<(PokeLobby.PokeLocke.AccionLauncher? Accion, string? ETag)> LeerAccionAsync(string lockeId, string playerTag, string actionId, CancellationToken ct = default)
        => Task.FromResult((_cola.TryGetValue(actionId, out var a) ? a : null, _etags.GetValueOrDefault(actionId)));

    public Task<bool> ReclamarAccionAsync(string lockeId, string playerTag, string actionId, PokeLobby.PokeLocke.AccionLauncher accion, CancellationToken ct = default)
    {
        // Simula el ETag: si ya está "applying" por otro con claim reciente → 412
        if (_cola.TryGetValue(actionId, out var actual)
            && actual.Status == PokeLobby.PokeLocke.AccionEstado.Aplicando
            && actual.ClaimedBy != "LockeLauncher 1.1"
            && actual.ClaimedAt is { } ca
            && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ca < PokeLobby.PokeLocke.ColaLauncherService.CaducidadClaimMs)
            return Task.FromResult(false);
        accion.Status = PokeLobby.PokeLocke.AccionEstado.Aplicando;
        accion.ClaimedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        accion.ClaimedBy = "LockeLauncher 1.1";
        _cola[actionId] = accion;
        return Task.FromResult(true);
    }

    public Task MarcarResultadoAsync(string lockeId, string playerTag, string actionId, string status, string resultMessage, string? errorMessage = null, CancellationToken ct = default)
    {
        Resultados.Add((actionId, status, resultMessage));
        if (_cola.TryGetValue(actionId, out var a))
        {
            a.Status = status;
            a.ResultMessage = resultMessage;
            a.ErrorMessage = errorMessage;
            a.AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            a.AppliedBy = "LockeLauncher 1.1";
        }
        if (status == PokeLobby.PokeLocke.AccionEstado.Aplicada) Aplicadas.Add(actionId);
        if (status == PokeLobby.PokeLocke.AccionEstado.Fallida) Fallidas.Add(actionId);
        return Task.CompletedTask;
    }

    public Task DevolverAPendienteAsync(string lockeId, string playerTag, string actionId, string motivo, CancellationToken ct = default)
    {
        if (_cola.TryGetValue(actionId, out var a))
        {
            a.Status = PokeLobby.PokeLocke.AccionEstado.Pendiente;
            a.ClaimedAt = null;
            a.ClaimedBy = null;
            a.ResultMessage = motivo;
        }
        return Task.CompletedTask;
    }
}