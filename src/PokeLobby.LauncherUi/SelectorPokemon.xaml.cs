using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeLobby.PokeLocke;
using PKHeX.Core;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Selector de Pokémon estilo PKHeX: muestra el EQUIPO (6 huecos) y las CAJAS (30 huecos por
/// caja, con navegador), cada hueco como una tarjeta con sprite, nombre, nivel, shiny y estado.
/// Solo los huecos presentes en <see cref="ObjetivoPkm"/> son seleccionables.
/// </summary>
public partial class SelectorPokemon : Window
{
    private readonly List<ObjetivoPkm> _objetivos;
    private readonly Dictionary<string, Border> _tarjetas = new(StringComparer.Ordinal);
    private UbicacionPkm? _elegido;
    private Border? _tarjetaElegida;
    private readonly List<int> _cajas = [];

    public UbicacionPkm? Elegido => _elegido;

    public SelectorPokemon(string titulo, IEnumerable<ObjetivoPkm> objetivos)
    {
        InitializeComponent();
        _objetivos = objetivos.ToList();
        TxtTitulo.Text = titulo;
        TxtSubtitulo.Text = "Elige un hueco del equipo o de una caja (doble clic para aceptar). " +
                            _objetivos.Count + " Pokémon disponibles.";
        ConstruirEquipo();
        ConstruirCajas();
    }

    // ─────────────────────────── equipo ───────────────────────────

    private void ConstruirEquipo()
    {
        GridEquipo.Children.Clear();
        for (var i = 0; i < 6; i++)
        {
            var ubi = UbicacionPkm.EnEquipo(i);
            var objetivo = _objetivos.FirstOrDefault(o => o.Ubicacion.Equals(ubi));
            GridEquipo.Children.Add(CrearHueco(objetivo, ubi));
        }
    }

    // ─────────────────────────── cajas ───────────────────────────

    private void ConstruirCajas()
    {
        _cajas.Clear();
        _cajas.AddRange(_objetivos
            .Where(o => !o.Ubicacion.EsEquipo)
            .Select(o => o.Ubicacion.Box)
            .Distinct()
            .OrderBy(b => b));
        CmbCaja.Items.Clear();
        foreach (var caja in _cajas)
            CmbCaja.Items.Add("Caja " + (caja + 1));
        if (_cajas.Count == 0)
        {
            GridCaja.Children.Clear();
            GridCaja.Children.Add(new TextBlock
            {
                Text = "Esta partida no tiene Pokémon en cajas para esta acción.",
                Foreground = (Brush)Application.Current.Resources["ColorTextoSuave"],
                Margin = new Thickness(4, 8, 0, 0),
            });
            TxtInfoCaja.Text = "";
            return;
        }
        CmbCaja.SelectedIndex = 0;
    }

    private void CmbCaja_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbCaja.SelectedIndex < 0 || CmbCaja.SelectedIndex >= _cajas.Count) return;
        var caja = _cajas[CmbCaja.SelectedIndex];
        GridCaja.Children.Clear();
        var enCaja = _objetivos.Count(o => !o.Ubicacion.EsEquipo && o.Ubicacion.Box == caja);
        TxtInfoCaja.Text = enCaja + " Pokémon en esta caja";
        for (var slot = 0; slot < 30; slot++)
        {
            var ubi = UbicacionPkm.EnCaja(caja, slot);
            var objetivo = _objetivos.FirstOrDefault(o => o.Ubicacion.Equals(ubi));
            GridCaja.Children.Add(CrearHueco(objetivo, ubi));
        }
    }

    // ─────────────────────────── tarjeta de hueco ───────────────────────────

    private Border CrearHueco(ObjetivoPkm? objetivo, UbicacionPkm ubi)
    {
        var borde = new Border
        {
            Width = 148,
            Height = 128,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(18, 23, 35)),
            BorderBrush = (Brush)Application.Current.Resources["ColorBorde"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
        };

        if (objetivo is null)
        {
            borde.Opacity = 0.45;
            borde.Child = new TextBlock
            {
                Text = ubi.EsEquipo ? "—" : "",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["ColorTextoSuave"],
            };
            return borde;
        }

        var pkm = objetivo.Pkm;
        var panel = new StackPanel();

        var imagen = new Image
        {
            Width = 72,
            Height = 72,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = objetivo.Etiqueta(),
        };
        _ = CargarSpriteAsync(pkm.Species, imagen);
        panel.Children.Add(imagen);

        var nombre = string.IsNullOrWhiteSpace(pkm.Nickname)
            ? NombreEspecie(pkm.Species)
            : pkm.Nickname;
        panel.Children.Add(new TextBlock
        {
            Text = nombre,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["ColorTexto"],
        });

        var detalles = "Nv" + pkm.CurrentLevel;
        if (pkm.IsShiny) detalles += " ✨";
        if (ubi.EsEquipo && pkm.Stat_HPCurrent <= 0) detalles += " ✖";
        panel.Children.Add(new TextBlock
        {
            Text = detalles,
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = pkm.IsShiny
                ? new SolidColorBrush(Color.FromRgb(255, 203, 5))
                : (Brush)Application.Current.Resources["ColorTextoSuave"],
        });

        borde.Child = panel;
        borde.Cursor = Cursors.Hand;
        borde.MouseLeftButtonDown += (_, _) => Seleccionar(borde, ubi, objetivo);
        borde.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount >= 2) Aceptar();
        };
        borde.ToolTip = new ToolTip         {             Content = new TextBlock { Text = InfoPkm(pkm), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)) },             Background = new SolidColorBrush(Color.FromRgb(22, 27, 41)),             BorderBrush = new SolidColorBrush(Color.FromRgb(58, 66, 88)),             BorderThickness = new Thickness(1),             Padding = new Thickness(10, 8, 10, 8),             HasDropShadow = true,             Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,         };
        _tarjetas[Clave(ubi)] = borde;
        return borde;
    }

    private void Seleccionar(Border borde, UbicacionPkm ubi, ObjetivoPkm objetivo)
    {
        if (_tarjetaElegida is not null)
        {
            _tarjetaElegida.BorderBrush = (Brush)Application.Current.Resources["ColorBorde"];
            _tarjetaElegida.BorderThickness = new Thickness(1);
        }
        _tarjetaElegida = borde;
        borde.BorderBrush = (Brush)Application.Current.Resources["AcentoBrush"];
        borde.BorderThickness = new Thickness(2);
        _elegido = ubi;
        BtnAceptar.IsEnabled = true;
        TxtSeleccion.Text = "Seleccionado: " + objetivo.Etiqueta();
    }

    /// <summary>Información completa del Pokémon al pasar el ratón.</summary>
    private static string InfoPkm(PKM p)
    {
        var s = GameInfo.GetStrings("es");
        var hab = p.Format == 4 ? p.Data[0x15] : p.Ability;
        var nombreHab = hab >= 0 && hab < s.Ability.Count ? s.Ability[hab] : "?";
        var ataques = string.Join(", ", p.Moves.Where(m => m > 0)
            .Select(m => m < s.Move.Count ? s.Move[m] : "#" + m));
        var ivs = $"HP {p.IV_HP} · ATK {p.IV_ATK} · DEF {p.IV_DEF} · SPA {p.IV_SPA} · SPD {p.IV_SPD} · SPE {p.IV_SPE}";
        var evs = $"HP {p.EV_HP} · ATK {p.EV_ATK} · DEF {p.EV_DEF} · SPA {p.EV_SPA} · SPD {p.EV_SPD} · SPE {p.EV_SPE}";
        return $"{NombreEspecie(p.Species)}  Nv{p.CurrentLevel}  ({p.IV_HP + p.IV_ATK + p.IV_DEF + p.IV_SPA + p.IV_SPD + p.IV_SPE} IVs)\n" +
               $"IVs:  {ivs}\nEVs:  {evs}\nHabilidad: {nombreHab}\nAtaques: {(ataques.Length == 0 ? "—" : ataques)}";
    }

    private static string Clave(UbicacionPkm ubi)
        => ubi.EsEquipo ? "E" + ubi.PartyIndex : "C" + ubi.Box + "." + ubi.Slot;

    private static string NombreEspecie(int species)
    {
        try
        {
            var strings = GameInfo.GetStrings("es");
            return species > 0 && species < strings.Species.Count ? strings.Species[species] : "#" + species;
        }
        catch
        {
            return "#" + species;
        }
    }

    /// <summary>Carga el sprite desde PokeAPI en segundo plano; si falla, deja el hueco sin imagen.</summary>
    private static async System.Threading.Tasks.Task CargarSpriteAsync(int species, Image destino)
    {
        if (species <= 0) return;
        var bmp = await SpritesPokemon.ObtenerAsync(species);
        if (bmp is not null) destino.Source = bmp;
    }

    private void Aceptar_Click(object sender, RoutedEventArgs e) => Aceptar();

    private void Aceptar()
    {
        if (_elegido is null) return;
        DialogResult = true;
        Close();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}