using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Temas de ACENTO del launcher, replicados del original (AccentThemeService): 16 presets
/// que recolorean la interfaz mediante recursos de WPF. Evidencia REA:
/// PokeLobbyLauncher.Services.AccentThemeService.Presets (16 colores).
/// </summary>
public static class TemaAcento
{
    /// <summary>16 acentos del original.</summary>
    public static readonly string[] Presets =
    [
        "#E3350D", "#F57D31", "#FFCB05", "#7AC74C",
        "#4CAF50", "#00C2A8", "#3B82F6", "#6366F1",
        "#8B5CF6", "#D946EF", "#EC4899", "#F43F5E",
        "#FF9F1C", "#14B8A6", "#A3E635", "#94A3B8",
    ];

    public static Color ColorDe(int indice)
        => (Color)ColorConverter.ConvertFromString(Presets[Math.Clamp(indice, 0, Presets.Length - 1)]);

    /// <summary>Aplica el acento a los recursos de la aplicación (AcentoBrush y derivados).</summary>
    public static void Apply(int indice)
    {
        var c = ColorDe(indice);
        Set("ColorAcento", c);
        Set("AcentoBrush", new SolidColorBrush(c));
        Set("AcentoSuaveBrush", new SolidColorBrush(Color.FromArgb(48, c.R, c.G, c.B)));
        Set("AcentoBordeBrush", new SolidColorBrush(Color.FromArgb(160, c.R, c.G, c.B)));
        // degradado del título/hero
        var grad = new LinearGradientBrush(
            Color.FromArgb(255, (byte)Math.Min(255, c.R + 40), (byte)Math.Min(255, c.G + 40), (byte)Math.Min(255, c.B + 40)),
            Color.FromArgb(255, c.R, c.G, c.B), 0);
        Set("AcentoGradienteBrush", grad);
    }

    private static void Set(string clave, object valor)
    {
        if (Application.Current is not null)
            Application.Current.Resources[clave] = valor;
    }
}

/// <summary>
/// Fondo del launcher, replicado del original (BackgroundThemeService): 8 presets de color,
/// imagen o vídeo (mp4) con audio y volumen, y un «velo» para mantener la legibilidad.
/// Evidencia REA: PokeLobbyLauncher.Services.BackgroundThemeService (Presets de 8 colores,
/// bgtheme.txt / bgimagen.txt / bgvideo_audio.txt, AplicarImagen + MediaElement).
/// </summary>
public static class TemaFondo
{
    /// <summary>8 fondos del original.</summary>
    public static readonly string[] Presets =
    [
        "#181D2A", "#16161A", "#16281F", "#231630",
        "#2E161F", "#2C2116", "#162C2C", "#1D232A",
    ];

    public static Color ColorDe(int indice)
        => (Color)ColorConverter.ConvertFromString(Presets[Math.Clamp(indice, 0, Presets.Length - 1)]);

    /// <summary>Ruta actual de imagen o vídeo de fondo (null = solo color).</summary>
    public static string? ImagenActual { get; private set; }

    public static bool EsVideo => ImagenActual is not null &&
        string.Equals(Path.GetExtension(ImagenActual), ".mp4", StringComparison.OrdinalIgnoreCase);

    public static string? VideoActual => EsVideo ? ImagenActual : null;

    public static bool VideoConSonido { get; private set; }
    public static int VideoVolumen { get; private set; } = 50;

    /// <summary>Se dispara cuando cambia el fondo (color o imagen).</summary>
    public static event Action? FondoCambiado;

    /// <summary>Aplica el preset de color + el velo sobre la imagen/vídeo si lo hay.</summary>
    public static void Apply(int indice)
    {
        var c = ColorDe(indice);
        var oscuro = Oscurecer(c, 0.55);
        // Recursos de PINCEL (las vistas usan DynamicResource sobre brushes)
        Set("ColorFondo", new SolidColorBrush(oscuro));
        Set("ColorPanel", new SolidColorBrush(c));
        Set("ColorPanelVelo", new SolidColorBrush(Color.FromArgb(235, c.R, c.G, c.B)));
        Set("ColorPanelSolido", new SolidColorBrush(c));
        Set("FondoVeloBrush", ImagenActual is null
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(Color.FromArgb(196, oscuro.R, oscuro.G, oscuro.B)));
    }

    /// <summary>Aplica una imagen o vídeo de fondo (null = quitar). Devuelve false si no es válido.</summary>
    public static bool AplicarImagen(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            ImagenActual = null;
            Set("FondoAppImagen", null);
            AplicarVelo(null);
            FondoCambiado?.Invoke();
            return true;
        }
        if (!File.Exists(ruta)) return false;
        if (string.Equals(Path.GetExtension(ruta), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            ImagenActual = ruta;
            Set("FondoAppImagen", null);
            AplicarVelo(null);
            FondoCambiado?.Invoke();
            return true;
        }
        var bmp = CargarBitmap(ruta);
        if (bmp is null) return false;
        ImagenActual = ruta;
        Set("FondoAppImagen", bmp);
        AplicarVelo(bmp);
        FondoCambiado?.Invoke();
        return true;
    }

    public static void GuardarAudioVideo(bool conSonido, int volumen)
    {
        VideoConSonido = conSonido;
        VideoVolumen = Math.Clamp(volumen, 0, 100);
    }

    private static void AplicarVelo(BitmapImage? _)
        => Set("FondoVeloBrush", ImagenActual is null
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(Color.FromArgb(196, Oscurecer(ColorDe(0), 0.55).R,
                Oscurecer(ColorDe(0), 0.55).G, Oscurecer(ColorDe(0), 0.55).B)));

    private static BitmapImage? CargarBitmap(string ruta)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(ruta, UriKind.Absolute);
            bmp.DecodePixelWidth = 2560;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static Color Oscurecer(Color c, double factor)
        => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private static void Set(string clave, object? valor)
    {
        if (Application.Current is not null)
            Application.Current.Resources[clave] = valor;
    }
}

/// <summary>Sonidos de navegación del original (tonos sintetizados al hover/click) — versión simple.</summary>
public static class SonidosUi
{
    public static bool Activados { get; set; } = true;

    public static void Hover() => Tono(1360, 90, 0.05);
    public static void Click() => Tono(1720, 140, 0.07);

    private static void Tono(double frecuencia, int milisegundos, double volumen)
    {
        if (!Activados) return;
        try
        {
            var sr = 44100;
            var n = sr * milisegundos / 1000;
            var datos = new byte[n * 2];
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / sr;
                var env = Math.Pow(1.0 - (double)i / n, 2.0);
                var muestra = (short)(Math.Sin(2 * Math.PI * frecuencia * t) * env * volumen * 32767);
                datos[i * 2] = (byte)(muestra & 0xFF);
                datos[i * 2 + 1] = (byte)((muestra >> 8) & 0xFF);
            }
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write("RIFF"u8); bw.Write(36 + datos.Length); bw.Write("WAVE"u8);
            bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(sr); bw.Write(sr * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write("data"u8); bw.Write(datos.Length); bw.Write(datos);
            ms.Position = 0;
            var player = new System.Media.SoundPlayer(new MemoryStream(ms.ToArray()));
            player.Play();
        }
        catch { /* sin audio disponible */ }
    }
}
/// <summary>
/// Configuración del emulador EXTERNO de Switch: delega en <see cref="PokeLobby.PokeLocke.EmuladorSwitchConfig"/>
/// (lógica pura testeable); aquí solo se guarda el estado de la sesión de UI.
/// </summary>
public static class EmuladorSwitch
{
    public const string ArgumentosPorDefecto = PokeLobby.PokeLocke.EmuladorSwitchConfig.ArgumentosPorDefecto;

    public static string Ruta { get; private set; } = "";
    public static string Argumentos { get; private set; } = ArgumentosPorDefecto;

    public static bool EsElLauncher(string? ruta) => PokeLobby.PokeLocke.EmuladorSwitchConfig.EsElLauncher(ruta);

    public static void Cargar(string ruta, string argumentos)
    {
        Ruta = PokeLobby.PokeLocke.EmuladorSwitchConfig.NormalizarRuta(ruta);
        Argumentos = PokeLobby.PokeLocke.EmuladorSwitchConfig.NormalizarArgumentos(argumentos);
    }

    public static void GuardarRuta(string ruta) => Ruta = PokeLobby.PokeLocke.EmuladorSwitchConfig.NormalizarRuta(ruta);
    public static void GuardarArgumentos(string argumentos)
        => Argumentos = PokeLobby.PokeLocke.EmuladorSwitchConfig.NormalizarArgumentos(argumentos);

    public static string ConstruirArgumentos(string rom, string? plantilla = null)
        => PokeLobby.PokeLocke.EmuladorSwitchConfig.ConstruirArgumentos(rom, plantilla ?? Argumentos);

    public static bool Configurado => PokeLobby.PokeLocke.EmuladorSwitchConfig.Configurado(Ruta);
}

/// <summary>Detección de consola por extensión (delega en la lógica pura testeable).</summary>
public static class ConsolaDetector
{
    public static string Detectar(string ruta) => PokeLobby.PokeLocke.ConsolaDetector.Detectar(ruta);

    public static readonly string[] Extensiones = PokeLobby.PokeLocke.ConsolaDetector.Extensiones;
}