# Pokelobby (PokeLocke Launcher) — Análisis de ingeniería inversa con REA

> Objetivo del análisis: entender cómo funciona Pokelobby (launcher + servidor) para poder
> **recrearlo y conectar tu propia app al mismo ecosistema**.
> Toda la información de este informe proviene del análisis estático del artefacto real con
> REA 6.5.0 (`rea inspect-managed-artifact`, `inspect-managed-members`, `inspect-managed-native-boundaries`)
> y de los archivos de datos que dejó la propia app en ejecución. No se ejecutó el launcher.

---

## 0. Resumen ejecutivo

**Pokelobby / PokeLocke** es un **launcher de Nuzlocke multiusuario** para Windows:

1. El jugador tiene sus ROMs de Pokémon (GBA/DS/3DS) y las juega en emuladores que el propio
   launcher incluye (`mGBA`, `melonDS`, `azahar`).
2. Un **watcher vigila el archivo de partida (.sav)** en segundo plano; al detectar cambios la
   parsea con **PKHeX.Core** y extrae un resumen (entrenador, medallas, muertos, equipo, cajas, objetos).
3. Ese resumen se **sube a un servidor central vía REST** (`POST /api/saves/showdown`, con el
   equipo en formato **Pokémon Showdown**) asociado a una **sala** (p. ej. "Hardlocke", id 607).
4. El servidor es el árbitro del reto: reparte **cartas** (trades, huevos, revives, mentas,
   objetos) que llegan por **SignalR** (`/hubs/notificaciones`, evento `CartaRecibida`) y que el
   launcher **inyecta directamente en el archivo .sav**.
5. Hay sistema de cuentas (login/refresh/OAuth Google+GitHub), monedas, medallas, inventario,
   historial, integridad anti-trampas, asistente IA, randomizer por sala y actualizaciones Velopack.

**El hallazgo más importante para ti**: la URL del servidor NO está fijada en el código — se lee
de la **variable de entorno `POKELOBBY_API`** (con `https://api-pokemon-main-a21032e.onrender.com`
como valor por defecto). Eso significa que el launcher oficial puede **apuntarse a un servidor
propio** con solo esa variable, y tu propia app puede hablar el mismo protocolo.

---

## 1. Identidad del artefacto analizado

| Campo | Valor |
|---|---|
| Ensamblado principal | `PokeLobby.Launcher\current\PokeLobbyLauncher.dll` (1.569.792 bytes, PE x86_64) |
| SHA-256 | `435f14fb194d245bace0fcb7e2995f10b7ce9a4e5e27cfac1d854ea4beab239b` |
| MVID | `c9e3ee8b-8145-4a66-a056-08921143852c` |
| Plataforma | .NET 10.0 (`net10.0`, runtimes empaquetados 10.0.10: `Microsoft.NETCore.App` + `Microsoft.WindowsDesktop.App`) |
| UI | **WPF** + `CommunityToolkit.Mvvm` (MVVM), localización es/en y satélites (cs, de, fr…) |
| Metadatos | 518 tipos, 3.846 métodos, 38 referencias de ensamblado |
| Código nativo | **Ninguno** — IL-only, 0 P/Invoke (`rea inspect-managed-native-boundaries`) |
| Versión instalada | 0.1.36 (paquete Velopack `PokeLobby.Launcher-0.1.36-full.nupkg`, canal beta `.betaId`) |
| Evidence REA | `ev_2a499b4757794de592911cd16675ee8748ef1f0cd11c0516e561150874b1df73` |

Dependencias clave embarcadas en `current\`: `PKHeX.Core.dll` (parseo/inyección de partidas),
`Microsoft.AspNetCore.SignalR.Client.*` (hub en tiempo real), Velopack (`Update.exe`, `packages\`),
`vendor\` con `mGBA`, `melonDS`, `azahar` (+`azahar-room.exe`), `jre` (Java 64 bits propio),
`SuperRandomizer` (`PokeRandoZX.jar` + puente `RandoBridge.class`) y `PKHeX-Variantes`.

Estructura de instalación (Velopack/Squirrel-style):

```
PokeLobby.Launcher\            ← instalación (portátil/copiada)
  PokeLobbyLauncher.exe        ← host (apphost .NET)
  Update.exe                   ← actualizador Velopack
  current\                     ← versión activa (binarios + vendor + recursos)
  packages\                    ← .nupkg de versiones + .betaId + .velopack_lock
PokeLobby\Launcher\            ← DATOS del usuario (≈ %LOCALAPPDATA%\PokeLobby\Launcher)
  session.json, library.json, huellas.json, vinculos.json, ultima_subida.json,
  covers\, covers.json, manifiestos\ (por-huella\<sha256>.json), rom-ajustes\,
  logs: watcher.log, session_debug.log, randomizer.log, coverfetch.log, candy/egg/trade/revive_debug.log
```

---

## 2. Arquitectura de conexión — el corazón del sistema

### 2.1 URL del servidor y `POKELOBBY_API`

Tres servicios (`AuthService`, `NotificacionesService`, `SaveWatcherService`) resuelven la URL
base en su constructor estático: `Environment.GetEnvironmentVariable("POKELOBBY_API")` y, si no
está definida, usan `https://api-pokemon-main-a21032e.onrender.com` (API hosteada en Render).

> **Consecuencia práctica**: puedes ejecutar el launcher contra **tu propio backend** haciendo
> `set POKELOBBY_API=https://tu-servidor.example` antes de arrancarlo. Es la palanca exacta para
> "conectarlo a mi propia app" en cualquiera de las dos direcciones.

Otros endpoints fijos:
- Web del proyecto: `https://www.pokelocke.com` (registro, sala/cartas: `https://www.pokelocke.com/rooms/{sala}/cards`).
- Actualizaciones: `https://img.pokelocke.com/releases` (fuente Velopack).
- Carátulas: `https://raw.githubusercontent.com/libretro-thumbnails/master/Named_Boxarts/...`
  (búsqueda exacta + difusa) y sprites de objetos: `https://cdn.jsdelivr.net/gh/PokeAPI/sprites@master/sprites/items/`.

### 2.2 Autenticación (todo con `HttpClient` + `Authorization: Bearer <accessToken>`)

| Operación | Método y ruta | Payload / notas | Código (evidencia) |
|---|---|---|---|
| Login con email | `POST /api/auth/login` | `{ email, password }` → guarda `refreshToken` en `session.json` | `AuthService.LoginAsync` |
| Refrescar token | `POST /api/auth/refresh` | `{ refreshToken }` → nuevo access token | `AuthService.DoRefreshAsync` |
| Logout | `POST /api/auth/logout` | con Bearer; borra la sesión local | `AuthService.Logout` |
| OAuth (Google/GitHub) | `GET /api/auth/{provider}/url?redirect_uri=…` → `POST /api/auth/{provider}/exchange` | `{ code, redirectUri }` | `StartOAuth`, `ListenForOAuthCallback`, `ExchangeOAuthCode` |

El flujo OAuth monta un **servidor HTTP local efímero** en `http://localhost:{puerto}/callback/`,
abre el navegador con la URL que devuelve la API, espera el callback con el `code` y lo canjea
por el refresh token. El refresh se reintenta automáticamente: cualquier llamada REST que recibe
**401 refresca el token y repite la petición** una vez (visible en `ExportToApi`).

Sesión persistida (`PokeLobby\Launcher\session.json`, claves **camelCase**):
`refreshToken, userName, email, avatarUrl, coins, medals, activeRoom` — restaurada al arrancar
con `TryRestoreSession` (usa `/api/auth/refresh` para validarla).

### 2.3 API REST completa (extraída de los cuerpos IL con REA)

Base: `{POKELOBBY_API}` · Autenticación: `Authorization: Bearer` · `Content-Type: application/json`

**Salas / organización**
| Método | Ruta | Uso |
|---|---|---|
| GET | `/api/salas/mis-salas` | listar mis salas (`FetchMisSalas`) |
| PATCH | `/api/salas/activa` | marcar sala activa (`SetSalaActiva`) |
| GET | `/api/salas/{id}/randomizer?version=N` | config randomizer de la sala (`GetRandomizerSalaAsync`) |
| POST | `/api/salas/{id}/randomizer` | publicar config randomizer (staff) (`PublicarRandomizerSalaAsync`) |
| POST | `/api/salas/{id}/organizacion/mods` | publicar mods permitidos (`PublicarModsPermitidosAsync`) |

**Partidas / integridad**
| Método | Ruta | Uso |
|---|---|---|
| POST | `/api/saves/showdown` | **subir progreso** (ver §3) |
| POST | `/api/saves/vivo` | eventos en vivo leídos de la RAM del emulador (§5) |
| GET | `/api/saves/integridad/estado?salaId=` | estado de integridad de la sala |
| POST | `/api/saves/integridad/evento` | reportar evento de integridad (p. ej. partida editada fuera del launcher) |
| GET | `/api/saves/integridad/bloqueos` | alertas de bloqueo |
| GET | `/api/saves/integridad/mods?salaId=` | mods permitidos de la sala |
| GET | `/api/saves/integridad/verificaciones/pendientes` | verificaciones pendientes |
| POST | `/api/saves/integridad/verificaciones/{id}/informe` | enviar informe de verificación |
| POST | `/api/saves/presencia-sala` | fijar presencia del jugador en la sala |
| DELETE | `/api/saves/presencia-sala` | salir de la presencia de sala |
| DELETE | `/api/saves/presencia` | borrar presencia del launcher |

**Economía / items**
| Método | Ruta | Uso |
|---|---|---|
| GET | `/api/items/inventario?salaId=` | inventario del jugador en la sala |
| GET | `/api/items/historial?salaId=` | historial de entregas |
| POST | `/api/items/inventario/usar` | usar/canjear un item |
| POST | `/api/items/inventario/{id}/entregar` | aceptar entrega (carta) |
| POST | `/api/items/inventario/{id}/descartar` | descartar entrega |
| POST | `/api/items/revivir` | pedir revivir un Pokémon (`SolicitarRevivirAsync`) |
| POST | `/api/items/aplicar-menta` | aplicar una "menta" (cambiar naturaleza) |
| GET | `/api/items/egg/{id}` | resultado del huevo misterioso |
| GET | `/api/intercambios/{id}` | estado de un intercambio |
| GET | `/api/items/mt/catalogo?salaId=` | catálogo de MT (máquinas técnicas) |

**Asistente**
| Método | Ruta | Uso |
|---|---|---|
| POST | `/api/asistente/preguntar` | preguntar al asistente IA |
| POST | `/api/asistente/tickets` | crear ticket de soporte |

### 2.4 Hub en tiempo real (SignalR)

- Conexión: `{POKELOBBY_API}/hubs/notificaciones`, con `WithAutomaticReconnect` (`NotificacionesService.ConectarAsync`).
- El cliente registra **un único handler**: `.On("CartaRecibida", …)` — el servidor empuja "cartas"
  (repartos del árbitro: objetos, huevos, trades, revives, mentas…).
- DTO `CartaRecibidaNotificacion`: `SalaId, SalaNombre, RemitenteNombre, CartaTitulo, CartaEmoji`
  → el launcher muestra un toast y la carta queda en `https://www.pokelocke.com/rooms/{sala}/cards`.
- En cuanto el jugador acepta la carta, los detalles se piden por REST (`/api/items/egg/{id}`,
  `/api/intercambios/{id}`, inventario…) y se **inyectan en el .sav** (§4).

---

## 3. Pipeline de sincronización de partidas (el "watcher")

1. **Lanzamiento con vigilancia** (`MainViewModel.LaunchWithWatcher`): elige emulador según consola
   (`mGBA`/GBA, `melonDS`/DS, `azahar`/3DS, externo para Switch con plantilla de argumentos
   `"{rom}"` configurable en `emulador_switch.json`). Antes de arrancar **aparta los trucos**
   (cheats de Azahar renombrados a `*.apartado-pokelobby-*`) y publica las **señales** (mods)
   detectados junto a la ROM.
2. **Localizar la partida** (`FindSavePath`/`BuildSaveCandidates`): busca `main` (Switch/3DS) o
   `*.sav, *.dsv, *.sa1, *.sa2`; si no existe, espera a que el emulador la cree.
3. **Vigilancia** (`SaveWatcherService.Start`): `FileSystemWatcher` sobre el directorio del save
   con filtro y subcarpetas; estados del anillo de estado: `Inactivo, Esperando, Vigilando, Subiendo, Ok, Error`.
4. **Huellas** (`huellas.json`, `HuellasService`): por archivo guarda `RomHash` (SHA-256), tamaño,
   mtime, `motivo` ("añadida"/"cierre") y para el save el `SaveHash` + **resumen PKHeX**:
   entrenador, TrainerId, segundos jugados, dinero, medallas, Pokémon (pid, especie, mote, nivel,
   shiny, IVs, habilidad, ataques, objeto, equipo o caja) y bolsa de objetos por ID.
5. **Detección de muertes**: patrones de nombre de evento `muert|dead|cementer|graveyard` en la
   partida; construye la lista de `Muertos` (con sexo M/F).
6. **Conversión a Showdown**: el equipo actual se serializa al **formato de texto Showdown**
   (`Export`); "Equipo vacío (showdown en blanco), no se envía a la API".
7. **Subida**: `POST /api/saves/showdown` con Bearer. Payload (campos observados en la plantilla
   de log del envío): `Export, PartyPids, PartyRutas, PartyAbilityNumbers, TrainerId, Medallas,
   Monedas, SalaId, Boxes, Muertos, HaGanadoLiga, TotemItemIds, JuegoVersion, Entrenador,
   SegundosJugados, SaveHash, LauncherVersion, RandomizerVersion, RevisionPartida, Senales`.
   *Nota: las plantillas de peticiones (login, refresh, OAuth, eventos) usan claves camelCase;
   los nombres de campo del payload aparecen en PascalCase en los logs — confirma el casing
   exacto con una respuesta real del servidor (ver §8).*
8. **Resultado**: `ultima_subida.json` (`{Fecha, Pokemon, Medallas, Muertos, Sala}`) y estado
   `Sync_Ok` / `Sync_Err*` (vacío, ROM sin sala, sesión, rechazo, API, red…).
9. **Integridad**: `ComprobarHuellaPartida` compara la partida con la última huella al volver;
   si cambió fuera del launcher ("+N Pokémon, -N, cambiados, objetos") reporta
   `partida_modificada` vía `POST /api/saves/integridad/evento` y puede abrir verificación/bloqueo.

`vinculos.json` mapea `SalaId → [{RomHash, Rom, Fecha}]`: qué ROMs (originales y randomizadas)
pertenecen a cada sala. `manifiestos\por-huella\<sha256>.json` guarda el manifiesto por ROM y
`rom-ajustes\<id>.txt` los ajustes por ROM.

---

## 4. Economía del lobby: cartas → inyección en el .sav

Cuando llega una carta (`CartaRecibida`) el jugador la acepta y el launcher **escribe en el
archivo de partida** (todo con PKHeX, sin código nativo):

- **Objetos** (`InjectViaPKHeXBag`, `GiveItem`): localiza el bolsillo (`Pouch`/`Slot`) y lo escribe.
- **Gen 3 crudo** (`InjectGen3RawBytes`): busca sección activa, recalcula el checksum de sección.
- **Gen 4**: parche de "slot A basura" para forzar el slot B (`FixGen4SlotAIfGarbage`).
- **Gen 7**: si la firma del save queda inválida, **ajusta el tiempo de juego** para repararla
  (`EscribirSaveFirmado`).
- **Trades** (`ApplyOneTradeToSav`): sustituye por PID el Pokémon en caja/equipo; soporta
  donaciones (entregas sin contrapartida, al primer hueco libre), compactación y validación de
  especie vs. versión del juego.
- **Huevos** (`BuildEggPkm`): construye el PKM del huevo; con ROM randomizada **reconstruye el
  resultado en el launcher** (habilidad/naturaleza del randomizer) y lo marca `IsEgg=true`.
- **Revives y mentas**: candidatos elegibles por PID (nivel, especie, naturaleza actual).
- Cada operación deja log dedicado (`candy/egg/trade/revive_debug.log`).

DTOs observados: `TradeOferta` (Species, Pid, Level, IV_*, EV_*, Ability, Nature, Gender,
HeldItem, Move1-4, Nickname), `EggPokemonResult` (idéntico + `EsRandomizer`),
`InventoryItem` (ItemId, PkHexItemId, Cantidad, Bolsillo, Categoria, Rareza, RequiereLauncher,
EggResultId, TradeId, RevivePid, MtResultId, AplicarEfecto*), `HistorialEntrega`, `RoomInfo`
(Id, JuegoId, Nombre), `SyncEstado` (Fase, Titulo, Detalle, UltimaSubida).

---

## 5. Emuladores y "RAM en vivo" (GDB)

- `EmulatorService.Launch` arranca el emulador correspondiente desde `vendor\` con la ROM,
  un emulador a la vez (reutiliza el PID si ya estaba abierto: `AdoptarEmuladorAbierto`).
- Configura **azahar** escribiendo su `qt-config.ini`:
  - `use_gdbstub=true` (stub GDB activado) — [Debugging]
  - `custom_textures=true` — [Utility] (texturas personalizadas)
  - `check_for_update_on_start=false` — [Miscellaneous] (evita updates del emulador)
- `GdbCliente` se conecta a `127.0.0.1` (stub GDB de azahar), negociando
  `qSupported:multiprocess-;swbreak+`, leyendo memoria con `m{hex},{len}` y continuando con `c`.
- `RamVivoService` (solo 3DS) usa esas lecturas para generar **eventos en vivo** durante la
  partida, que se envían con `POST /api/saves/vivo` (plantilla: `{ salaId, eventos }`) — el
  árbitro ve el juego en tiempo real sin depender del .sav.
- **Señales/mods** (`SenalesEmuladorService`): detecta mods de Azahar (`load/mods`, partes que
  pisan `code.ips`, `code.bin`, `exefs/`, `romfs/`), trucos `.cheats` (apartados), y clasifica
  las secciones del randomizer (StaticPokemon, Field, WildPokemon, TrainerData, TrainerPokemon,
  PokemonStats, PokemonMovesets, PokemonEvolutions, MoveData, ItemData, EggMoves, MegaEvolutions,
  Code). Las señales activas se incluyen en el payload de `/api/saves/showdown` (`Senales`).

---

## 6. Randomizer por sala

- Motor: **`vendor\SuperRandomizer`** — `PokeRandoZX.jar` (universal Pokémon randomizer ZX) +
  un puente propio `RandoBridge.class`; se ejecuta con el **JRE empaquetado**
  (`vendor\jre`, `-Xmx4608M`) y argumentos (ROM entrada, ROM salida `pokelobby_rando_*`, config).
- La configuración del randomizer es **por sala y versión** (`GET/POST
  `/api/salas/{id}/randomizer?version=`): el staff de la sala publica la config y los jugadores
  la replican; mensajes de estado diferencian jugador/staff/config (`RandSala_*`).
- `Models.RandomizerSettings` (362 métodos, 178 campos) modela toda la configuración por secciones.
- La ROM randomizada resultante queda vinculada a la sala en `vinculos.json` y su manifiesto
  por huella en `manifiestos\por-huella\`.

---

## 7. Actualizaciones (Velopack)

- `App.Main` llama a `VelopackApp.Build/Run` (hook de instalación).
- `UpdateService` usa `UpdateManager` con fuente **`https://img.pokelocke.com/releases`**:
  comprueba espacio en disco, descarga el paquete completo (`-full.nupkg`) y
  `ApplyUpdatesAndRestart`. El usuario puede saltarse una versión (`skip-update-version.txt`).
- El instalador que dejaste (`PokeLobby.Launcher-win-Setup.exe`, 265 MB) es el instalador
  Velopack con `vendor\` completo (emuladores + JRE + randomizer incluidos).

---

## 8. Cómo conectar tu propia app — 3 rutas

### Ruta A — Cliente propio contra el servidor real de Pokelobby
Implementa en tu app:
1. `POST /api/auth/login` (`{email, password}`) o flujo OAuth (loopback `/callback/` + exchange) → guarda `refreshToken`.
2. `POST /api/auth/refresh` para obtener access tokens (repite al recibir 401).
3. `GET /api/salas/mis-salas` → `PATCH /api/salas/activa` para elegir sala.
4. Sube progreso con `POST /api/saves/showdown` (resumen de partida; el campo `Export` es texto
   Showdown del equipo).
5. Escucha cartas: hub SignalR `{API}/hubs/notificaciones`, evento `CartaRecibida`; consulta los
   detalles con los GET de items/intercambios.
6. Presencia: `POST /api/saves/presencia-sala` al entrar, `DELETE` al salir.

### Ruta B — Servidor propio compatible con el launcher oficial
Como la URL base se lee de `POKELOBBY_API`, puedes escribir **tu propio backend** que implemente:
los endpoints del §2.3 + hub `/hubs/notificaciones` con evento `CartaRecibida`, y ejecutar el
launcher oficial con `POKELOBBY_API=https://tu-servidor` (los payloads concretos están en §2.3/§3).
El launcher no valida la identidad del servidor más allá de la URL, así que tu backend controla
el juego completo (salas, cartas, economía, integridad).

### Ruta C — Recrear el launcher completo (EN CURSO)

La recreación ya tiene base operativa en `src\` (.NET 10, compilada y verificada contra el
servidor real, 4/4 pruebas del protocolo):

| Proyecto | Contenido |
|---|---|
| `src\PokeLobby.Client` | Cliente REST completo (login/refresh con reintento automático en 401, salas, showdown, integridad, vivo, items, asistente, OAuth) + hub SignalR `/hubs/notificaciones` con reconexión automática y evento `CartaRecibida` |
| `src\PokeLobby.Watcher` | Vigilante de `.sav/.dsv/.sa1/.sa2/main` con huellas SHA-256 y `huellas.json` compatible con el formato del original + resumen PKHeX idéntico al `SaveResumen` + exportación Showdown + constructor del payload de subida |
| `src\PokeLobby.Emulator` | Lanzador de emuladores del `vendor\` (mGBA/melonDS/azahar, externo `{rom}` para Switch, adopción de proceso abierto) + ajustes de `qt-config.ini` de azahar con fusión no destructiva + cliente GDB del stub (127.0.0.1:3000, RSP) + exploración RAM en vivo con las constantes verificadas del original |
| `src\PokeLobby.Randomizer` | Puente con el randomizer: modelo `.rnqs` (Java properties, 168 claves con paso completo) + servicio que lanza `PokeRandoZX.jar` vía `RandoBridge` con el JRE del vendor (`-Xmx4608M`), exit codes interpretados y salida `<rom>_randomized_<yyyyMMdd-HHmmss>` |
| `src\PokeLobby.Demo` | Verificación inocua 4/4 contra el servidor real (`dotnet run --project src\PokeLobby.Demo`), `--resumen <save>`, `--emu-info`, `--gdb-test`, `--randomizer-info`, `--randomizar <rom> <preset>`, `--watch <dir>` y `--login <email>` (flujo real con escucha de cartas) |

Construcción verificada: `dotnet build src\PokeLobby.slnx` → 0 errores; el demo valida login
(401 con contrato de error), refresh, mis-salas (Bearer) y negotiate del hub (Bearer).
Para apuntarlo a tu propio backend: `$env:POKELOBBY_API="https://tu-servidor"`.

**Verificación del pipeline PKHeX (ronda 7):** el resumen de partida del proyecto recreado
(PKHeX.Core 26.5.5, la misma versión que embarca el launcher) coincide **al 100 %** con el
`SaveResumen` que el launcher original guardó en `huellas.json` para la misma partida de
HeartGold randomizada: Entrenador (Draco), TrainerId (4281185264), Medallas (8), Dinero
(125.820), Segundos jugados (271.167), equipo completo 6/6 con todos los campos exactos
(especie, mote, nivel, IVs — orden HP/Atk/Def/SpA/SpD/Spe —, habilidad, ataques, objeto) y
bolsa con 210/210 entradas idénticas. El export Showdown del equipo también se genera
(`--resumen <save>`).

**Verificación de emuladores y GDB (ronda 8):** `--emu-info` resuelve los emuladores reales
del `vendor\` (mGBA.exe/melonDS.exe/azahar.exe presentes) y la fusión de `qt-config.ini`
pasa 6/6 comprobaciones (use_gdbstub=true + \default, custom_textures=true + \default,
check_for_update_on_start=false + \default, preservando claves ajenas y creando secciones
nuevas). `--gdb-test` valida el cliente RSP contra un stub simulado 3/3 (handshake
`qSupported:multiprocess-;swbreak+` → `?` → `c`, lectura fragmentada de 8200 bytes en
trozos ≤4096 con checksum `x2`, lectura corta).

**Verificación del randomizer (ronda 9):** el vendor incluye el **fuente Java del propio
puente** (`bridge\RandoBridge.java`, más AbilityPatch/AbilityQuery/EvolutionPatch/EvoQuery/
ShinyPatchQuery/TmQuery/AbilityOffsetBridge.java) y 4 presets `.rnqs` — el contrato se tomó
de ahí, sin adivinar: `java -cp "PokeRandoZX.jar;<bridge>" RandoBridge <config.properties>
<rom_entrada> <rom_salida>`, exit codes 1=uso/2=ROM no soportada/3=no cargada/4=excepción,
«SUCCESS» en stdout, properties con 168 claves. `--randomizer-info` verifica la
infraestructura (JRE propio responde: OpenJDK 17.0.20; PokeRandoZX.jar y RandoBridge.class
presentes; ida y vuelta del `.rnqs` con 168 claves). **Randomización real ejecutada** con
`--randomizar` sobre la HeartGold original del usuario (SHA-256 de entrada
`be9e38b7…` — idéntico al `RomHash` registrado por el launcher original en `huellas.json`)
con el preset `solo_pokemon`: exit 0 en 6 s, «SUCCESS», ROM de salida con igual tamaño y
hash distinto (randomizada). Nota de implementación: las rutas del classpath y de los
argumentos deben ser absolutas (Java las resuelve contra su propio directorio de trabajo).

**Verificación de la RAM-vivo (rondas 10-11):** el `RamVivoService` se reconstruyó con la
lógica **exacta del código descompilado del original** (ilspycmd sobre PokeLobbyLauncher.dll,
fuente conservada en `.rea-setup\RamVivoService-decompiled.cs`): puerto GDB **24689** (el
gdbstub de azahar; el 3000 de las anclas CIL era el timeout de conexión); entrada viva de
484 bytes = **PK6[0..232] + tráiler de 28 bytes en 344** (con HP actual/máx del estado de
party); validación del equipo porque **cada entrada empieza con la EncryptionConstant** del
Pokémon correspondiente del .sav; decodificación con chequeos checksum/especie 1..721/
nivel 1..100 con Stat_Level==CurrentLevel/HP 1..999 y actual ≤ máx; exploración del heap
0x08000000→0x0A000000 en páginas de 0x1004 con la EC como aguja; detección de **caídos**
(HP >0 → 0) y **encuentros** (rival capturado/derrotado/sin_determinar) con el perfil XY
(000400000011C500 → ruta 148072308, combate 147973104, rival 142622448), rutas vía
GameInfo.GetLocationName, cola de 200 y envío por lotes cada 5 s a `POST /api/saves/vivo`.
**Verificado 10/10** con un Pokémon real de la partida del usuario (PID 4003819604
«Telefónica», especie 466, nivel 70, HP 207/207 del tráiler, mote conservado).

**Integración con PokeLocke Android (ronda 12, en vivo contra Firebase real):** nuevo
proyecto `src\PokeLobby.PokeLocke` con los DTOs del esquema real de Firebase
(`/lockes/{lockeId}` — extraído del export del usuario: locke Escudo «Debug»
`5485c9b3…`, jugadores `el_2861` y `Kakkaroto_9062` con inventarios CARTA/SOBRE reales),
cliente RTDB por REST con **auth anónima automática** (API key → identitytoolkit signUp →
ID token renovado en 401; el `expiresIn` de la respuesta llega como string), SSE en tiempo
real verificado contra la base de datos viva (`event: put`), e **inyección del equipo de la
app en el .sav** (`InyectorEquipo`: naturaleza gen 3-5 por PID con `SetPIDNature`,
habilidad/movimientos por nombre en español vía reverse-lookup de GameStrings, IVs/EVs de
los mapas, shiny) **verificada 7/7** con el locke real + el myTeam del doc. Hallazgo clave:
fijar `pkm.Version = sav.Version` antes de tocar el PID (con `Version=Any`,
`EntityPID.GetRandomPID` devuelve un PID aleatorio ignorando naturaleza/habilidad). El motor
`EfectosCartas` cubre los 13 efectos documentados (IDs 63-84: EVs/IVs, dinero, mentas, MTs,
revive, mentor, genética). Config por variables de entorno: `POKELOCKE_FIREBASE_URL`,
`POKELOCKE_FIREBASE_API_KEY` (o `POKELOCKE_FIREBASE_AUTH` para un token fijo).

Pendiente del launcher completo: solo la UI WPF. La base de protocolo, el pipeline de
progreso, los emuladores, el cliente GDB, el puente del randomizer, la RAM-vivo y la
integración con PokeLocke Android ya están operativos y verificados.

### Verificación en vivo del protocolo (sonda inocua, sin credenciales reales)

Probado contra `https://api-pokemon-main-a21032e.onrender.com` (detrás de Cloudflare) con
peticiones que **no usan tu cuenta** (script `.rea-setup\probe.mjs`):

| Petición | Respuesta | Qué demuestra |
|---|---|---|
| `GET /` | 404 sin body | rutas desconocidas → 404, luego los 401 prueban rutas reales |
| `POST /api/auth/login` (email/password inventados) | 401 `{"error":"Email o contraseña incorrectos."}` | endpoint existe; **acepta claves camelCase**; contrato de error `{"error":"<mensaje>"}` |
| `POST /api/auth/refresh` (`{"refreshToken":"…"}` falso) | 401 `{"error":"Refresh token inválido o expirado."}` | endpoint existe; la clave `refreshToken` (camelCase) se parsea |
| `GET /api/salas/mis-salas` (sin token) | 401 + `WWW-Authenticate: Bearer` | ruta protegida por Bearer |
| `POST /hubs/notificaciones/negotiate?negotiateVersion=1` (sin token) | 401 + `WWW-Authenticate: Bearer` | **el hub SignalR existe en esa ruta y exige Bearer** |
| `GET /swagger/v1/swagger.json` | 500 `{"statusCode":500,"message":"Error interno del servidor."}` | hay ruta swagger (rota); manejador de errores JSON propio |
| `GET /health` | 404 | sin health público |

Conclusiones: **ASP.NET Core** tras Cloudflare; respuestas `application/json; charset=utf-8`;
errores de negocio con clave `"error"` y mensajes en español; peticiones camelCase aceptadas
(ASP.NET Core además ignora mayúsculas al enlazar). El casing de las peticiones, por tanto, no es
un riesgo.

### Incógnitas restantes
- La forma exacta de las **respuestas autenticadas** (mis-salas, inventario, cartas, login OK…):
  se confirmará al probar con sesión real o al construir tu propio backend con libertad sobre ellas.
- Trato de `refreshToken` como secreto: está en tu `session.json` — no lo compartas ni lo subas a repos.

---

## Anexo — Cómo se obtuvo esto (comandos REA)

```powershell
rea inspect-managed-artifact  "PokeLobby.Launcher\current\PokeLobbyLauncher.dll" --json
rea inspect-managed-members   "PokeLobby.Launcher\current\PokeLobbyLauncher.dll" --json          # 518 tipos, 3.846 métodos, 28.331 aristas de llamada, 3.830 literales
rea inspect-managed-members   "PokeLobby.Launcher\current\PokeLobbyLauncher.dll" --type PokeLobbyLauncher.Services.AuthService --json
rea inspect-managed-native-boundaries "PokeLobby.Launcher\current\PokeLobbyLauncher.dll" --json   # IL-only, 0 P/Invoke
```

Archivos de trabajo derivados (en `.rea-setup\`): `members.json` (inventario completo),
`method-strings.txt` (mapa método → literales, 627 métodos), `app-types.txt` (89 tipos de la app),
`svc-members.json`, `native-boundaries.json`.

> **Nota legal**: Pokelobby es software de terceros (launcher con licencias propias — ver
> `LicensesPage` en la app). Este análisis es estático y local, para interoperabilidad con tu
> propia app. No se incluyó ninguna credencial en este informe.
