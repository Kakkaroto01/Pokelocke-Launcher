# Reparto en un solo archivo y actualizaciones automáticas

Guía para que un amigo instale PokeLocke Launcher **descargando un único `.exe`**, sin
instalar antes PokeLobby, y para que las siguientes versiones le lleguen solas.

> **Estado (2/11/2026):** el proyecto ya está preparado y se ha generado
> `dist\PokeLockeLauncher-0.1.0-win-Setup.exe` (249,1 MB) con el `vendor` dentro.
> Falta solo crear el repositorio público y poner su URL real en `Actualizador.cs`
> (ahora tiene `TU_USUARIO` como marcador). **No repartas ese instalador de prueba:**
> funciona para jugar, pero se ha compilado sin URL y no se actualizará. Hay que
> regenerarlo después de configurar el repositorio.

---

## 1. La respuesta corta

- **Sí se puede** que solo descargue lo de LockeLauncher, pero el paquete tiene que incluir
  la carpeta `vendor\` (emuladores + JRE + randomizer). Son **380,7 MB** y sin ella el
  launcher abre, pero no puede jugar ni randomizar.
- La forma limpia es un **instalador Velopack** (`PokeLockeLauncher-win-Setup.exe`,
  ~249 MB, parecido al instalador oficial) con el `vendor` dentro. Se instala en el perfil
  del usuario, crea accesos directos y **se actualiza solo** desde las Releases de GitHub.
- El ZIP actual de 67 MB sigue sirviendo para probar, pero **no se autoactualiza** y no
  lleva emuladores: úsalo solo tú, no es lo que hay que repartir.

Lo que NO hay que hacer: mandar la carpeta compilada a mano, ni pedir al amigo que
instale PokeLobby primero. Eso era solo un apaño para no duplicar los 380 MB.

---

## 2. Por qué hace falta el `vendor`

`Ajustes.BuscarVendorDir()` busca, por este orden:

1. La variable de entorno `POKELOCKE_VENDOR_DIR`.
2. `vendor\` junto al ejecutable.
3. La instalación oficial de PokeLobby (`%LOCALAPPDATA%\PokeLobby.Launcher\current\vendor`).
4. Subiendo por el árbol de carpetas.

Contenido real de esa carpeta en tu equipo:

| Carpeta | Para qué |
|---|---|
| `mGBA` | Emulador de GBA |
| `melonDS` | Emulador de NDS |
| `azahar` | Emulador de 3DS |
| `jre` | Java para el randomizer |
| `SuperRandomizer` | Motor del randomizer |
| `PKHeX-Variantes` | Utilidades de PKHeX |

Si el amigo no tiene PokeLobby instalado, las opciones 3 y 4 no existen. Por eso, o
metemos `vendor` en el instalador, o el launcher no sirve para jugar.

**Consecuencia:** el instalador pesa ~249 MB (compilado y medido en este equipo; el
instalador oficial pesa 265.572.199 bytes). GitHub admite assets de hasta 2 GB, así que no
hay problema.

---

## 3. Cómo queda el reparto

```
Tú publicas una vez:
  PokeLockeLauncher-win-Setup.exe  (GitHub Release, ~249 MB)

El amigo:
  1. Descarga ese .exe.
  2. Lo ejecuta (SmartScreen -> "Más información" -> "Ejecutar de todas formas").
  3. Se instala en su perfil y se abre.
  4. A partir de ahí, al abrir el launcher comprueba si hay versión nueva,
     la descarga y se reinicia actualizado.
```

Sus datos **no** viven en la carpeta del programa:

- `%APPDATA%\PokeLockeLauncher\ajustes.json` -> ajustes, sesión, tema.
- `%APPDATA%\PokeLockeLauncher\datos\` -> `library.json`, `huellas.json`, `covers\`.

El programa instalado queda en `%LOCALAPPDATA%\PokeLockeLauncher\current\` (exe, `covers\`
y `vendor\`). Los datos van en `%APPDATA%` a propósito: al desinstalar, Velopack borra toda la
carpeta `%LOCALAPPDATA%\PokeLockeLauncher`, pero la biblioteca y tus carátulas sobreviven.
Las 25 carátulas incluidas se copian a `datos\covers` en el primer arranque, **sin sobrescribir**
ninguna carátula puesta a mano.

Por eso una actualización (o incluso desinstalar y reinstalar) no le borra la biblioteca
ni la sesión.

---

## 4. Puesta en marcha (solo una vez)

### 4.1. Crear el repositorio

1. Crea un repositorio **público** en GitHub, por ejemplo `pokelocke-launcher`.
   - Público es importante: leer las Releases no necesita token. Si es privado, habría
     que meter un token dentro del launcher, y eso es un secreto regalado.
2. No subas `vendor\`, ROMs ni `.sav`: el `.gitignore` de la raíz ya los excluye.
3. Apunta la URL: `https://github.com/TU_USUARIO/pokelocke-launcher`.

### 4.2. Crear el token de publicación

Solo lo necesitas **tú**, en tu PC, para subir releases. No va dentro de la app.

1. GitHub -> Settings -> Developer settings -> **Personal access tokens -> Fine-grained**.
2. Repository access: solo ese repositorio.
3. Permissions -> **Contents: Read and write** (es lo que usan las Releases).
4. Genera el token y guárdalo en una variable de entorno de tu usuario:

```powershell
setx VELOPACK_GITHUB_TOKEN "github_pat_..."
```

Cierra y vuelve a abrir la terminal para que la variable exista. No la escribas en
ningún archivo del repo.

### 4.3. Instalar la herramienta de empaquetado

```powershell
dotnet tool install -g vpk --version 1.2.161
```

Versión verificada contra NuGet el 2/11/2026. Si ya la tienes, `dotnet tool update -g vpk`.

### 4.4. Cambios en el proyecto (4 archivos)

> **Estado:** ya están aplicados en este repositorio. Se documentan aquí como referencia
> y para poder rehacerlos si se parte de otra copia. Lo único pendiente es poner la URL
> real del repositorio en `Actualizador.cs`.

Sin estos cambios, `vpk` genera el instalador pero el launcher no sabe buscar
actualizaciones (y `vpk pack` falla al no encontrar `VelopackApp.Build().Run()`).

#### a) `src/PokeLobby.LauncherUi/PokeLobby.LauncherUi.csproj`

Añade el paquete y haz que WPF use un `Main` propio (Velopack tiene que ejecutarse antes
de que arranque la interfaz):

```xml
<PropertyGroup>
  ...
  <StartupObject>PokeLobby.LauncherUi.App</StartupObject>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Velopack" Version="1.2.161" />
</ItemGroup>

<ItemGroup>
  <ApplicationDefinition Remove="App.xaml" />
  <Page Include="App.xaml" />
</ItemGroup>
```

#### b) `src/PokeLobby.LauncherUi/App.xaml.cs`

Añade el `using` y el `Main`. El constructor que ya tienes se queda igual:

```csharp
using Velopack;

public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Debe ser lo primero: gestiona instalación, actualización y reinicio.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    public App()
    {
        // ... lo que ya había ...
    }
}
```

#### c) `src/PokeLobby.LauncherUi/Actualizador.cs` (nuevo)

```csharp
using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace PokeLobby.LauncherUi;

/// <summary>Comprueba e instala actualizaciones desde las Releases de GitHub.</summary>
internal static class Actualizador
{
    // Cambia esto por tu repositorio, sin ".git" al final.
    private const string RepositorioUrl = "https://github.com/TU_USUARIO/pokelocke-launcher";

    public static async Task ComprobarAsync(bool avisarSiNoHay = false)
    {
        if (RepositorioUrl.Contains("TU_USUARIO", StringComparison.Ordinal))
            return; // todavía sin configurar

        var mgr = new UpdateManager(new GithubSource(RepositorioUrl, null, prerelease: false));
        if (!mgr.IsInstalled)
            return; // se está ejecutando desde el ZIP: no hay instalación que actualizar

        try
        {
            // Si una actualización se descargó pero no llegó a aplicarse, se aplica ahora.
            if (mgr.UpdatePendingRestart is { } pendiente)
            {
                mgr.ApplyUpdatesAndRestart(pendiente);
                return;
            }

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                if (avisarSiNoHay)
                    MessageBox.Show("Ya tienes la última versión.", "PokeLocke Launcher",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var respuesta = MessageBox.Show(
                $"Hay una versión nueva: {info.TargetFullRelease.Version}\n" +
                $"Tienes instalada: {mgr.CurrentVersion}\n\n" +
                "¿Descargarla e instalarla ahora? El launcher se reiniciará.",
                "PokeLocke Launcher",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (respuesta != MessageBoxResult.Yes)
                return;

            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info.TargetFullRelease);
        }
        catch
        {
            // Sin red, GitHub caído o rate limit: no se molesta al jugador.
            if (avisarSiNoHay)
                MessageBox.Show("No se pudo comprobar si hay actualizaciones.",
                    "PokeLocke Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
```

#### d) Llamarlo al arrancar

En `MainWindow.xaml.cs`, al final de `AlCargarAsync()`:

```csharp
_ = Actualizador.ComprobarAsync();
```

Se lanza sin bloquear la carga. Solo muestra un diálogo si de verdad hay una versión
nueva; si no hay red, no dice nada.

> Alternativa: si prefieres un botón "Buscar actualizaciones" en Ajustes, llama a
> `Actualizador.ComprobarAsync(avisarSiNoHay: true)` desde su `Click`. No es necesario
> para que funcione la actualización automática.

### 4.5. Generar el primer instalador

El script `publicar.ps1` hace todo: publica, copia el `vendor`, empaqueta y (si quieres)
sube a GitHub.

```powershell
cd C:\Users\david\Downloads\Pokelocke\Launcher\LockeLauncher

# Sin subir: genera el Setup para probarlo
pwsh -File publicar.ps1 -Version 0.1.0 -NoUpload

# Con subida a GitHub (crea la Release y publica)
pwsh -File publicar.ps1 -Version 0.1.0 `
  -RepoUrl https://github.com/TU_USUARIO/pokelocke-launcher
```

Salida:

```
..\dist\velopack\releases\PokeLockeLauncher-win-Setup.exe   (~249 MB)
..\dist\velopack\releases\releases.win.json
```

El `vendor` se detecta solo. Si quieres indicarlo a mano:

```powershell
pwsh -File publicar.ps1 -Version 0.1.0 -VendorDir "D:\PokeLobby\vendor"
```

Si el amigo **ya tiene PokeLobby instalado** y quieres un instalador ligero (~70 MB), usa
`-SinVendor`: él tendrá que abrir Ajustes y elegir su carpeta `vendor`.

### 4.6. Probar antes de repartir

1. Ejecuta `dist\velopack\releases\PokeLockeLauncher-win-Setup.exe`.
2. Comprueba que se instala y abre, y que existe `...\current\vendor\mGBA`.
3. Añade una ROM, ábrela y comprueba que arranca el emulador.
4. Cierra el launcher y comprueba que siguen ahí `%APPDATA%\PokeLockeLauncher\ajustes.json`
   y las carátulas en `%APPDATA%\PokeLockeLauncher\datos\covers`.
5. Publica una versión de prueba `0.1.1` (aunque solo cambie un texto) y vuelve a abrir
   el launcher instalado: debe ofrecer la actualización, descargarla y reiniciarse.
6. Verifica que la biblioteca y la sesión siguen intactas.

---

## 5. Publicar una actualización (rutina)

1. Cambia el código.
2. Sube la versión (por ejemplo de `0.1.0` a `0.1.1`).
3. Ejecuta:

```powershell
pwsh -File publicar.ps1 -Version 0.1.1 `
  -RepoUrl https://github.com/TU_USUARIO/pokelocke-launcher
```

4. Listo. El amigo lo recibe la próxima vez que abra el launcher.

### Por qué las actualizaciones son pequeñas

Velopack genera **paquetes delta** contra las releases anteriores que estén en
`dist\velopack\releases`. Por eso el script **no borra** esa carpeta: si la vacías en
cada publicación, la siguiente actualización será completa (~249 MB) en vez de unos MB.

Si publicas desde otro PC o desde GitHub Actions, antes hay que descargar las releases
anteriores a esa carpeta. Para un grupo pequeño, lo normal es publicar siempre desde tu
PC y conservar `dist\velopack\releases`.

---

## 6. Qué ve el colega

1. Le pasas el enlace de la Release (o el `Setup.exe` directamente).
2. Descarga ~249 MB una vez.
3. Ejecuta, SmartScreen, instalar. Sin permisos de administrador.
4. Abre con su usuario y contraseña de PokeLocke.
5. Actualizaciones: al abrir, si hay versión nueva, un diálogo le pregunta; acepta,
   descarga solo lo cambiado y el launcher se reinicia.

No instala .NET, no instala PokeLobby, no toca Firebase, no copia emuladores.

---

## 7. Detalles que conviene saber

### SmartScreen

El `.exe` no está firmado, así que Windows mostrará "Windows protegió tu PC". Es normal:
"Más información" -> "Ejecutar de todas formas". Si el grupo crece, se puede firmar con
Azure Trusted Signing o un certificado de code signing; `vpk pack` acepta
`--signParams` / `--azureTrustedSignFile`.

### El ZIP actual no se actualiza

`PokeLockeLauncher-0.1.0-win-x64.zip` no lleva `vendor` ni instalador. Sirve para que tú
pruebes la interfaz, pero no es el formato de reparto.

### Antivirus y emuladores

El instalador incluye binarios de terceros (mGBA, melonDS, azahar, JRE). Algunos
antivirus marcan emuladores como sospechosos. No es un fallo del launcher; si pasa, se
añade la carpeta a exclusiones.

### Licencias

`vendor` son programas de terceros (GPL y otras licencias). Al redistribuirlos, conserva
sus archivos `LICENSE`/`COPYING` dentro de la carpeta. No publiques el `vendor` como
código propio.

### Volver atrás

Velopack no baja de versión por defecto. Para "deshacer" una release, publica una versión
**mayor** con el código anterior (por ejemplo `0.1.2` con el comportamiento de `0.1.0`).
No borres releases antiguas: son la base de los deltas.

### Límites de GitHub

- Asset máximo: 2 GB (el instalador son ~249 MB).
- Sin token, GitHub limita a 60 consultas por hora y IP. Para un grupo de amigos es
  suficiente; el launcher consulta una vez por arranque.

### Si alguien sigue usando el ZIP

`UpdateManager.IsInstalled` es `false` y no se actualiza. Es intencionado: sin instalador
no hay carpeta `current\` que Velopack pueda reemplazar. Para recibir actualizaciones hay
que instalar con el `Setup.exe`.

---

## 8. Checklist

- [ ] Repo público creado y URL apuntada.
- [ ] Token fine-grained con `Contents: Read and write`, guardado en `VELOPACK_GITHUB_TOKEN`.
- [x] `vpk` instalado (`vpk --version`).
- [x] `Velopack` añadido al `.csproj`, `StartupObject` y `App.xaml` como `Page`.
- [x] `Main` con `VelopackApp.Build().Run()`.
- [ ] `Actualizador.cs` con la URL real del repo (ahora pone `TU_USUARIO`).
- [x] Llamada a `Actualizador.ComprobarAsync()` en `AlCargarAsync`.
- [x] `publicar.ps1 -Version 0.1.0 -NoUpload` genera el Setup con `vendor`.
- [ ] Instalado y probado en tu PC (ROM + emulador + datos).
- [ ] Publicada `0.1.0` y probada la actualización a `0.1.1`.
- [ ] Enlace pasado a los colegas.
