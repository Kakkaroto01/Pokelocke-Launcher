using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Carátulas de la biblioteca: se guardan/referencian desde la carpeta <c>covers</c> dentro de la
/// carpeta de datos del launcher (<c>%LOCALAPPDATA%\PokeLockeLauncher\datos\covers</c>).
/// La instalación trae un juego de carátulas en <c>covers\</c> junto al ejecutable: en el primer
/// arranque se copian las que falten, sin pisar nunca una carátula puesta a mano.
/// </summary>
public static class Caratulas
{
    public static readonly string[] ExtensionesImagen = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>Carpeta de carátulas dentro de la carpeta de datos (se crea si no existe).</summary>
    public static string CarpetaDe(string datosDir)
        => Path.Combine(datosDir, "covers");

    /// <summary>
    /// Carpeta de carátulas incluidas con la instalación: <c>covers\</c> junto al ejecutable
    /// (dentro del paquete Velopack acaba en <c>current\covers</c>).
    /// </summary>
    public static string CarpetaIncluidas()
        => Path.Combine(AppContext.BaseDirectory, "covers");

    /// <summary>
    /// Copia a la carpeta de datos las carátulas incluidas que aún no estén. No sobrescribe nada:
    /// una carátula propia (por ejemplo la del Añil) siempre gana. Devuelve cuántas copió.
    /// </summary>
    public static int Sembrar(string datosDir) => SembrarDesde(CarpetaIncluidas(), datosDir);

    /// <summary>
    /// Igual que <see cref="Sembrar(string)"/> pero con una carpeta de origen explícita.
    /// Devuelve cuántas carátulas copió.
    /// </summary>
    public static int SembrarDesde(string origen, string datosDir)
    {
        if (!Directory.Exists(origen)) return 0;
        var destino = CarpetaDe(datosDir);
        try { Directory.CreateDirectory(destino); } catch { return 0; }

        var copiadas = 0;
        foreach (var archivo in Directory.GetFiles(origen))
        {
            if (!ExtensionesImagen.Contains(Path.GetExtension(archivo), StringComparer.OrdinalIgnoreCase)) continue;
            var hasta = Path.Combine(destino, Path.GetFileName(archivo));
            if (File.Exists(hasta)) continue;
            try { File.Copy(archivo, hasta); copiadas++; } catch { /* una carátula ilegible no rompe el arranque */ }
        }
        return copiadas;
    }

    /// <summary>
    /// Copia una imagen elegida a mano dentro de <c>covers</c> con un nombre estable (el del juego
    /// si se conoce) y devuelve la ruta definitiva. Si no se puede copiar, devuelve el original
    /// para no perder la carátula que el usuario ya había elegido.
    /// </summary>
    public static string Guardar(string datosDir, string archivoOrigen, string? nombre = null)
    {
        try
        {
            if (!EsImagen(archivoOrigen)) return archivoOrigen;
            var destino = CarpetaDe(datosDir);
            Directory.CreateDirectory(destino);

            var baseNombre = LimpiarNombre(string.IsNullOrWhiteSpace(nombre)
                ? Path.GetFileNameWithoutExtension(archivoOrigen)
                : nombre!);
            if (baseNombre.Length == 0) baseNombre = "caratula";
            var extension = Path.GetExtension(archivoOrigen).ToLowerInvariant();

            var ruta = Path.Combine(destino, baseNombre + extension);
            if (string.Equals(Path.GetFullPath(ruta), Path.GetFullPath(archivoOrigen), StringComparison.OrdinalIgnoreCase))
                return ruta;   // ya está en covers con ese nombre: nada que copiar

            for (var i = 2; File.Exists(ruta) && i < 1000; i++)
                ruta = Path.Combine(destino, baseNombre + " (" + i + ")" + extension);

            File.Copy(archivoOrigen, ruta, overwrite: false);
            return ruta;
        }
        catch
        {
            return archivoOrigen;
        }
    }

    /// <summary>Quita caracteres no válidos en un nombre de archivo y espacios sobrantes.</summary>
    private static string LimpiarNombre(string texto)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
            sb.Append(invalidos.Contains(c) ? '_' : c);
        return sb.ToString().Trim().TrimEnd('.');
    }

    /// <summary>Normaliza un nombre para comparar: minúsculas, sin acentos, sin «pokemon», sin signos.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var t = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(t.Length);
        foreach (var c in t)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        // tokens completos: se descartan ruidos (pokemon, versión, idiomas, regiones) y números
        var ruido = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pokemon", "version", "edition", "usa", "eur", "esp", "jpn", "jp", "en", "es", "de", "fr", "it",
            "the", "jeu", "game", "nds", "gba", "3ds", "xci", "nsp", "rom", "randomized",
        };
        var tokens = new List<string>();
        foreach (var bruto in sb.ToString().Split([' ', '-', '_', '.', '(', ')', '[', ']', ',', '+', '/', '&'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (ruido.Contains(bruto)) continue;
            if (bruto.Length >= 3 && bruto.All(char.IsDigit)) continue;   // nº de release, no versión
            tokens.Add(bruto);
        }
        return string.Join(" ", tokens);
    }
    /// <summary>
    /// Equivalencias español/inglés de los títulos: las carátulas incluidas llevan el nombre
    /// español (Rojo Fuego, Blanco 2, Ultrasol…) y las ROMs pueden llamarse en inglés.
    /// </summary>
    private static readonly (string A, string B)[] AliasJuego =
    [
        ("red", "rojo"), ("blue", "azul"), ("yellow", "amarillo"), ("gold", "oro"),
        ("silver", "plata"), ("crystal", "cristal"), ("ruby", "rubi"), ("sapphire", "zafiro"),
        ("emerald", "esmeralda"), ("firered", "rojo fuego"), ("leafgreen", "verde hoja"),
        ("diamond", "diamante"), ("pearl", "perla"), ("platinum", "platino"),
        ("black", "negro"), ("white", "blanco"), ("black 2", "negro 2"), ("white 2", "blanco 2"),
        ("sun", "sol"), ("moon", "luna"), ("ultra sun", "ultrasol"), ("ultra moon", "ultraluna"),
        ("omega ruby", "rubi omega"), ("alpha sapphire", "zafiro alfa"),
        ("sword", "espada"), ("shield", "escudo"),
    ];

    /// <summary>
    /// Busca una carátula existente para los nombres dados (juego detectado, nombre de la ROM…).
    /// Devuelve null si no hay ninguna coincidencia fiable.
    /// </summary>
    public static string? Buscar(string datosDir, IEnumerable<string?> nombres)
    {
        var carpeta = CarpetaDe(datosDir);
        if (!Directory.Exists(carpeta)) return null;
        var indices = new List<string>();
        foreach (var nombre in nombres.Select(Normalizar))
        {
            if (nombre.Length == 0) continue;
            indices.Add(nombre);
            foreach (var (a, b) in AliasJuego)
            {
                if (nombre == a) indices.Add(b);
                else if (nombre == b) indices.Add(a);
            }
        }
        indices = indices.Distinct().ToList();
        if (indices.Count == 0) return null;

        var archivos = Directory.GetFiles(carpeta)
            .Where(f => ExtensionesImagen.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        // 1) coincidencia exacta normalizada  2) el archivo empieza por el nombre buscado
        //    (el prefijo solo con nombres de 3+ letras: «x»/«y» exigen coincidencia exacta)
        foreach (var indice in indices)
        {
            var exacto = archivos.FirstOrDefault(f => Normalizar(Path.GetFileNameWithoutExtension(f)) == indice);
            if (exacto is not null) return exacto;
        }
        foreach (var indice in indices.Where(n => n.Length >= 3))
        {
            var parcial = archivos.FirstOrDefault(f =>
                Normalizar(Path.GetFileNameWithoutExtension(f)).StartsWith(indice, StringComparison.Ordinal));
            if (parcial is not null) return parcial;
        }
        return null;
    }

    /// <summary>¿Es una imagen admitida?</summary>
    public static bool EsImagen(string? ruta)
        => !string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta)
           && ExtensionesImagen.Contains(Path.GetExtension(ruta), StringComparer.OrdinalIgnoreCase);

    /// <summary>Carpeta inicial del selector: la última usada, o «covers», o la de la ROM.</summary>
    public static string CarpetaInicial(string datosDir, string? ultimaCarpeta, string? carpetaRom = null)
    {
        if (!string.IsNullOrWhiteSpace(ultimaCarpeta) && Directory.Exists(ultimaCarpeta)) return ultimaCarpeta!;
        var covers = CarpetaDe(datosDir);
        if (Directory.Exists(covers)) return covers;
        if (!string.IsNullOrWhiteSpace(carpetaRom) && Directory.Exists(carpetaRom)) return carpetaRom!;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }
}

/// <summary>
/// Detección del juego a partir del nombre del archivo de ROM (sin leer la ROM). Es una heurística
/// conservadora sobre los títulos oficiales: si no hay coincidencia clara devuelve "".
/// </summary>
public static class DetectorJuegoRom
{
    private static readonly (string Clave, string Juego)[] Titulos =
    [
        ("heartgold", "HeartGold"), ("heart gold", "HeartGold"),
        ("soulsilver", "SoulSilver"), ("soul silver", "SoulSilver"),
        ("platinum", "Platinum"), ("platine", "Platinum"),
        ("diamond", "Diamond"), ("diamante", "Diamond"),
        ("pearl", "Pearl"), ("perla", "Pearl"),
        ("black 2", "Black 2"), ("negro 2", "Black 2"), ("white 2", "White 2"), ("blanco 2", "White 2"),
        ("black", "Black"), ("negro", "Black"), ("white", "White"), ("blanco", "White"),
        ("emerald", "Emerald"), ("esmeralda", "Emerald"),
        ("ruby", "Ruby"), ("rubi", "Ruby"), ("sapphire", "Sapphire"), ("zafiro", "Sapphire"),
        ("firered", "FireRed"), ("rojo fuego", "FireRed"),
        ("leafgreen", "LeafGreen"), ("verde hoja", "LeafGreen"),
        ("yellow", "Yellow"), ("amarillo", "Yellow"),
        ("red", "Red"), ("rojo", "Red"), ("blue", "Blue"), ("azul", "Blue"),
        ("añil", "Añil"), ("anil", "Añil"),
        ("crystal", "Crystal"), ("cristal", "Crystal"),
        ("gold", "Gold"), ("oro", "Gold"), ("silver", "Silver"), ("plata", "Silver"),
        ("x", "X"), ("y", "Y"), ("omega ruby", "Omega Ruby"), ("rubi omega", "Omega Ruby"),
        ("alpha sapphire", "Alpha Sapphire"), ("zafiro alfa", "Alpha Sapphire"),
        ("sun", "Sun"), ("sol", "Sun"), ("moon", "Moon"), ("luna", "Moon"),
        ("ultra sun", "Ultra Sun"), ("ultra sol", "Ultra Sun"), ("ultrasun", "Ultra Sun"), ("ultrasol", "Ultra Sun"),
        ("ultra moon", "Ultra Moon"), ("ultra luna", "Ultra Moon"), ("ultramoon", "Ultra Moon"), ("ultraluna", "Ultra Moon"),
        ("sword", "Sword"), ("espada", "Sword"), ("shield", "Shield"), ("escudo", "Shield"),
        ("brilliant diamond", "Brilliant Diamond"), ("shining pearl", "Shining Pearl"),
        ("scarlet", "Scarlet"), ("violet", "Violet"), ("escarlata", "Scarlet"), ("purpura", "Violet"),
    ];

    /// <summary>Devuelve el título detectado ("" si no hay coincidencia fiable).</summary>
    public static string Detectar(string? nombreRom)
    {
        var normalizado = Caratulas.Normalizar(Path.GetFileNameWithoutExtension(nombreRom ?? ""));
        if (normalizado.Length == 0) return "";
        // las claves más largas primero (evita que «black» gane a «black 2»)
        foreach (var (clave, juego) in Titulos.OrderByDescending(t => t.Clave.Length))
        {
            var patron = " " + clave + " ";
            if ((" " + normalizado + " ").Contains(patron, StringComparison.Ordinal))
                return juego;
        }
        // casos de una sola letra (X/Y) exigen coincidencia exacta de palabra al final
        var tokens = normalizado.Split(' ');
        if (tokens.Length <= 2)
        {
            if (tokens.Contains("x")) return "X";
            if (tokens.Contains("y")) return "Y";
        }
        return "";
    }
}
