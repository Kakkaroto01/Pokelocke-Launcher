# PokeLobby.Recreado (C# / .NET 10)

Recreación del launcher de Pokelobby a partir del análisis de ingeniería inversa con REA
(ver `..\INFORME-PokeLobby.md` para el protocolo completo y su evidencia).

## Proyectos

| Proyecto | Qué contiene |
|---|---|
| `PokeLobby.Client` | Cliente REST completo del protocolo (auth + refresh automático en 401, salas, partidas/showdown, integridad, vivo, items, asistente, OAuth Google/GitHub) + hub SignalR `/hubs/notificaciones` con reconexión automática y evento `CartaRecibida`. |
| `PokeLobby.Watcher` | Vigilante de partidas `.sav/.dsv/.sa1/.sa2/main` con huellas SHA-256 y `huellas.json` **compatible con el formato del launcher original** + resumen PKHeX (formato idéntico al `SaveResumen`) + exportación Showdown. |
| `PokeLobby.Emulator` | Lanzador de emuladores del `vendor\` (mGBA→GBA, melonDS→DS, azahar→3DS, externo con plantilla `{rom}` para Switch), ajustes de `qt-config.ini` de azahar (gdbstub/texturas/sin-update, fusión no destructiva) y **cliente GDB** del stub de azahar (127.0.0.1:3000) + exploración RAM en vivo (0x08000000→0x0A000000, equipo 6×484 bytes). |
| `PokeLobby.LauncherUi` | **La interfaz gráfica** (WPF, español, tema oscuro): biblioteca con carátulas desde `library.json` real, botón ▶ JUGAR (emulador + watcher + resumen Showdown en vivo), 🎲 Randomizar con presets, lockes de Firebase en vivo (SSE) con inyección del equipo de la app al .sav, y registro de actividad. Ejecutable: `PokeLockeLauncher.exe`. |
| `PokeLobby.PokeLocke` | **Integración con PokeLocke Android**: DTOs del esquema Firebase (`/lockes/{lockeId}`), cliente RTDB por REST + SSE en tiempo real (config por `POKELOCKE_FIREBASE_URL`/`POKELOCKE_FIREBASE_AUTH`), **inyección del equipo de la app en el .sav** (especie/nivel/naturaleza por PID/habilidad/movimientos en español/IVs/EVs/shiny) y motor de efectos de cartas (EVs/IVs, dinero, mentas, MTs, revive, mentor, genética). |
| `PokeLobby.Demo` | Consola: verificación del protocolo, resumen PKHeX, estado de emuladores, test GDB, demo del watcher y login real con escucha de cartas. |

## Uso

```powershell
# Construir
dotnet build src\PokeLobby.slnx

# 1) Verificación inocua contra el servidor real (sin tus credenciales) — 4/4 esperado
dotnet run --project src\PokeLobby.Demo

# 2) Demo del vigilante de partidas sobre una carpeta
dotnet run --project src\PokeLobby.Demo -- --watch C:\ruta\a\partidas

# 3) Login real (pide contraseña) + tus salas + escucha de cartas del hub
dotnet run --project src\PokeLobby.Demo -- --login tu@email.com
```

La URL del servidor se resuelve igual que el launcher original:
variable de entorno `POKELOBBY_API` (por defecto `https://api-pokemon-main-a21032e.onrender.com`).
Para apuntar a **tu propio backend**:

```powershell
$env:POKELOBBY_API = "https://tu-servidor.example"
dotnet run --project src\PokeLobby.Demo
```

## Conectar tu propia app

Referencia `PokeLobby.Client` desde tu proyecto:

```csharp
using var client = new PokelobbyClient();            // lee POKELOBBY_API
await client.LoginAsync(email, password);            // o ExchangeOAuthCodeAsync(...)
var salas = await client.GetMisSalasAsync();         // List<RoomInfo>
await client.SetSalaActivaAsync(salaId);
await client.SendShowdownAsync(new ShowdownUpload { ... });   // progreso de partida
var hub = new PokelobbyNotificationsHub();
await hub.ConnectAsync(() => Task.FromResult(client.AccessToken),
    carta => Console.WriteLine($"CARTA: {carta.CartaTitulo}"));
```

- ✔ **UI WPF** — `PokeLockeLauncher.exe` (tema oscuro, español, logo pokeball): biblioteca
  desde tu `library.json` real con carátulas, ▶ JUGAR (lanza emulador, arranca el watcher y
  muestra el resumen Showdown de cada guardado), 🎲 Randomizar con presets del vendor,
  selector de lockes de Firebase + jugador con **suscripción SSE en vivo**, botón
  «Inyectar equipo de la app → .sav», y barra de estado con registro. Ajustes en ⚙
  (Firebase URL/API key, carpeta de datos, vendor).
- ✔ Cliente del protocolo REST + hub SignalR (verificado 4/4 contra el servidor real).
- ✔ Watcher de partidas con huellas compatibles con el original.
- ✔ Resumen PKHeX + exportación Showdown — **verificado al 100 % contra el ground truth del
  launcher original** (`huellas.json`, misma partida de HeartGold): equipo 6/6 con todos los
  campos exactos, bolsa 210/210 entradas, entrenador/medallas/dinero/tiempo exactos.
  Pruébalo: `dotnet run --project src\PokeLobby.Demo -- --resumen <tu-save.sav>`
- ✔ Lanzamiento de emuladores + config de azahar — resuelve los emuladores reales del
  `vendor\` (mGBA/melonDS/azahar presentes), fusión de `qt-config.ini` **6/6** (gdbstub,
  texturas, sin auto-update, preservando claves ajenas) y adopción de emulador ya abierto.
  Pruébalo: `dotnet run --project src\PokeLobby.Demo -- --emu-info`
- ✔ Cliente GDB (Remote Serial Protocol, **puerto 24689** del gdbstub de azahar) — **3/3
  pruebas** contra un stub simulado: handshake `qSupported:multiprocess-;swbreak+` + `?` + `c`,
  lecturas `m/x` fragmentadas ≤4096 con checksum. Pruébalo: `dotnet run --project src\PokeLobby.Demo -- --gdb-test`
- ✔ Puente Java del randomizer — contrato verificado contra el **fuente Java incluido en el
  vendor** (`bridge\RandoBridge.java`): `java -cp "PokeRandoZX.jar;<bridge>" RandoBridge
  <config.properties> <rom_entrada> <rom_salida>` con exit codes 1-4 y «SUCCESS»; JRE propio
  del vendor responde (OpenJDK 17.0.20); 4 presets cargados (`full_random`,
  `full_random_equilibrado`, `solo_habilidades_y_ataques`, `solo_pokemon`); ida y vuelta del
  formato `.rnqs` con 168 claves ✔. Pruébalo: `--randomizer-info`, y para randomizar de verdad:
  `dotnet run --project src\PokeLobby.Demo -- --randomizar <rom.nds> solo_pokemon`
- ✔ RAM-vivo completa — reconstruida con la lógica exacta del código descompilado del original
  (ilspycmd, fuente en `.rea-setup\RamVivoService-decompiled.cs`): entrada de 484 bytes =
  PK6[0..232] + tráiler de 28 bytes en 344 (con HP actual/máx), validación del equipo por
  EncryptionConstant, exploración del heap 0x08000000→0x0A000000 con aguja EC en páginas de
  0x1004, detección de **caídos** y **encuentros** (rival capturado/derrotado) con perfil XY
  (000400000011C500: ruta/combate/rival), cola de 200 y envío por lotes cada 5 s a
  `POST /api/saves/vivo`. **Verificado 10/10** con un Pokémon real de la partida del usuario
  (PID 4003819604 «Telefónica», especie 466, nivel 70, HP 207/207). Pruébalo:
  `dotnet run --project src\PokeLobby.Demo -- --ramvivo-test <tu-save.sav>`
- ✔ **Cuenta PokeLocke (login)** — la UI inicia sesión con el **mismo email/password de la
  app** (Firebase Email/Password): muestra SOLO los lockes de tu cuenta (`user_lockes/{uid}`),
  resuelve tu playerTag desde `users/{uid}` (`usuario_discriminator`), sesión persistente con
  **refresh token** (re-entra sin contraseña; rota y se guarda renovado), y botón **➕ Crear
  locke** desde el escritorio (escribe el locke + el vínculo en tu cuenta).
- ✔ **Integración PokeLocke Android — en vivo contra Firebase real** — cliente RTDB con
  **auth anónima automática** (`POKELOCKE_FIREBASE_API_KEY` → identitytoolkit signUp → ID
  token, renovado en 401), lectura del locke `5485c9b3…` (Escudo «Debug») **en vivo** con los
  datos reales (el_2861: 1097 monedas / 80 cartas), SSE en tiempo real verificado
  (`event: put` recibido), e **inyección del equipo de la app en el .sav verificada 7/7**
  con el JSON del doc (Typhlosion Nv50 TIMID «Mar Llamas», IVs 31×6, EVs 252/4/252 — releídos
  del .sav). Nota crítica: fijar `pkm.Version = sav.Version` antes de tocar el PID (con
  `Version=Any`, `EntityPID.GetRandomPID` ignora naturaleza/habilidad).
  Pruébalo: `dotnet run --project src\PokeLobby.Demo -- --pokenete-leer <lockeId>` (con
  `POKELOCKE_FIREBASE_URL` + `POKELOCKE_FIREBASE_API_KEY` en el entorno), o inyecta desde un
  JSON: `--inyectar-equipo <save> <locke.json> <playerTag>`.
- ✔ **UI WPF** — `PokeLockeLauncher.exe` (tema oscuro, español, logo pokeball): biblioteca
  desde tu `library.json` real con carátulas, ▶ JUGAR (lanza emulador, arranca el watcher y
  muestra el resumen Showdown de cada guardado), 🎲 Randomizar con presets del vendor,
  selector de lockes de Firebase + jugador con **suscripción SSE en vivo**, botón
  «Inyectar equipo de la app → .sav», y barra de estado con registro. Ajustes en ⚙
  (Firebase URL/API key, carpeta de datos, vendor).
