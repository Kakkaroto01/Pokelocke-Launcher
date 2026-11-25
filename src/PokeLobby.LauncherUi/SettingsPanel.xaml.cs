using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Panel de ajustes y personalización embebido en la ventana principal.
/// Equivalente a la antigua SettingsWindow (tema de acento, fondo, sonidos,
/// emulador de Switch, carpetas y Firebase).
/// </summary>
public partial class SettingsPanel : UserControl
{
    private readonly Ajustes _ajustes;
    private bool _cargando = true;

    public event Action? CambiosAplicados;

    public SettingsPanel(Ajustes ajustes)
    {
        InitializeComponent();
        _ajustes = ajustes;
        CargarValores();
        _cargando = false;
    }

    private void CargarValores()
    {
        TxtSwitchExe.Text = _ajustes.SwitchRuta;
        TxtSwitchArgs.Text = _ajustes.SwitchArgumentos;
        TxtVendor.Text = _ajustes.VendorDir;
        TxtDatos.Text = _ajustes.DatosDir;
        TxtFirebaseUrl.Text = _ajustes.FirebaseUrl;
        TxtFirebaseKey.Text = _ajustes.FirebaseApiKey;
        ChkSonidos.IsChecked = _ajustes.Sonidos;
        ChkSonidoVideo.IsChecked = _ajustes.VideoConSonido;
        SldVolumen.Value = _ajustes.VideoVolumen;
        TxtVolumen.Text = $"{_ajustes.VideoVolumen}%";
        // PanelAudioVideo eliminado en el rediseño
        // PanelAudioVideo.Visibility = TemaFondo.EsVideo ? Visibility.Visible : Visibility.Collapsed;
        ConstruirAcentos();
        ConstruirFondos();
        ActualizarEstadoFondo();
        ActualizarAvisoSwitch();
    }

    // ─────────────────────────── tema de acento ───────────────────────────

    private void ConstruirAcentos()
    {
        PanelAcentos.Children.Clear();
        for (var i = 0; i < TemaAcento.Presets.Length; i++)
        {
            var indice = i;
            var c = TemaAcento.ColorDe(i);
            var borde = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(c),
                ToolTip = TemaAcento.Presets[i],
            };
            if (i == _ajustes.TemaIndice)
            {
                borde.BorderBrush = (Brush)FindResource("ColorTexto");
                borde.BorderThickness = new Thickness(3);
            }
            borde.MouseLeftButtonUp += (_, _) =>
            {
                _ajustes.TemaIndice = indice;
                TemaAcento.Apply(indice);
                ConstruirAcentos();
            };
            PanelAcentos.Children.Add(borde);
        }
    }

    // ─────────────────────────── fondo ───────────────────────────

    private void ConstruirFondos()
    {
        PanelFondosGrid.Children.Clear();
        for (var i = 0; i < TemaFondo.Presets.Length; i++)
        {
            var indice = i;
            var c = TemaFondo.ColorDe(i);
            var borde = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(c),
                ToolTip = TemaFondo.Presets[i],
            };
            if (i == _ajustes.FondoIndice)
            {
                borde.BorderBrush = (Brush)FindResource("ColorTexto");
                borde.BorderThickness = new Thickness(3);
            }
            borde.MouseLeftButtonUp += (_, _) =>
            {
                _ajustes.FondoIndice = indice;
                TemaFondo.Apply(indice);
                ConstruirFondos();
                CambiosAplicados?.Invoke();
            };
            PanelFondosGrid.Children.Add(borde);
        }
    }

    private void ActualizarEstadoFondo()
    {
        FondoLabel.Text = TemaFondo.ImagenActual is null
            ? "Sin imagen/vídeo (solo color)"
            : (TemaFondo.EsVideo ? "Vídeo activo" : "Imagen activa") + ": " + Path.GetFileName(TemaFondo.ImagenActual);
        // PanelAudioVideo eliminado en el rediseño
        // PanelAudioVideo.Visibility = TemaFondo.EsVideo ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ElegirFondo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Fondo del launcher",
            Filter = "Imagen o vídeo|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.mp4|Todos los archivos|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        if (!TemaFondo.AplicarImagen(dlg.FileName))
        {
            MessageBox.Show("No se pudo usar ese archivo como fondo (¿es una imagen/vídeo válido?).",
                "Fondo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _ajustes.FondoRuta = dlg.FileName;
        ActualizarEstadoFondo();
        CambiosAplicados?.Invoke();
    }

    private void QuitarFondo_Click(object sender, RoutedEventArgs e)
    {
        TemaFondo.AplicarImagen(null);
        _ajustes.FondoRuta = "";
        ActualizarEstadoFondo();
        CambiosAplicados?.Invoke();
    }

    private void AudioVideo_Changed(object sender, RoutedEventArgs e)
    {
        if (_cargando) return;
        _ajustes.VideoConSonido = ChkSonidoVideo.IsChecked == true;
        TemaFondo.GuardarAudioVideo(_ajustes.VideoConSonido, (int)SldVolumen.Value);
    }

    private void Volumen_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtVolumen is not null) TxtVolumen.Text = $"{(int)e.NewValue}%";
        if (_cargando) return;
        _ajustes.VideoVolumen = (int)e.NewValue;
        TemaFondo.GuardarAudioVideo(_ajustes.VideoConSonido, _ajustes.VideoVolumen);
    }

    private void Sonidos_Changed(object sender, RoutedEventArgs e)
    {
        if (_cargando) return;
        _ajustes.Sonidos = ChkSonidos.IsChecked == true;
    }

    // ─────────────────────────── emulador de Switch ───────────────────────────

    private void ElegirSwitch_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Ejecutable del emulador de Switch",
            Filter = "Ejecutables|*.exe|Todos los archivos|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        if (EmuladorSwitch.EsElLauncher(dlg.FileName))
        {
            MessageBox.Show("Ese ejecutable es el propio launcher: elige el del emulador de Switch.",
                "Emulador de Switch", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            return;
        }
        _ajustes.SwitchRuta = dlg.FileName;
        EmuladorSwitch.GuardarRuta(dlg.FileName);
        TxtSwitchExe.Text = dlg.FileName;
        ActualizarAvisoSwitch();
    }

    private void QuitarSwitch_Click(object sender, RoutedEventArgs e)
    {
        _ajustes.SwitchRuta = "";
        EmuladorSwitch.GuardarRuta("");
        TxtSwitchExe.Text = "";
        ActualizarAvisoSwitch();
    }

    private void SwitchArgs_LostFocus(object sender, RoutedEventArgs e)
    {
        _ajustes.SwitchArgumentos = string.IsNullOrWhiteSpace(TxtSwitchArgs.Text)
            ? EmuladorSwitch.ArgumentosPorDefecto
            : TxtSwitchArgs.Text.Trim();
        EmuladorSwitch.GuardarArgumentos(_ajustes.SwitchArgumentos);
        TxtSwitchArgs.Text = _ajustes.SwitchArgumentos;
        ActualizarAvisoSwitch();
    }

    private void ActualizarAvisoSwitch()
    {
        if (string.IsNullOrWhiteSpace(_ajustes.SwitchRuta))
            TxtSwitchAviso.Text = "Sin emulador de Switch: las ROMs de Switch no se podrán lanzar.";
        else if (!File.Exists(_ajustes.SwitchRuta))
            TxtSwitchAviso.Text = "La ruta del emulador ya no existe: vuelve a elegirlo.";
        else if (!_ajustes.SwitchArgumentos.Contains("{rom}", StringComparison.Ordinal))
            TxtSwitchAviso.Text = "Los argumentos no contienen {rom}: el emulador no recibirá la ROM.";
        else
            TxtSwitchAviso.Text = "";
    }

    // ─────────────────────────── carpetas / firebase ───────────────────────────

    private void ElegirVendor_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta vendor de Pokelobby" };
        if (dlg.ShowDialog() == true) TxtVendor.Text = dlg.FolderName;
    }

    private void ElegirDatos_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta de datos del launcher" };
        if (dlg.ShowDialog() == true) TxtDatos.Text = dlg.FolderName;
    }

    /// <summary>Persiste todos los valores editados y notifica cambios.</summary>
    public void AplicarCambios()
    {
        _ajustes.VendorDir = TxtVendor.Text.Trim();
        _ajustes.DatosDir = TxtDatos.Text.Trim();
        _ajustes.FirebaseUrl = TxtFirebaseUrl.Text.Trim();
        _ajustes.FirebaseApiKey = TxtFirebaseKey.Text.Trim();
        _ajustes.Sonidos = ChkSonidos.IsChecked == true;
        _ajustes.VideoConSonido = ChkSonidoVideo.IsChecked == true;
        _ajustes.VideoVolumen = (int)SldVolumen.Value;
        _ajustes.SwitchRuta = TxtSwitchExe.Text.Trim();
        _ajustes.SwitchArgumentos = TxtSwitchArgs.Text.Trim();
        _ajustes.Guardar();
        CambiosAplicados?.Invoke();
    }

    private void BtnAplicar_Click(object sender, RoutedEventArgs e)
    {
        AplicarCambios();
    }

    private void BtnRestablecer_Click(object sender, RoutedEventArgs e)
    {
        var resultado = MessageBox.Show(
            "¿Restablecer todos los ajustes a valores predeterminados?\n\nSe perderán los temas, fondos, rutas y credenciales configuradas.",
            "Restablecer ajustes de fábrica", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (resultado != MessageBoxResult.Yes) return;

        _ajustes.TemaIndice = 0;
        _ajustes.FondoIndice = 0;
        _ajustes.FondoRuta = "";
        _ajustes.VideoConSonido = false;
        _ajustes.VideoVolumen = 35;
        _ajustes.Sonidos = true;
        _ajustes.SwitchRuta = "";
        _ajustes.SwitchArgumentos = EmuladorSwitch.ArgumentosPorDefecto;
        _ajustes.VendorDir = "";
        _ajustes.DatosDir = "";
        _ajustes.FirebaseUrl = "";
        _ajustes.FirebaseApiKey = "";
        _ajustes.Guardar();

        CargarValores();
        CambiosAplicados?.Invoke();
        MessageBox.Show("Ajustes restablecidos. Reinicia el launcher para aplicar todos los cambios.",
            "Restablecer ajustes", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ToggleKey_Click(object sender, RoutedEventArgs e)
    {
        // TextBox simple (no PasswordBox): el botón de ojo es decorativo
    }
}
