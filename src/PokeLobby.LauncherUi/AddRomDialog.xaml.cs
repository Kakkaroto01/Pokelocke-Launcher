using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PokeLobby.Watcher;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Diálogo «Añadir ROM», equivalente al AddRomDialog del original (evidencia REA):
/// ruta de la ROM + carpeta de partidas + consola detectada por extensión (GBA/DS/3DS/Switch).
/// Añade: detección del juego por el nombre, carátula automática desde la carpeta de carátulas
/// y selección manual con carpeta recordada.
/// </summary>
public partial class AddRomDialog : Window
{
    private readonly Ajustes _ajustes;

    public RomEntry? Resultado { get; private set; }

    public AddRomDialog(Ajustes ajustes, string? romPrellenada = null)
    {
        InitializeComponent();
        _ajustes = ajustes;
        CmbConsola.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(romPrellenada))
        {
            TxtRom.Text = romPrellenada;
            Autorrellenar(romPrellenada);
        }
    }

    /// <summary>Rellena nombre, consola, carpeta de partidas, juego detectado y carátula sugerida.</summary>
    private void Autorrellenar(string ruta)
    {
        TxtNombre.Text = Path.GetFileNameWithoutExtension(ruta);
        var consola = ConsolaDetector.Detectar(ruta);
        foreach (ComboBoxItem item in CmbConsola.Items)
            if (string.Equals(item.Content?.ToString(), consola, StringComparison.OrdinalIgnoreCase))
                CmbConsola.SelectedItem = item;
        if (string.IsNullOrEmpty(TxtSave.Text))
            TxtSave.Text = Path.GetDirectoryName(ruta) ?? "";

        var juego = PokeLobby.PokeLocke.DetectorJuegoRom.Detectar(ruta);
        TxtJuego.Text = string.IsNullOrEmpty(juego) ? "no detectado (puedes añadir la carátula a mano)" : juego;

        var caratula = PokeLobby.PokeLocke.Caratulas.Buscar(_ajustes.DatosDir,
            [juego, Path.GetFileNameWithoutExtension(ruta)]);
        if (caratula is not null)
        {
            TxtCaratula.Text = caratula;
            TxtCaratulaEstado.Text = "Carátula encontrada automáticamente en la carpeta de carátulas.";
        }
        else
        {
            TxtCaratulaEstado.Text = "Sin carátula automática: puedes elegir una imagen o continuar sin carátula.";
        }
    }

    private void ExaminarRom_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecciona la ROM",
            Filter = "ROMs|*.gba;*.gb;*.gbc;*.nds;*.3ds;*.cci;*.cia;*.cxi;*.xci;*.xcz;*.nsp;*.nsz;*.exe|Todos los archivos|*.*",
        };
        if (dlg.ShowDialog() == true)
        {
            TxtRom.Text = dlg.FileName;
            Autorrellenar(dlg.FileName);
        }
    }

    private void ElegirSaveArchivo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Elige el archivo de partida (o el «main» de 3DS)",
            Filter = "Partidas|*.sav;*.dsv;*.srm;main;*.bin|Todos los archivos|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        var archivo = dlg.FileName;
        var rom = TxtRom.Text.Trim();
        var es3ds = rom.Length > 0 &&
            Path.GetExtension(rom).ToLowerInvariant() is ".3ds" or ".cci" or ".cxi" or ".cia";

        if (es3ds)
        {
            var importado = PokeLobby.PokeLocke.Guardado3ds.ImportarPartida(rom, archivo);
            if (importado.Ok)
            {
                TxtSave.Text = Path.GetDirectoryName(importado.Destino!) ?? archivo;
                TxtCaratulaEstado.Text = "Partida importada a la carpeta de Azahar.";
            }
            else
            {
                Avisar("No se pudo importar la partida: " + importado.Mensaje);
            }
            return;
        }

        TxtSave.Text = archivo;   // 2D/DS: se acepta el archivo de partida tal cual
    }
    private void ElegirSave_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta donde el emulador guarda las partidas de esta ROM" };
        if (dlg.ShowDialog() == true) TxtSave.Text = dlg.FolderName;
    }

    private void ExaminarCaratula_Click(object sender, RoutedEventArgs e)
    {
        string? elegida = CaratulaDialogos.Elegir(this, _ajustes, TxtJuego.Text, TxtNombre.Text);
        if (elegida is null) return;
        TxtCaratula.Text = elegida;
        TxtCaratulaEstado.Text = elegida.Length == 0
            ? "Sin carátula."
            : "Carátula seleccionada manualmente.";
    }

    private void QuitarCaratula_Click(object sender, RoutedEventArgs e)
    {
        TxtCaratula.Text = "";
        TxtCaratulaEstado.Text = "Sin carátula.";
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Anadir_Click(object sender, RoutedEventArgs e)
    {
        var rom = TxtRom.Text.Trim();
        if (rom.Length == 0 || !File.Exists(rom))
        {
            Avisar("Elige un archivo de ROM válido.");
            return;
        }

        var consola = (CmbConsola.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ConsolaDetector.Detectar(rom);
        if (consola == "Switch" && !EmuladorSwitch.Configurado)
            Avisar("Esta ROM es de Switch y aún no has elegido el emulador en Ajustes: podrás añadirla, " +
                   "pero no se podrá jugar hasta que configures el ejecutable del emulador.");

        // Carpeta de partidas: si el jugador eligió una, debe existir y ser escribible.
        var carpetaSave = TxtSave.Text.Trim();
        if (carpetaSave.Length > 0)
        {
            if (!Directory.Exists(carpetaSave))
            {
                Avisar("La carpeta de partidas no existe:\n" + carpetaSave);
                return;
            }
            if (!PokeLobby.PokeLocke.PermisosCarpeta.PuedeEscribir(carpetaSave))
            {
                Avisar("No tengo permiso de escritura en la carpeta de partidas:\n" + carpetaSave +
                       "\n\nEl emulador no podría guardar la partida ahí.");
                return;
            }
        }

        var juego = PokeLobby.PokeLocke.DetectorJuegoRom.Detectar(rom);
        Resultado = new RomEntry
        {
            Name = string.IsNullOrWhiteSpace(TxtNombre.Text) ? Path.GetFileNameWithoutExtension(rom) : TxtNombre.Text.Trim(),
            ConsoleName = consola,
            RomPath = rom,
            SavePath = carpetaSave,
            CoverPath = TxtCaratula.Text.Trim(),
            DetectedGame = string.IsNullOrEmpty(juego) ? "" : juego,
            LastPlayed = "Never",
        };
        DialogResult = true;
        Close();
    }

    private void Avisar(string texto)
    {
        TxtAviso.Text = texto;
        TxtAviso.Visibility = Visibility.Visible;
    }
}
