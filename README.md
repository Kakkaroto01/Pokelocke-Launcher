# LockeLauncher — Recreación de PokeLobby en C#/.NET 10

Recreación completa del launcher de Pokelobby a partir de ingeniería inversa con REA
(Reverse Engineer Anything). Incluye la integración con **PokeLocke Android** (Firebase).

## Proyectos

| Proyecto | Qué contiene |
|---|---|
| `PokeLobby.Client` | Cliente REST completo del protocolo (auth + refresh automático en 401, salas, partidas/showdown, integridad, vivo, items, asistente, OAuth Google/GitHub) + hub SignalR `/hubs/notificaciones` con reconexión automática y evento `CartaRecibida`. |
| `PokeLobby.Watcher` | Vigilante de partidas `.sav/.dsv/.sa1/.sa2/main` con huellas SHA-256 y `huellas.json` compatible con el formato del original + resumen PKHeX idéntico al `SaveResumen` + exportación Showdown + constructor del payload de subida. |
| `PokeLobby.Emulator` | Lanzador de emuladores del `vendor\` (mGBA/melonDS/azahar, externo `{rom}` para Switch, adopción de proceso abierto) + ajustes de `qt-config.ini` de azahar con fusión no destructiva + cliente GDB del stub (127.0.0.1:24689, RSP) + exploración RAM en vivo con las constantes verificadas del original. |
| `PokeLobby.Randomizer` | Puente con el randomizer: modelo `.rnqs` (Java properties, 168 claves) + servicio que lanza `PokeRandoZX.jar` vía `RandoBridge` con el JRE del vendor (`-Xmx4608M`), exit codes interpretados y salida `<rom>_randomized_<timestamp>`. |
| `PokeLobby.PokeLocke` | **Integración con PokeLocke Android**: DTOs del esquema Firebase (`/lockes/{lockeId}`), cliente RTDB por REST + SSE en tiempo real con auth por cuenta (email/password, refresh token), **inyección del equipo de la app en el .sav** y motor de efectos de cartas (13 efectos documentados). |
| `PokeLobby.LauncherUi` | **La interfaz gráfica** (WPF, español, tema oscuro): biblioteca con carátulas, ▶ JUGAR (emulador + watcher + resumen Showdown en vivo), 🎲 Randomizar con presets, lockes de Firebase en vivo (SSE) con inyección del equipo de la app al .sav, login con la cuenta de la app, y registro de actividad. |
| `PokeLobby.Demo` | Consola de verificación: sonda inocua 4/4 contra el servidor real, `--resumen`, `--emu-info`, `--gdb-test`, `--randomizer-info`, `--randomizar`, `--ramvivo-test`, `--inyectar-equipo`, `--pokenete-leer`, `--watch`, `--login`. |

## Requisitos

- **.NET SDK 10.0** (o 10.0.x con soporte net10.0-windows)
- **PKHeX.Core 26.5.5** (NuGet — se restaura automáticamente)
- **Carpeta `vendor\`** de una instalación de Pokelobby (emuladores, JRE, randomizer)
  → se configura en ⚙ Ajustes de la UI; por defecto busca subiendo en el árbol de directorios.
- **Carpeta de datos** `%APPDATA%\PokeLockeLauncher\datos\` (library.json, huellas.json, covers)
  → la usa por defecto, configurable en ⚙ Ajustes. Si existe una carpeta de datos del launcher
  original (`%LOCALAPPDATA%\PokeLobby\Launcher`) se **copian** los datos la primera vez (sin borrar
  nada de la original).

## Construir

```powershell
dotnet build src\PokeLobby.slnx
```

## Arrancar la UI

```powershell
src\PokeLobby.LauncherUi\bin\Debug\net10.0-windows\PokeLockeLauncher.exe
```

En ⚙ Ajustes configura:
- **Firebase RTDB — URL** y **Web API Key** (Firebase Console → Configuración del proyecto)
- **Carpeta vendor\** (donde están mGBA, melonDS, azahar, SuperRandomizer, jre)

## Cuenta PokeLocke

La UI inicia sesión con el **mismo email y contraseña de la app Android**
(Firebase Email/Password). El launcher muestra solo los lockes de tu cuenta
(`user_lockes/{uid}`), resuelve tu playerTag desde `users/{uid}`, y guarda un
**refresh token** (nunca la contraseña) para no pedir credenciales en cada arranque.

## Verificación rápida

```powershell
# Sonda inocua contra el servidor real (sin credenciales) — 4/4 esperado:
dotnet run --project src\PokeLobby.Demo

# Verificación completa de la RAM-vivo con tu partida real:
dotnet run --project src\PokeLobby.Demo -- --ramvivo-test <ruta-save.sav>

# Test del cliente GDB contra stub simulado — 3/3:
dotnet run --project src\PokeLobby.Demo -- --gdb-test

# Estado de emuladores y fusión de config de azahar — 6/6:
dotnet run --project src\PokeLobby.Demo -- --emu-info

# Estado del randomizer:
dotnet run --project src\PokeLobby.Demo -- --randomizer-info
```

## Inyección del equipo de la app en la partida

Con un locke conectado y una ROM seleccionada, el botón «Inyectar equipo de la app»
lee `myTeam` de Firebase y construye los PKM con PKHeX (naturaleza, habilidad,
movimientos, IVs, EVs, shiny) para escribirlos en el archivo de partida.

Para probarlo sin la app desde un JSON:

```powershell
dotnet run --project src\PokeLobby.Demo -- --inyectar-equipo <save.sav> <locke.json> <playerTag>
```

## Documentación

Ver `INFORME-PokeLobby.md` para el protocolo completo, la arquitectura del original,
la evidencia REA y las decisiones de implementación.

## Cola launcherQueue

- El launcher procesa acciones de la cola en Firebase (CARD_EFFECT, GACHA_POKEMON, ITEM_GRANT, MINT_USE): las reclama con ETag condicional, las aplica de forma segura sobre el .sav (con backup, verificación y regeneración de huella) y las marca como applied/failed/skipped para que la app las muestre. Si el emulador está abierto, las acciones se acumulan y se aplican al cerrarlo.
- Pruébalo con la cola de ejemplo incluida:

```powershell
dotnet run --project src\PokeLobby.Demo -- --cola-test <save.sav> ejemplos\acciones-ejemplo.json
```

- **Nota de compilación (.NET 10)**: por un bug del SDK con `.slnx` + múltiples referencias de proyecto, los proyectos `PokeLobby.Demo` y `PokeLobby.LauncherUi` compilan los fuentes del resto **inline** (ver sus `.csproj`). Compila cada proyecto directamente:
  `dotnet build src\PokeLobby.Demo\PokeLobby.Demo.csproj` y `dotnet build src\PokeLobby.LauncherUi\PokeLobby.LauncherUi.csproj`.
- **Login**: en la ventana de sesión puedes escribir tu **nombre de usuario de la app** (se construye `{usuario}@pokelocke.com`) o un email completo.

## Cola launcherQueue — contrato v1.1 (correcciones PROMPT 4)

- **Conectada a la UI**: contador de pendientes en la cabecera, botón **⚡ Aplicar pendientes** (solo con el emulador cerrado), y aplicación automática **antes de ▶ JUGAR** con lista y confirmación. La suscripción SSE avisa en cuanto la app crea una acción.
- **Escritura real en el .sav**: las ediciones se escriben por slot (`SetPartySlotAtIndex` / `SetBoxSlotAtIndex`) y la mochila con una única instancia de `PlayerBag` + `CopyTo`. Los Pokémon de **caja** también son objetivos (el «muerto» de un Nuzlocke suele estar ahí).
- **Verificación en dos fases**: (1) semántica **inmediata por acción** sobre el save en memoria, con **rollback por snapshot** si falla; (2) **persistencia**: tras escribir se **relee el .sav de disco** y se exige que coincida byte a byte con lo aplicado. Solo entonces se marca `applied` y se apunta en `acciones-aplicadas.txt`.
- **Claim por acción** (v1.1.g): GET del nodo individual con `X-Firebase-ETag` → PUT del objeto **completo** con `status:"applying"`, `claimedAt`, `claimedBy` e `if-match` (412 = otro launcher se la quedó). Un `applying` con más de **10 minutos** se reprocesa.
- **Cancelar ≠ fallar** (v1.1.c): un diálogo cancelado deja la acción en `pending` y el resto del lote continúa.
- **Cartas 87/88 (rival)** (v1.1.b): llegan en TU cola con `params.sourcePlayer`, `stats` y `targetSpeciesId`; se localiza al Pokémon por especie entre equipo y cajas y se le ponen los IV a 0. Sin coincidencias → sigue `pending` («No encuentro el Pokémon objetivo»).
- **Zenkai** (v1.1.f): usa `params.stats` (2 stats) o pregunta cuáles; revive con `Heal()` (HP, PP y estado) y avisa de lo que no se puede restaurar (registro de caídos).
- **Gachapón**: PID/EC según generación respetando naturaleza, **TID/SID antes de `SetShiny`** (si no, dejaba de ser shiny), IVs aleatorios, bola aplicada (avisa si se adapta), EXP real para el nivel, movimientos legales vía `MoveListSuggest`, stats de equipo recalculadas, **registro en la Pokédex** y aviso de legality.
- **Objetos**: `ITEM_GRANT` usa `GiveItem` sobre el bolsillo correcto (`Info.GetItems`), reutiliza stacks o busca hueco libre, respeta el tope del bolsillo y nunca quita nada.
- **DTO tolerante**: `appItemId` acepta **número** (Android) o string numérico; las entradas ilegibles se registran y se marcan `failed` con el motivo (no desaparecen en silencio).
- **Validación de juego** por **conjuntos de versiones** (PKHeX devuelve combinadas: DP, RS, HGSS, FRLG, BW, XY…). Corregidos Omega Ruby→OR, Alpha Sapphire→AS y «Perla Reluciente»→SP.

## Tests (`src\PokeLobby.Tests`)

70 tests xUnit (xunit.v3 + Microsoft.Testing.Platform):

```powershell
# en una máquina normal:
dotnet test src\PokeLobby.Tests\PokeLobby.Tests.csproj

# en esta sesión (la sandbox bloquea los named pipes del puente MSBuild↔test):
src\PokeLobby.Tests\bin\Debug\net10.0\PokeLobby.Tests.exe -noColor
```

Cubren: deserialización del JSON real de Android (5/5 acciones, `appItemId` número y string, cola nula/array con huecos, entrada inválida registrada), los 28 nombres de juego con versiones combinadas, **cada efecto del catálogo releído desde disco** sobre un fixture de HeartGold, las mismas operaciones sobre saves en blanco de Gen 5/6/7, cancelación→pending, orden por `createdAt`, idempotencia, claim rechazado/caducado, juego distinto, reparto inválido y el cliente Firebase con `HttpMessageHandler` simulado (412, PUT completo con `if-match`, PATCH tras 401 con contenido regenerado).

## Correcciones PROMPT 6 (checksums y cola)

- **BLOQUEANTE corregido**: `InyectorEquipo.GuardarSave` ahora serializa con el pipeline de PKHeX (`sav.Write(BinaryExportSetting.None)` → `SetChecksums` + `Metadata.Finalize`), conservando el tamaño y las cabeceras/pies del formato (DeSmuME, 3DS…). Antes se escribía `sav.Data` crudo, que dejaba `ChecksumsValid == false` y el juego habría tratado la partida como dañada. Afecta a la cola **y** al botón «Inyectar equipo».
- **Verificación reescrita**: tras escribir se **relee el .sav de disco** y se exige `releido.ChecksumsValid == true` **y** que los verificadores semánticos de cada efecto pasen sobre el save releído. Si alguno falla se restaura el backup y se marca `failed`. Los verificadores se ejecutan también inmediatamente tras cada acción (con rollback por snapshot) para detectar el error en el origen.
- **Aviso/reparación en la UI**: al arrancar, si una partida del catálogo tiene huella «launcher-aplicó» y `ChecksumsValid == false` (escrita por la versión anterior), se ofrece repararla: copia `.bak-reparacion-…` y reescritura con checksums válidos.
- **Entradas ilegibles**: además de registrarse en el log, se marcan `failed` en Firebase con el motivo («JSON inválido: …») para que el jugador lo vea en el móvil.
- **Lote robusto**: una acción `failed` (p. ej. especie inexistente) no impide guardar las demás; `acciones-aplicadas.txt` solo contiene las aplicadas **y verificadas**.
- **Cartas 87/88 sin objetivo**: el `resultMessage` llega al móvil y se cuentan los intentos en `intentos-objetivo.txt`; tras **3** intentos se avisa en el log indicando la especie buscada.

## Tests (PROMPT 6)

- `ChecksumsTests.Regresion_ChecksumsValidos_TrasCarta70GachaEItem`: carta 70 + Gachapón + ITEM_GRANT con el servicio real sobre una copia de `testdata/hgss.sav` → `ChecksumsValid == true` al releer, valores cambiados, **mismo tamaño** y **backup byte a byte idéntico** al original.
- `GeneracionesEnBlanco_WriteProduceChecksumsValidos` y `GeneracionesTests.*`: gen 5/6/7 en blanco → `sav.Write()` deja `ChecksumsValid == true` y el tamaño no cambia.
- `EntradaIlegible_SeMarcaFailedEnFirebase`, `AccionFallida_NoImpideLasDemas_Y_IdempotenciaSoloAplicadas`, `Carta87_SinObjetivo_AvisaTrasTresIntentos`.

## Rediseño y paridad con PokeLobby original (evidencia REA)

Revisión con REA de `PokeLobbyLauncher.dll` v0.1.40 (SHA-256 `53aaae6e…`): 7 secciones
(Home/Library/Inventory/Randomizer/Settings/Licenses/Ayuda), `AddRomDialog`, `RandomizerPage`
(194 claves del `.rnqs` en ~30 grupos), `SettingsPage` con secciones plegables, `AccentThemeService`
(16 acentos), `BackgroundThemeService` (8 fondos + imagen/vídeo mp4 con audio y volumen),
`EmuladorExternoService` (`emulador_switch.json`, argumentos `"{rom}"`), sonidos de navegación
sintetizados y `RomCard`.

Implementado ahora:

- **UI nueva**: barra lateral de navegación (Inicio/Biblioteca/Locke/Randomizador/Ajustes),
  tarjetas redondeadas, carátulas en la biblioteca, tema oscuro limpio y barra de estado.
- **Añadir ROMs** (`AddRomDialog`): archivo, nombre, consola detectada por extensión
  (GBA/DS/3DS/Switch), carpeta de partidas y carátula; aviso si es Switch sin emulador.
- **Randomizador con ventana propia** (`RandomizerWindow`): TODAS las claves del `.rnqs`
  agrupadas por categorías, editor por tipo (`.mod` combo, booleanos checkbox, números…),
  filtro «solo cambiadas», cargar/guardar `.rnqs`, presets del vendor y ejecución con registro;
  la ROM resultante se añade a la biblioteca.
- **Personalización del launcher** (`SettingsWindow`): 16 acentos, 8 fondos, imagen o **vídeo
  de fondo** con sonido y volumen, sonidos de UI, carpetas, Firebase.
- **Emulador de Switch configurable**: `.exe` + argumentos con `{rom}` (sustitución idéntica al
  original: si la plantilla no tiene `{rom}` se añade la ruta entrecomillada), bloqueo de elegir
  el propio launcher y avisos de ruta/argumentos inválidos. Se usa al pulsar ▶ JUGAR.

Tests: `LauncherConfigTests` (detección de consola por extensión, 12 extensiones, sustitución
de `{rom}` con y sin plantilla, rechazo del launcher como emulador, `Configurado` requiere .exe).

## Correcciones tras el primer uso (biblioteca y lanzamiento del emulador)

- **«Access to the path …library.json is denied»**: la carpeta `%LOCALAPPDATA%\PokeLobby\Launcher`
  es escribible, pero la instancia que estaba abierta se había lanzado desde un shell restringido
  (heredaba la sandbox: AppData y E: bloqueados). Ahora el launcher **comprueba la escritura al
  arrancar** y, si falla, avisa, lo registra y ofrece **elegir otra carpeta de datos** en el momento
  (`GuardarBiblioteca`), en lugar de lanzar una excepción.
- **El emulador se abría sin la ROM**: `EmulatorService.Lanzar` **adoptaba** cualquier mGBA/melonDS/
  azahar ya abierto y devolvía ese proceso. El original solo *detecta* un emulador abierto al
  iniciar (para el estado) y **siempre lanza con la ROM**: `Arguments = "\"" + romPath + "\""`
  (evidencia REA de `EmulatorService.Launch`). Corregido: ya no se adopta nada; los argumentos se
  construyen con `LanzamientoEmulador.Argumentos` (DS/GBA/3DS → ROM entrecomillada; Switch → plantilla).
- **Si ya hay un emulador abierto** al pulsar ▶ JUGAR, ahora se explica que no se inyectan acciones
  mientras corre y se ofrece abrirlo igualmente con la ROM (antes se abortaba en silencio).
- **«Unable to write to DS save» (melonDS)**: además de la causa de permisos, el launcher ahora
  **comprueba que la carpeta del .sav sea escribible ANTES de lanzar** y avisa con la ruta exacta.

Tests nuevos: `ArgumentosLanzamiento_*` (DS/GBA/3DS/Switch) y `PermisosCarpeta_DetectaEscritura`.

## Carpetas: ROMs vs. datos del launcher

- **Las ROMs se quedan donde estén** (p. ej. `E:\...\Nintendo\Pokemon\HeartGold\`). El launcher solo
  guarda su **ruta**; no las copia ni las mueve. El `.sav` debe estar en la misma carpeta que la ROM
  (o indicar «carpeta de partidas» al añadirla).
- **Carpeta de datos** (solo metadatos: `library.json`, `huellas.json`, carátulas): por defecto
  `%APPDATA%\PokeLockeLauncher\datos` (`%APPDATA%\PokeLockeLauncher\ajustes.json` para los ajustes).
  Está en **Roaming** a propósito: el programa se instala en `%LOCALAPPDATA%\PokeLockeLauncher` y
  Velopack borra esa carpeta entera al desinstalar; los datos de `%APPDATA%` sobreviven.
  Si no fuese escribible, el launcher **se auto-repara**: migra los datos a
  `Documentos\PokeLockeLauncher` (o junto al exe / TEMP) y lo avisa una sola vez.
- **Carátulas incluidas**: el paquete trae 25 carátulas en `covers\` junto al ejecutable
  (en una instalación Velopack, `…\current\covers`). En el primer arranque se copian a la carpeta
  de datos **las que falten**; una carátula puesta a mano (p. ej. la del Añil) nunca se sobrescribe.
- Inicio recomendado: doble clic en **`Iniciar-PokeLockeLauncher.cmd`** (arranca con los permisos
  normales del usuario; una instancia lanzada desde un entorno restringido no puede escribir).

## Actualización REA v0.1.43 y correcciones de la UI (turno actual)

**REA sobre el original actualizado**: `PokeLobbyLauncher.dll` v0.1.40 → **v0.1.43**
(SHA-256 `0e8033af…`, 585 TypeDefs). Novedades detectadas: `RandomizerRecientesService`
(marca ROM/salas usadas recientemente en el randomizador) y **199 claves de ajustes** en
`RandomizerSettings.ToPropertiesText()` (antes se documentaban 194). Los valores por defecto se
extrajeron del propio método descompilado (tipos `string`/`bool`/`int`, 21 grupos) y quedaron en
`PokeLobby.Randomizer/AjustesDefecto.cs`, de donde `new RandomizerSettings()` los siembra.

**Correcciones**
- **El aviso de carpeta de datos ya no sale en cada arranque**: `ajustes.json` se guarda en la
  primera ruta escribible (AppData → junto al exe → Documentos). En tu equipo AppData está
  bloqueado, así que ahora vive junto al ejecutable y la carpeta de datos (`…\net10.0-windows\datos`)
  se conserva entre arranques. Puedes cambiarla en ⚙ Ajustes.
- **Biblioteca = ventana propia** (`BibliotecaWindow`): lista con carátulas, **Añadir ROM**,
  **🗑 Borrar** (con confirmación y sin tocar los archivos), Recargar, **JUGAR** y **Randomizar**.
- **Randomizador dentro de la ventana** (pestaña `RandomizadorControl`, ya no otra ventana) con
  las **199 opciones** del UPRZX agrupadas en 21 categorías, editor por tipo, filtro «solo
  cambiadas», presets del vendor, cargar/guardar `.rnqs` y ejecución con registro.
- **Navegación real**: Inicio/Locke muestran el panel de inicio; Randomizador cambia de pestaña;
  Biblioteca abre su ventana; Ajustes abre la ventana de personalización.

Tests: `RandomizerDefectosTests` (199 claves, defectos exactos, Cargar sobrescribe y conserva
desconocidas, properties completo, candidatas de carpeta) — **119 tests en total**.

## Correcciones y mejoras (paquete actual)

- **Autenticación por usuario**: la UI pide «Usuario» y «Contraseña»; internamente se usa
  `usuario@pokelocke.com` (evidencia REA de la app Android). `CuentaPokelocke` normaliza
  (minúsculas, sin acentos, acepta email pegado) y valida longitud 3–32 y caracteres admitidos;
  se conservan refresh token, restauración de sesión y cierre de sesión.
- **Desplegables legibles**: causa = plantilla por defecto de WPF (popup blanco del sistema).
  Ahora ComboBox/ComboBoxItem tienen plantilla propia oscura con hover, seleccionado, foco,
  deshabilitado y desplazamiento; afecta a todos los desplegables (biblioteca, ajustes, randomizador).
- **Crear Locke eliminado** de la UI y de su handler; la consulta y uso de lockes existentes sigue igual.
- **Biblioteca integrada** en la ventana principal (sección de la barra lateral), con añadir,
  **borrar** (solo de la lista), recargar, JUGAR, randomizar y cambiar carátula.
- **Carátulas**: detección del juego por el nombre de la ROM (`DetectorJuegoRom`), búsqueda
  automática en `<datos>/covers` (`Caratulas.Buscar`), selección manual con carpeta recordada
  (`UltimaCarpetaCaratulas`), opción de continuar sin carátula y de cambiarla.
- **Partidas**: `LocalizadorPartidas` busca por **nombre base** de la ROM (convención del emulador),
  respeta la carpeta configurada, usa la huella exacta y solo acepta una partida suelta si no hay
  otra ROM en la carpeta. Si aún no existe el `.sav`, se permite jugar: el emulador la crea al
  guardar (se comprueba carpeta y permisos antes).
- **Randomizador**: navegación por categorías (20 grupos), un único ScrollViewer (rueda del ratón
  arreglada), controles por tipo (casilla / desplegable / barra deslizante / campo numérico),
  valores visibles, persistencia entre categorías, restablecer por categoría, aviso de
  compatibilidad por generación y conexión al motor real (`RandomizerSettings` → RandoBridge).

Verificación: `dotnet build src\PokeLobby.slnx` → 0 errores; `PokeLobby.Tests` → **155 tests**.
Randomizado real de extremo a extremo sobre una COPIA de la ROM (preset `full_random`):
SUCCESS, salida de 126.561.248 bytes (mismo tamaño), sin tocar los archivos del usuario.

## Reparto a los jugadores y actualizaciones

### Generar el paquete

```powershell
pwsh -File distribuir.ps1 -Version 0.2.0
```

- Publica en `Release` **autocontenido** (`win-x64`): **no** hace falta instalar .NET en el
  equipo de destino. Resultado: `..\dist\win-x64\` (~159 MB, 415 archivos) y
  `..\dist\PokeLockeLauncher-0.2.0-win-x64.zip` (~68 MB).
- Con `-FrameworkDependent` el paquete baja a ~20 MB, pero exige el **.NET 10 Desktop Runtime**.
- El ZIP incluye `LEEME.txt` con las instrucciones para el jugador.

> El script pasa `--source https://api.nuget.org/v3/index.json` porque `src\NuGet.Config`
> limpia los orígenes (las compilaciones normales tiran de la caché local). Solo hace falta
> conexión la primera vez que se descargan los *runtime packs*.

### Qué viaja y qué no

| Contenido | Va en el ZIP | Dónde vive |
|---|---|---|
| Binarios + runtime .NET | Sí (~159 MB) | carpeta descomprimida |
| `ajustes.json` del jugador | **No** | `%APPDATA%\PokeLockeLauncher\` (o junto al exe si no es escribible) |
| `library.json`, `huellas.json`, `covers\` | **No** | igual (por defecto `%APPDATA%\PokeLockeLauncher\datos`) |
| Carátulas incluidas (25, ~10 MB) | **Sí** | `covers\` junto al ejecutable; se siembran en la carpeta de datos sin pisar las propias |
| `vendor\` (mGBA, melonDS, azahar, JRE, SuperRandomizer) | **No** (~381 MB) | se reutiliza la instalación oficial |
| Configuración de Firebase | Sí (valores públicos por defecto) | `Ajustes.FirebaseUrlPorDefecto` / `FirebaseApiKeyPorDefecto` |

`Ajustes.BuscarVendorDir()` prueba, en este orden: variable `POKELOCKE_VENDOR_DIR`, `vendor\`
junto al ejecutable, la instalación oficial en `%LOCALAPPDATA%\PokeLobby.Launcher\current\vendor`
(y `PokeLobbyLauncher`, `Program Files`), y por último subiendo por el árbol de directorios.
Así quien ya juega con el launcher oficial no necesita copiar 381 MB.

> El `vendor\` son emuladores y un JRE de terceros: **no se empaqueta ni se sube a GitHub**.
> Tampoco ROMs ni `.sav` (ver `.gitignore` en la raíz).

### Actualizar a los demás

| Método | Esfuerzo | Experiencia del jugador |
|---|---|---|
| Reenviar el ZIP nuevo | nulo | descomprimir encima / carpeta nueva; conserva datos y sesión |
| GitHub Releases manual | bajo | descarga el ZIP de la release |
| **Velopack + GitHub Releases** (recomendado) | medio | el launcher se actualiza solo al arrancar (delta, reinicio) |

Velopack es lo que usa el launcher original (`Update.exe`, `sq.version`, `Velopack.dll`), así que
encaja con la arquitectura: `dotnet tool install -g vpk`, paquete `Velopack` en
`PokeLobby.LauncherUi`, `VelopackApp.Build().Run()` al inicio y un `UpdateManager` apuntando a la
release de GitHub. `publicar.ps1` genera ese instalador (`dist\velopack\releases\…-Setup.exe`) y
`Actualizador.ComprobarAsync()` avisa/instala la versión nueva al arrancar.

### Instalador o ZIP: qué recibe el jugador

| | Instalador Velopack (`publicar.ps1`) | ZIP portable (`distribuir.ps1`) |
|---|---|---|
| Archivo | `PokeLockeLauncher-<versión>-win-Setup.exe` (~250 MB con vendor) | `PokeLockeLauncher-<versión>-win-x64.zip` (~68 MB) |
| Instalación | automática, **por usuario y sin admin** | descomprimir donde quiera |
| Dónde queda | `%LOCALAPPDATA%\PokeLockeLauncher\` (`current\` con el exe, `covers\`, `vendor\`) | la carpeta elegida al descomprimir |
| Datos del jugador | `%APPDATA%\PokeLockeLauncher\` (`ajustes.json`, `datos\`); sobreviven a desinstalar | igual (o junto al exe si no es escribible) |
| Actualizaciones | **solas**, en el mismo sitio (delta + reinicio) | hay que volver a descargar y descomprimir |

Recomendado: el **instalador** para la mayoría de jugadores; el ZIP solo para quien quiera una
copia portable.

Nota: el feed de Velopack puede ser un repo público (no necesita token para leer). Para publicar
sí hace falta un token de GitHub (`vpk upload github --token …`), que no se guarda en el repo.
