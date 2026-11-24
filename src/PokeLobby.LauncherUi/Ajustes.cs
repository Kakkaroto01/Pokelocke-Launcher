using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Rutas propias del launcher. Los datos viven en <c>%APPDATA%\PokeLockeLauncher</c>:
/// <c>ajustes.json</c> en la raíz y la biblioteca/carátulas en <c>datos\</c>.
/// Se usa Roaming a propósito: Velopack instala en <c>%LOCALAPPDATA%\PokeLockeLauncher</c> y
/// al desinstalar borra esa carpeta ENTERA; lo que está en <c>%APPDATA%</c> sobrevive.
/// Las carpetas antiguas de PokeLobby solo se LEEN una vez para migrar; no se crean ni se usan.
/// </summary>
public static class RutasLauncher
{
    public const string NombreCarpeta = "PokeLockeLauncher";

    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Carpeta raíz de datos del launcher en el perfil del usuario (sobrevive a desinstalar).</summary>
    public static string Base => Path.Combine(Roaming, NombreCarpeta);

    /// <summary>Ajustes y sesión (refresh token).</summary>
    public static string ArchivoAjustes => Path.Combine(Base, "ajustes.json");

    /// <summary>Biblioteca, huellas, vínculos y carátulas.</summary>
    public static string Datos => Path.Combine(Base, "datos");

    // ── Restos de etapas anteriores: solo para migrar, nunca para escribir ──
    /// <summary>Etapa intermedia: datos dentro de la carpeta de instalación (Velopack la borra al desinstalar).</summary>
    public static string BaseAntiguaLocal => Path.Combine(Local, NombreCarpeta);
    public static string AjustesAntiguos => Path.Combine(Local, "PokeLobby.LauncherUi", "ajustes.json");
    public static string DatosAntiguos => Path.Combine(Local, "PokeLobby", "Launcher");
}

/// <summary>
/// Migración de una sola dirección desde las carpetas de PokeLobby a las de PokeLocke.
/// Copia (no mueve) para no romper la instalación original, y nunca sobrescribe lo que ya exista.
/// </summary>
public static class MigracionDatos
{
    private static readonly string[] Archivos = ["library.json", "huellas.json", "covers.json", "vinculos.json"];

    /// <summary>Copia la biblioteca y las carátulas antiguas si aún no hay datos propios.</summary>
    public static void MigrarSiHaceFalta()
    {
        // 1) Etapa intermedia: se guardaban dentro de la carpeta de instalación (LOCALAPPDATA).
        CopiarAjustes(RutasLauncher.BaseAntiguaLocal, RutasLauncher.Base);
        CopiarDatos(Path.Combine(RutasLauncher.BaseAntiguaLocal, "datos"), RutasLauncher.Datos);
        // 2) Launcher original de PokeLobby.
        CopiarDatos(RutasLauncher.DatosAntiguos, RutasLauncher.Datos);
    }

    private static void CopiarAjustes(string origenBase, string destinoBase)
    {
        try
        {
            var desde = Path.Combine(origenBase, "ajustes.json");
            var hasta = Path.Combine(destinoBase, "ajustes.json");
            if (!File.Exists(desde) || File.Exists(hasta)) return;
            Directory.CreateDirectory(destinoBase);
            File.Copy(desde, hasta);
        }
        catch { /* si falla, se arranca con los ajustes por defecto */ }
    }

    private static void CopiarDatos(string origen, string destino)
    {
        try
        {
            if (!Directory.Exists(origen)) return;
            if (File.Exists(Path.Combine(destino, "library.json"))) return;   // ya migrado

            Directory.CreateDirectory(destino);
            foreach (var nombre in Archivos)
            {
                var desde = Path.Combine(origen, nombre);
                var hasta = Path.Combine(destino, nombre);
                if (File.Exists(desde) && !File.Exists(hasta)) File.Copy(desde, hasta);
            }

            var coversDesde = Path.Combine(origen, "covers");
            if (!Directory.Exists(coversDesde)) return;
            var coversHasta = Path.Combine(destino, "covers");
            Directory.CreateDirectory(coversHasta);
            foreach (var archivo in Directory.GetFiles(coversDesde))
            {
                var hasta = Path.Combine(coversHasta, Path.GetFileName(archivo));
                if (!File.Exists(hasta)) File.Copy(archivo, hasta);
            }
        }
        catch { /* si falla, el launcher arranca igual con la carpeta propia vacía */ }
    }

    /// <summary>¿Esta ruta es una carpeta de datos heredada (PokeLobby o la etapa en LOCALAPPDATA)?</summary>
    public static bool EsDatosAntiguos(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return false;
        try
        {
            var comparada = Path.GetFullPath(ruta!).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var heredada in new[] { RutasLauncher.DatosAntiguos, Path.Combine(RutasLauncher.BaseAntiguaLocal, "datos") })
            {
                if (string.Equals(comparada,
                    Path.GetFullPath(heredada).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch { return false; }
    }
}

/// <summary>Ajustes del launcher (guardados en %LOCALAPPDATA%\PokeLockeLauncher\ajustes.json).</summary>
public sealed class Ajustes
{
    /// <summary>
    /// Configuración pública del proyecto Firebase de PokeLocke (la misma que lleva la app Android;
    /// una API key web de Firebase no es un secreto). Se usa como valor inicial para que quien
    /// reciba el launcher no tenga que escribirla a mano. Se puede sobrescribir en ⚙ Ajustes.
    /// </summary>
    public const string FirebaseUrlPorDefecto = "https://pokelocke-orion-default-rtdb.europe-west1.firebasedatabase.app";
    public const string FirebaseApiKeyPorDefecto = "AIzaSyDzELi4d_P3cxC-dd2r8rNyIoWEV-ah_zA";

    public string FirebaseUrl { get; set; } = FirebaseUrlPorDefecto;
    public string FirebaseApiKey { get; set; } = FirebaseApiKeyPorDefecto;
    public string FirebaseAuthToken { get; set; } = "";
    /// <summary>Email de la cuenta PokeLocke (sesión persistente).</summary>
    public string CuentaEmail { get; set; } = "";
    /// <summary>Nombre visible de la cuenta (se conserva aunque el email no llegue).</summary>
    public string CuentaUsuario { get; set; } = "";
    /// <summary>Refresh token de Firebase (permite re-entrar sin la contraseña; se rota en cada renovación).</summary>
    public string CuentaRefreshToken { get; set; } = "";
    /// <summary>Carpeta de datos del launcher (library.json, huellas.json, covers…).</summary>
    public string DatosDir { get; set; } = RutasLauncher.Datos;
    /// <summary>Carpeta vendor con los emuladores (auto-detectada si se deja vacía).</summary>
    public string VendorDir { get; set; } = "";

    // ── Personalización del launcher (evidencia REA: AccentThemeService / BackgroundThemeService) ──
    /// <summary>Índice del tema de acento (16 presets).</summary>
    public int TemaIndice { get; set; }
    /// <summary>Índice del fondo de color (8 presets).</summary>
    public int FondoIndice { get; set; }
    /// <summary>Ruta de imagen (.png/.jpg) o vídeo (.mp4) de fondo; vacío = solo color.</summary>
    public string FondoRuta { get; set; } = "";
    public bool VideoConSonido { get; set; }
    public int VideoVolumen { get; set; } = 50;
    /// <summary>Última carpeta usada en el selector de carátulas (se recuerda entre sesiones).</summary>
    public string UltimaCarpetaCaratulas { get; set; } = "";

    /// <summary>Sonidos de navegación (hover/click) del original.</summary>
    public bool Sonidos { get; set; } = true;

    // ── Emulador externo de Switch (evidencia REA: EmuladorExternoService) ──
    /// <summary>Ruta del ejecutable del emulador de Switch (yuzu/ryujinx/etc.).</summary>
    public string SwitchRuta { get; set; } = "";
    /// <summary>Plantilla de argumentos; <c>{rom}</c> se sustituye por la ROM entrecomillada.</summary>
    public string SwitchArgumentos { get; set; } = "\"{rom}\"";

    /// <summary>
    /// Rutas candidatas de <c>ajustes.json</c>, por orden: la propia del launcher y, si no se
    /// puede escribir (permisos, CFA, OneDrive…), junto al ejecutable y en Documentos. La ruta
    /// antigua de PokeLobby se lee al final solo para migrar y así no perder la sesión. Así la
    /// carpeta de datos elegida SE PERSISTE y el aviso de reparación no vuelve a salir cada arranque.
    /// </summary>
    private static string[] RutasCandidatas()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return
        [
            RutasLauncher.ArchivoAjustes,
            Path.Combine(AppContext.BaseDirectory, "ajustes.json"),
            Path.Combine(docs, "PokeLockeLauncher", "ajustes.json"),
            Path.Combine(RutasLauncher.BaseAntiguaLocal, "ajustes.json"),
            RutasLauncher.AjustesAntiguos,
        ];
    }

    private static string? _rutaActiva;

    /// <summary>¿Es una ruta heredada (de solo lectura) y no la propia del launcher?</summary>
    private static bool EsRutaHeredada(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return false;
        return string.Equals(ruta, RutasLauncher.AjustesAntiguos, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ruta, Path.Combine(RutasLauncher.BaseAntiguaLocal, "ajustes.json"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ruta desde la que se cargaron (o donde se guardaron) los ajustes.</summary>
    public static string? RutaActiva => _rutaActiva;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static Ajustes Cargar()
    {
        // Primero se traen biblioteca y carátulas de la etapa PokeLobby (copiando, sin borrar nada).
        MigracionDatos.MigrarSiHaceFalta();

        // Se prefiere la copia modificada más recientemente entre las que existan.
        Ajustes? mejor = null;
        string? mejorRuta = null;
        var mejorFecha = DateTime.MinValue;
        foreach (var ruta in RutasCandidatas())
        {
            try
            {
                if (!File.Exists(ruta)) continue;
                var leido = JsonSerializer.Deserialize<Ajustes>(File.ReadAllText(ruta), Json);
                if (leido is null) continue;
                var fecha = File.GetLastWriteTimeUtc(ruta);
                if (fecha > mejorFecha) { mejor = leido; mejorRuta = ruta; mejorFecha = fecha; }
            }
            catch { /* copia corrupta: se prueba la siguiente */ }
        }
        if (mejor is not null)
        {
            // Nunca se guarda en las rutas heredadas: si lo leído viene de una de ellas, la
            // próxima escritura va a la carpeta propia (así no se crea nada con el nombre viejo).
            _rutaActiva = EsRutaHeredada(mejorRuta) ? null : mejorRuta;
            // Un ajustes.json antiguo trae la carpeta heredada escrita: se remapea a la propia.
            if (MigracionDatos.EsDatosAntiguos(mejor.DatosDir)) mejor.DatosDir = RutasLauncher.Datos;
            return mejor;
        }

        var porDefecto = new Ajustes();
        // Variables de entorno como valores iniciales (misma convención que los demos).
        // Si no hay variable, se deja la configuración pública del proyecto: así el launcher
        // repartido funciona sin pedir Firebase a mano.
        porDefecto.FirebaseUrl =
            Environment.GetEnvironmentVariable("POKELOCKE_FIREBASE_URL") ?? FirebaseUrlPorDefecto;
        porDefecto.FirebaseApiKey =
            Environment.GetEnvironmentVariable("POKELOCKE_FIREBASE_API_KEY") ?? FirebaseApiKeyPorDefecto;
        return porDefecto;
    }

    /// <summary>Guarda en la primera ruta escribible (y devuelve cuál fue). No lanza excepciones.</summary>
    public bool Guardar()
    {
        var texto = JsonSerializer.Serialize(this, Json);
        // Las rutas heredadas solo sirven para LEER: aquí nunca se escribe.
        var candidatas = RutasCandidatas()
            .Where(c => !EsRutaHeredada(c))
            .ToArray();
        var preferida = _rutaActiva;
        if (preferida is not null)
            candidatas = [preferida, .. candidatas.Where(c => !string.Equals(c, preferida, StringComparison.OrdinalIgnoreCase))];

        foreach (var ruta in candidatas)
        {
            try
            {
                var dir = Path.GetDirectoryName(ruta)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(ruta, texto);
                _rutaActiva = ruta;
                return true;
            }
            catch { /* se prueba la siguiente ubicación */ }
        }
        return false;
    }

    /// <summary>
    /// Busca la carpeta <c>vendor\</c> (emuladores, JRE, randomizer). Orden:
    /// 1) variable de entorno, 2) la propia instalación del launcher, 3) la instalación oficial
    /// de PokeLobby/LockeLauncher en las rutas habituales, 4) subiendo por el árbol.
    /// Así quien reciba el instalador completo usa el vendor incluido y, si no lo lleva,
    /// puede reutilizar el de una instalación anterior sin copiar 380 MB.
    /// </summary>
    public static string BuscarVendorDir()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidatos = new List<string?>
        {
            Environment.GetEnvironmentVariable("POKELOCKE_VENDOR_DIR"),
            Path.Combine(AppContext.BaseDirectory, "vendor"),
        };
        foreach (var raiz in new[] { local, pf, pf86 })
        {
            if (string.IsNullOrWhiteSpace(raiz)) continue;
            candidatos.Add(Path.Combine(raiz, RutasLauncher.NombreCarpeta, "current", "vendor"));
            candidatos.Add(Path.Combine(raiz, "PokeLobby.Launcher", "current", "vendor"));
            candidatos.Add(Path.Combine(raiz, "PokeLobbyLauncher", "current", "vendor"));
        }
        foreach (var candidato in candidatos)
        {
            if (!string.IsNullOrWhiteSpace(candidato) && Directory.Exists(candidato)) return candidato;
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidato = Path.Combine(dir, RutasLauncher.NombreCarpeta, "current", "vendor");
            if (Directory.Exists(candidato)) return candidato;
            var padre = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            if (padre == dir) break;
            dir = padre;
        }
        return "";
    }
}
