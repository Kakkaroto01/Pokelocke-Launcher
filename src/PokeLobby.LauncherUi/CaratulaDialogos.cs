using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PokeLobby.PokeLocke;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Diálogo de carátula compartido: abre el selector en la carpeta de carátulas (o en la última
/// usada), sugiere la carátula detectada y recuerda la carpeta. La imagen elegida se COPIA a
/// <c>covers</c> con un nombre estable, así no se pierde si luego se mueve el archivo original.
/// Devuelve: null = cancelado, "" = quitar carátula, ruta = nueva carátula.
/// </summary>
public static class CaratulaDialogos
{
    public static string? Elegir(Window dueno, Ajustes ajustes, string? juego, string? nombreRom)
    {
        var carpetaCovers = Caratulas.CarpetaDe(ajustes.DatosDir);
        var sugerida = Caratulas.Buscar(ajustes.DatosDir, [juego, nombreRom]);

        var mensaje = "Elige una imagen de carátula." + Environment.NewLine +
                      (!string.IsNullOrWhiteSpace(juego) ? "Juego detectado: " + juego + Environment.NewLine : "") +
                      (sugerida is not null
                          ? "Encontrada automáticamente: " + Path.GetFileName(sugerida)
                          : "No hay coincidencia automática en " + carpetaCovers);
        var eleccion = MessageBox.Show(dueno, mensaje + Environment.NewLine + Environment.NewLine +
                                         "Sí = elegir imagen · No = quitar carátula · Cancelar = dejarlo como está",
            "Carátula", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (eleccion == MessageBoxResult.Cancel) return null;
        if (eleccion == MessageBoxResult.No) return "";

        var dlg = new OpenFileDialog
        {
            Title = "Selecciona la carátula",
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Todos los archivos|*.*",
            InitialDirectory = Caratulas.CarpetaInicial(ajustes.DatosDir, ajustes.UltimaCarpetaCaratulas,
                Path.GetDirectoryName(nombreRom ?? "")),
            FileName = sugerida is not null ? Path.GetFileName(sugerida) : "",
        };
        if (dlg.ShowDialog(dueno) != true) return null;
        if (!Caratulas.EsImagen(dlg.FileName))
        {
            MessageBox.Show(dueno, "El archivo elegido no es una imagen válida.", "Carátula",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        ajustes.UltimaCarpetaCaratulas = Path.GetDirectoryName(dlg.FileName) ?? "";
        try { ajustes.Guardar(); } catch { }

        // Se copia a covers con el nombre del juego detectado (o el del archivo si no se detectó).
        var destino = Caratulas.Guardar(ajustes.DatosDir, dlg.FileName,
            string.IsNullOrWhiteSpace(juego) ? Path.GetFileNameWithoutExtension(nombreRom ?? "") : juego);
        return destino;
    }
}
