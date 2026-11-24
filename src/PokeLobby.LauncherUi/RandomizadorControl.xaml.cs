using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PokeLobby.Randomizer;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Randomizador integrado: navegación por CATEGORÍAS (como Universal Pokémon Randomizer ZX),
/// un único contenedor de desplazamiento (rueda del ratón correcta) y un control adecuado por
/// tipo de parámetro. Todos los cambios van al mismo <see cref="RandomizerSettings"/> que se
/// envía al motor real (RandoBridge → PokeRandoZX.jar).
/// </summary>
public partial class RandomizadorControl : UserControl
{
    private Ajustes _ajustes = new();
    private MainWindow? _dueno;
    private RandomizerSettings _settings = new();
    private readonly Dictionary<string, Control> _controles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _valoresObservados = new(StringComparer.OrdinalIgnoreCase);
    private bool _cargado;
    private string _prefijoActual = "";

    /// <summary>Rangos conocidos para claves numéricas (mínimo, máximo, sufijo).</summary>
    private static readonly Dictionary<string, (double Min, double Max, string Sufijo)> Rangos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["movesets.forceGoodDamagingPercent"] = (0, 100, "%"),
        ["trainers.movesetForceGoodDamagingPercent"] = (0, 100, "%"),
        ["tmmoves.forceGoodDamagingPercent"] = (0, 100, "%"),
        ["tutormoves.forceGoodDamagingPercent"] = (0, 100, "%"),
        ["wild.shinyChanceOneIn"] = (1, 8192, " (1 entre N)"),
        ["trainers.shinyChanceOneIn"] = (1, 8192, " (1 entre N)"),
        ["statics.levelModifierValue"] = (0, 100, " niveles"),
        ["wild.levelModifierValue"] = (0, 100, " niveles"),
        ["totems.levelModifierValue"] = (0, 100, " niveles"),
        ["statics.bstMargin"] = (0, 100, " pts"),
        ["evolutions.tradeLevel"] = (1, 100, ""),
        ["evolutions.maxLevel"] = (1, 100, ""),
        ["movesets.guaranteedLevel1Min"] = (1, 10, ""),
        ["trainers.additionalBossCount"] = (0, 20, ""),
        ["trainers.additionalImportantCount"] = (0, 20, ""),
        ["trainers.additionalRegularCount"] = (0, 20, ""),
        ["trainers.legendPerTeam"] = (0, 6, ""),
        ["trainers.legendMinPerTeam"] = (0, 6, ""),
        ["trainers.leagueUniqueCount"] = (0, 20, ""),
        ["basestats.updateGenerationTo"] = (1, 9, ""),
        ["movedata.updateGenerationTo"] = (1, 9, ""),
    };

    /// <summary>Valores enumerados conocidos (del propio motor y de sus presets).</summary>
    private static readonly Dictionary<string, string[]> Enumerados = new(StringComparer.OrdinalIgnoreCase)
    {
        ["basestats.expCurveSpeed"] = ["ERRATIC", "FAST", "MEDIUM_FAST", "MEDIUM_SLOW", "SLOW", "FLUCTUATING"],
        ["basestats.expCurveTarget"] = ["LEGENDARIES", "STRONG_LEGENDARIES", "ALL"],
        ["wild.additionalRule"] = ["NONE", "SIMILAR_STRENGTH", "CATCH_EM_ALL", "TYPE_THEMED_AREAS"],
        ["trainers.mod"] = ["UNCHANGED", "RANDOM", "DISTRIBUTED", "MAINPLAYTHROUGH", "TYPE_THEMED", "TYPE_THEMED_ELITE4_GYMS"],
        ["starters.mod"] = ["UNCHANGED", "CUSTOM", "RANDOM_WITH_TWO_EVOLUTIONS", "COMPLETELY_RANDOM"],
        ["trades.mod"] = ["UNCHANGED", "RANDOMIZE_GIVEN", "RANDOMIZE_BOTH"],
        ["totems.allyMod"] = ["UNCHANGED", "COMPLETELY_RANDOM", "RANDOM", "SAME_STRENGTH"],
        ["totems.auraMod"] = ["UNCHANGED", "COMPLETELY_RANDOM", "RANDOM", "SAME_STRENGTH"],
    };

    public RandomizadorControl()
    {
        InitializeComponent();
    }

    /// <summary>Inyecta ajustes y dueño (los llama MainWindow al construir la ventana).</summary>
    public void Inicializar(Ajustes ajustes, MainWindow? dueno)
    {
        _ajustes = ajustes;
        _dueno = dueno;
        if (_cargado) return;
        _cargado = true;
        CargarPresetsVendor();
        RecolectarValoresObservados();

        // La plantilla de defectos tiene TODO en UNCHANGED: si se empieza así, «Randomizar» apenas
        // cambia nada y parece que no funciona. Se arranca con el preset full_random del vendor.
        try
        {
            var servicio = new PokeLobby.Randomizer.RandomizerService(_ajustes.VendorDir);
            foreach (var nombrePreset in new[] { "full_random", "full_random_equilibrado" })
            {
                if (servicio.Presets().Contains(nombrePreset, StringComparer.OrdinalIgnoreCase))
                {
                    _settings = PokeLobby.Randomizer.RandomizerSettings.DesdePreset(servicio.DirRandomizer, nombrePreset);
                    Log("Preset inicial: «" + nombrePreset + "» (" + _settings.Valores.Count + " claves).");
                    break;
                }
            }
        }
        catch { /* sin vendor: se usan los defectos */ }

        LlenarCategorias();
    }

    /// <summary>Pre-fija la ROM a randomizar.</summary>
    public void PonerRom(string? rutaRom)
    {
        if (string.IsNullOrEmpty(rutaRom)) return;
        TxtRom.Text = rutaRom;
        Log("ROM: " + Path.GetFileName(rutaRom));
    }

    // ─────────────────────────── categorías (agrupadas estilo Pokelobby) ───────────────────────────

    private sealed record CategoriaGrupo(string Titulo, string[] Prefijos)
    {
        public override string ToString() => Titulo;
    }

    /// <summary>Agrupación de categorías del motor en pestañas principales al estilo Pokelobby.</summary>
    private static readonly CategoriaGrupo[] CategoriasAgrupadas =
    [
        new("Características",        ["types", "abilities", "basestats", "evolutions"]),
        new("Iniciales y Estáticos",  ["starters", "statics", "trades"]),
        new("Movimientos",            ["movedata", "movesets", "tmmoves", "tmcompat", "tutorcompat"]),
        new("Pokémon Salvajes",       ["wild", "totems"]),
        new("Entrenadores",           ["trainers"]),
        new("Objetos",                ["fielditems", "shops", "pickup"]),
        new("Miscelánea",             ["misc", "liga"]),
    ];

    private void LlenarCategorias()
    {
        var gruposPresentes = _settings.Valores.Keys
            .Select(k => k.Split('.')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        TabsCategorias.Items.Clear();
        foreach (var grupo in CategoriasAgrupadas)
        {
            // Solo mostrar la pestaña si tiene al menos un prefijo presente en los ajustes
            if (!grupo.Prefijos.Any(p => gruposPresentes.Contains(p))) continue;

            var tab = new TabItem
            {
                Header = grupo.Titulo,
                Tag = grupo,
            };
            TabsCategorias.Items.Add(tab);
        }

        if (TabsCategorias.Items.Count > 0)
            TabsCategorias.SelectedIndex = 0;
    }

    private void TabsCategorias_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabsCategorias.SelectedItem is not TabItem tab) return;
        if (tab.Tag is not CategoriaGrupo grupo) return;
        VolcarControles();
        _prefijoActual = grupo.Prefijos.FirstOrDefault() ?? "";
        MostrarCategoria(grupo);
    }

    private void MostrarCategoria(CategoriaGrupo grupo)
    {
        var opciones = _settings.Valores
            .Where(kv => grupo.Prefijos.Any(p => kv.Key.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PanelOpciones.Children.Clear();
        _controles.Clear();

        if (opciones.Count == 0)
        {
            PanelOpciones.Children.Add(new TextBlock
            {
                Text = "No hay opciones que mostrar con el filtro actual.",
                Foreground = Recurso("ColorTextoSuave", Brushes.Gray),
                Margin = new Thickness(4, 8, 0, 0),
            });
            return;
        }

        try
        {
            // Agrupar opciones en UNA tarjeta por categoría base (Modo + Opciones juntos,
            // al estilo Pokelobby: no se separan en tarjetas distintas).
            var subgrupos = opciones
                .Select(kv => new
                {
                    Clave = kv.Key,
                    Valor = kv.Value,
                    PrefijoReal = ObtenerPrefijoReal(kv.Key, grupo.Prefijos),
                    Subseccion = ObtenerSubseccion(kv.Key, grupo.Prefijos)
                })
                .GroupBy(x => x.Subseccion)
                .OrderBy(g => OrdenSubseccion(g.Key, g.First().PrefijoReal))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var subgrupo in subgrupos)
            {
                var prefijoReal = subgrupo.First().PrefijoReal;
                var tarjeta = CrearTarjeta(subgrupo.Key, subgrupo.Select(x => (x.Clave, x.Valor)).ToList(), prefijoReal);
                PanelOpciones.Children.Add(tarjeta);
                PanelOpciones.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });
            }

            // Quitar el último Separator sobrante
            if (PanelOpciones.Children.Count > 0 && PanelOpciones.Children[^1] is Separator)
                PanelOpciones.Children.RemoveAt(PanelOpciones.Children.Count - 1);
        }
        catch (Exception ex)
        {
            PanelOpciones.Children.Clear();
            PanelOpciones.Children.Add(new TextBlock
            {
                Text = $"Error al mostrar las opciones: {ex.Message}",
                Foreground = new SolidColorBrush(Colors.OrangeRed),
                FontSize = 12,
                Margin = new Thickness(4, 8, 0, 0),
            });
        }
    }

    /// <summary>Devuelve el nombre de la tarjeta (categoría base) para una clave dada.
    /// Agrupa Modo + Opciones bajo un mismo nombre, como Pokelobby (una sola tarjeta
    /// por categoría: «Estáticos», «Evoluciones», etc.).</summary>
    private static string ObtenerSubseccion(string clave, string[] prefijosCategoria)
    {
        var prefijo = prefijosCategoria.FirstOrDefault(p => clave.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase))
            ?? prefijosCategoria[0];
        var resto = clave[(prefijo.Length + 1)..]; // quitar "prefijo."
        var primerSegmento = resto.Split('.')[0];

        return prefijo.ToLowerInvariant() switch
        {
            "types" => "Tipos",
            "abilities" => "Habilidades",
            "basestats" => "Estadísticas Base",
            "statics" => "Estáticos",
            "evolutions" => "Evoluciones",
            "starters" => "Iniciales",
            "trades" => "Intercambios",
            "movedata" => "Datos de Movimiento",
            "movesets" => "Movesets",
            "tmmoves" => "MT/MO",
            "tmcompat" => "MT/MO",
            "tutormoves" => "Tutores",
            "tutorcompat" => "Tutores",
            "wild" => "Salvajes",
            "totems" => "Tótems",
            "trainers" => resto switch
            {
                "mod" => "Modo de Entrenadores",
                "levelModified" or "levelModifier" or "levelModifierValue" => "Nivel de Entrenadores",
                "legendChampion" or "legendEliteFour" or "legendLeadersFrom" or "legendLimited" or
                    "legendPerTeam" or "legendMinPerTeam" or "legendAllowStrong" or "legendStrongWhereFits" =>
                    "Legendarios",
                "forceMegaChampion" or "forceMegaEliteFour" or "forceMegaGymLeaders" or "forceMegaRivals" or
                    "megasALaAltura" => "Mega Evolución",
                "readjustToBstBand" or "gymLeaderBstLowerPercent" or "gymLeaderBstUpperPercent" or
                    "importantBstLowerPercent" or "importantBstUpperPercent" => "Ajuste de Fuerza",
                _ => "Opciones Generales",
            },
            "fielditems" => "Objetos de Campo",
            "shops" => "Tiendas",
            "pickup" => "Recogida",
            "misc" => "Varios",
            "liga" => "Liga",
            _ => primerSegmento switch
            {
                "mod" => "Modo",
                _ => primerSegmento,
            },
        };
    }

    /// <summary>Devuelve el prefijo real de la clave (ej. "statics", "starters", "trainers") usado para agrupar.</summary>
    private static string ObtenerPrefijoReal(string clave, string[] prefijosCategoria)
    {
        return prefijosCategoria.FirstOrDefault(p => clave.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase))
            ?? prefijosCategoria[0];
    }

    /// <summary>Orden de aparición de las tarjetas dentro de cada categoría, siguiendo Pokelobby.
    /// Menor valor = aparece primero. <paramref name="prefijoReal"/> es el prefijo de motor de la clave
    /// (ej. "starters", "statics", "trainers"), no el array de la categoría.</summary>
    private static int OrdenSubseccion(string subseccion, string prefijoReal)
    {
        var categoria = prefijoReal.ToLowerInvariant();

        return categoria switch
        {
            // ── Características ──
            "types" => subseccion == "Tipos" ? 1 : 99,
            "abilities" => subseccion == "Habilidades" ? 1 : 99,
            "basestats" => subseccion == "Estadísticas Base" ? 1 : 99,
            "evolutions" => subseccion == "Evoluciones" ? 1 : 99,

            // ── Iniciales y Estáticos ──
            "starters" => subseccion switch
            {
                "Iniciales" => 1,
                "Opciones de Iniciales" => 2,
                _ => 99,
            },
            "statics" => subseccion switch
            {
                "Estáticos" => 1,
                "Opciones de Estáticos" => 2,
                _ => 99,
            },
            "trades" => subseccion switch
            {
                "Intercambios" => 3,
                _ => 99,
            },

            // ── Movimientos ──
            "movedata" => subseccion == "Datos de Movimiento" ? 2 : 99,
            "movesets" => subseccion == "Movesets" ? 1 : 99,
            "tmmoves" => subseccion == "MT/MO" ? 1 : 99,
            "tmcompat" => subseccion == "MT/MO" ? 1 : 99,
            "tutormoves" => subseccion == "Tutores" ? 1 : 99,
            "tutorcompat" => subseccion == "Tutores" ? 1 : 99,

            // ── Pokémon Salvajes ──
            "wild" => subseccion == "Salvajes" ? 1 : 99,
            "totems" => subseccion == "Tótems" ? 2 : 99,

            // ── Entrenadores ──
            "trainers" => subseccion switch
            {
                "Modo de Entrenadores" => 1,
                "Opciones Generales" => 2,
                "Nivel de Entrenadores" => 3,
                "Legendarios" => 4,
                "Mega Evolución" => 5,
                "Ajuste de Fuerza" => 6,
                _ => 99,
            },

            // ── Objetos ──
            "fielditems" => subseccion == "Objetos de Campo" ? 1 : 99,
            "shops" => subseccion == "Tiendas" ? 2 : 99,
            "pickup" => subseccion == "Recogida" ? 3 : 99,

            // ── Miscelánea ──
            "misc" => subseccion switch
            {
                "Varios" => 1,
                _ => 99,
            },
            "liga" => subseccion == "Liga" ? 1 : 99,

            _ => 99,
        };
    }

    /// <summary>Crea una tarjeta (Border) con título y las opciones dadas, al estilo Pokelobby.</summary>
    private Border CrearTarjeta(string titulo, List<(string clave, string valor)> opciones, string prefijoReal)
    {
        var tarjeta = new Border
        {
            Style = (Style)Application.Current.Resources["Tarjeta"],
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var stack = new StackPanel();

        // Título de la tarjeta
        var tituloTarjeta = new TextBlock
        {
            Text = titulo,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Recurso("ColorTexto", Brushes.White),
            Margin = new Thickness(0, 0, 0, 14),
        };
        stack.Children.Add(tituloTarjeta);

        // Grid uniforme con el número de columnas apropiado para esta categoría.
        var columnas = ColumnasParaCategoria(prefijoReal);
        var grid = new Grid();
        for (var c = 0; c < columnas; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fila = 0;
        var columna = 0;
        foreach (var (clave, valor) in opciones)
        {
            var celda = CrearFilaOpcion(clave, valor);
            Grid.SetRow(celda, fila);
            Grid.SetColumn(celda, columna);
            grid.Children.Add(celda);

            columna++;
            if (columna >= columnas)
            {
                columna = 0;
                fila++;
            }
        }

        for (var i = 0; i <= fila; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        stack.Children.Add(grid);
        tarjeta.Child = stack;
        return tarjeta;
    }

    /// <summary>Número de columnas dentro de una tarjeta, según la categoría (estilo Pokelobby).</summary>
    private static int ColumnasParaCategoria(string prefijoReal)
    {
        return prefijoReal.ToLowerInvariant() switch
        {
            "trainers" => 4,   // Entrenadores: muchas opciones, 4 columnas
            "misc" => 3,       // Miscelánea: varias columnas
            "liga" => 3,       // Liga: varias columnas
            _ => 2,            // Resto: 2 columnas
        };
    }

    /// <summary>Crea una fila de opción individual (etiqueta + control), al estilo Pokelobby.</summary>
    private FrameworkElement CrearFilaOpcion(string clave, string valor)
    {
        // trainers.percentageLevelModifier no existe en el motor (la casilla real es trainers.levelModified).
        if (clave.Equals("trainers.percentageLevelModifier", StringComparison.OrdinalIgnoreCase))
            return new Grid { Height = 0 };

        var fila = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 8, 0, 8) };

        var etiqueta = new TextBlock
        {
            Text = TituloClave(clave),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = DescripcionesRandomizer.Para(clave, valor),
            Foreground = Recurso("ColorTextoSuave", Brushes.Gray),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
        };
        ToolTipService.SetShowDuration(etiqueta, 30000);
        ToolTipService.SetInitialShowDelay(etiqueta, 350);
        fila.Children.Add(etiqueta);

        // ── Casilla de verificación: checkbox + texto al lado, como Pokelobby ──
        if (valor.Equals("true", StringComparison.OrdinalIgnoreCase) || valor.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            var check = new CheckBox
            {
                Content = TituloClave(clave),
                IsChecked = valor.Equals("true", StringComparison.OrdinalIgnoreCase),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = Recurso("ColorTextoSuave", Brushes.Gray),
                FontSize = 12,
                ToolTip = DescripcionesRandomizer.Para(clave, valor),
            };
            ToolTipService.SetShowDuration(check, 30000);
            ToolTipService.SetInitialShowDelay(check, 350);
            check.Checked += (_, _) => Guardar(clave, "true");
            check.Unchecked += (_, _) => Guardar(clave, "false");
            fila.Children.Add(check);
            return fila;
        }

        // ── Opciones mutuamente excluyentes (.mod y claves con valores del motor): radio buttons ──
        string[] opcionesRadio;
        if (clave.EndsWith(".mod", StringComparison.OrdinalIgnoreCase))
        {
            opcionesRadio = ValoresParaClave(clave, valor);
        }
        else if (DescripcionesRandomizer.ValoresMotor.TryGetValue(clave, out var _validosMotor))
        {
            opcionesRadio = _validosMotor;
        }
        else
        {
            opcionesRadio = Array.Empty<string>();
        }

        if (opcionesRadio.Length > 0)
        {
            var panelRadios = new StackPanel { Orientation = Orientation.Vertical };
            foreach (var opcion in opcionesRadio)
            {
                var radio = new RadioButton
                {
                    Content = opcion,
                    IsChecked = string.Equals(opcion, valor, StringComparison.OrdinalIgnoreCase),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 4),
                    Foreground = Recurso("ColorTextoSuave", Brushes.Gray),
                    FontSize = 12,
                    ToolTip = DescripcionesRandomizer.ParaValor(clave, opcion),
                };
                ToolTipService.SetShowDuration(radio, 30000);
                ToolTipService.SetInitialShowDelay(radio, 350);
                var op = opcion; // cierre para el lambda
                radio.Checked += (_, _) => Guardar(clave, op);
                panelRadios.Children.Add(radio);
            }
            fila.Children.Add(panelRadios);
            return fila;
        }

        // ── Enumerados conocidos: desplegable compacto ──
        if (Enumerados.TryGetValue(clave, out var opcionesEnum))
        {
            var combo = new ComboBox { MaxDropDownHeight = 300, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var opcion in opcionesEnum)
            {
                combo.Items.Add(new ComboBoxItem { Content = opcion, ToolTip = DescripcionesRandomizer.ParaValor(clave, opcion) });
            }
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals(x.Content?.ToString(), valor, StringComparison.OrdinalIgnoreCase));
            if (combo.SelectedItem is null) { var propio = new ComboBoxItem { Content = valor }; combo.Items.Add(propio); combo.SelectedItem = propio; }
            combo.SelectionChanged += (_, _) => Guardar(clave, ValorCombo(combo));
            fila.Children.Add(combo);
            return fila;
        }

        // ── Probabilidad de shiny: % en vez de 1/N ──
        if (clave.EndsWith(".shinyChanceOneIn", StringComparison.OrdinalIgnoreCase))
        {
            var unoDe = int.TryParse(valor, out var n) ? n : 0;
            var cajaPct = new TextBox
            {
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Left,
                Text = unoDe > 0 ? (100.0 / unoDe).ToString("0.####", CultureInfo.InvariantCulture) : "0",
            };
            var detalle = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = Recurso("ColorTextoSuave", Brushes.Gray),
                Text = unoDe > 0 ? $"Probabilidad: 1 de cada {unoDe}" : "Desactivado (0 %)",
            };
            void Aplicar()
            {
                if (!double.TryParse(cajaPct.Text.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var pct) || pct <= 0)
                {
                    Guardar(clave, "0");
                    detalle.Text = "Desactivado (0 %)";
                    return;
                }
                var nuevoN = Math.Max(1, (int)Math.Round(100.0 / pct));
                Guardar(clave, nuevoN.ToString(CultureInfo.InvariantCulture));
                detalle.Text = $"Probabilidad: 1 de cada {nuevoN} (≈ {pct:0.####} %)";
            }
            cajaPct.LostFocus += (_, _) => Aplicar();
            cajaPct.KeyDown += (_, e) => { if (e.Key == Key.Enter) Aplicar(); };
            var panelPct = new StackPanel();
            panelPct.Children.Add(cajaPct);
            panelPct.Children.Add(detalle);
            fila.Children.Add(panelPct);
            return fila;
        }

        // ── Rangos numéricos: slider + valor ──
        if (Rangos.TryGetValue(clave, out var rango)
                 && double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numero))
        {
            var slider = new Slider
            {
                Minimum = rango.Min,
                Maximum = rango.Max,
                Value = Math.Clamp(numero, rango.Min, rango.Max),
                IsSnapToTickEnabled = rango.Max <= 20,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 220,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var valorTexto = new TextBlock
            {
                Text = ((int)slider.Value) + rango.Sufijo,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = Recurso("ColorTexto", Brushes.White),
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(8, 0, 0, 0),
            };
            slider.ValueChanged += (_, e) =>
            {
                var v = (int)Math.Round(e.NewValue);
                valorTexto.Text = v + rango.Sufijo;
                Guardar(clave, v.ToString(CultureInfo.InvariantCulture));
            };
            var panelSlider = new StackPanel { Orientation = Orientation.Horizontal };
            panelSlider.Children.Add(slider);
            panelSlider.Children.Add(valorTexto);
            fila.Children.Add(panelSlider);
            _controles[clave] = slider;
            return fila;
        }

        // ── Número entero: campo pequeño ──
        if (int.TryParse(valor, out _))
        {
            var caja = new TextBox { Text = valor, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            caja.LostFocus += (_, _) => Guardar(clave, caja.Text.Trim());
            caja.KeyDown += (_, e) => { if (e.Key == Key.Enter) Guardar(clave, caja.Text.Trim()); };
            fila.Children.Add(caja);
            return fila;
        }

        // ── Texto libre ──
        var cajaTexto = new TextBox { Text = valor, HorizontalAlignment = HorizontalAlignment.Left };
        cajaTexto.LostFocus += (_, _) => Guardar(clave, cajaTexto.Text.Trim());
        cajaTexto.KeyDown += (_, e) => { if (e.Key == Key.Enter) Guardar(clave, cajaTexto.Text.Trim()); };
        fila.Children.Add(cajaTexto);
        return fila;
    }

    /// <summary>Valores admitidos: los EXACTOS del motor, y si no hay tabla, los vistos en presets.</summary>
    private string[] ValoresParaClave(string clave, string actual)
    {
        if (DescripcionesRandomizer.ValoresMotor.TryGetValue(clave, out var exactos))
        {
            var lista = exactos.ToList();
            if (!lista.Any(v => string.Equals(v, actual, StringComparison.OrdinalIgnoreCase))) lista.Add(actual);
            return lista.ToArray();
        }
        var base_ = new List<string> { "UNCHANGED" };
        if (Enumerados.TryGetValue(clave, out var conocidos)) base_.AddRange(conocidos);
        if (_valoresObservados.TryGetValue(clave, out var observados)) base_.AddRange(observados);
        base_.Add(actual);
        return base_.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Los presets del vendor son la fuente más fiable de valores válidos del motor.</summary>
    private void RecolectarValoresObservados()
    {
        try
        {
            var dir = Path.Combine(DirRandomizer, "presets");
            if (!Directory.Exists(dir)) return;
            foreach (var preset in Directory.GetFiles(dir, "*.rnqs"))
            {
                foreach (var linea in File.ReadAllLines(preset))
                {
                    var limpia = linea.Trim();
                    if (limpia.Length == 0 || limpia.StartsWith('#')) continue;
                    var igual = limpia.IndexOf('=');
                    if (igual <= 0) continue;
                    var clave = limpia[..igual].Trim();
                    var valor = limpia[(igual + 1)..].Trim();
                    if (!_valoresObservados.TryGetValue(clave, out var lista))
                        lista = [];
                    if (!lista.Contains(valor, StringComparer.OrdinalIgnoreCase))
                        _valoresObservados[clave] = [.. lista, valor];
                    else
                        _valoresObservados[clave] = lista;
                }
            }
        }
        catch { /* sin presets: quedan los valores conocidos */ }
    }

    private void VolcarControles()
    {
        foreach (var (clave, control) in _controles)
        {
            var valor = control switch
            {
                ComboBox combo => ValorCombo(combo),
                CheckBox check => check.IsChecked == true ? "true" : "false",
                TextBox caja => caja.Text.Trim(),
                _ => "",
            };
            if (valor.Length > 0) _settings.Set(clave, valor);
        }
    }

    /// <summary>Texto seleccionado de un ComboBox cuyos items son ComboBoxItem.</summary>
    private static string ValorCombo(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? combo.SelectedItem?.ToString() ?? "";

    private void Guardar(string clave, string valor)
    {
        if (valor.Length > 0) _settings.Set(clave, valor);
    }

    private void ResetCategoria_Click(object sender, RoutedEventArgs e)
    {
        if (_prefijoActual.Length == 0) return;
        var defecto = new RandomizerSettings();
        if (TabsCategorias.SelectedItem is TabItem tab && tab.Tag is CategoriaGrupo grupo)
        {
            foreach (var clave in _settings.Valores.Keys
                         .Where(k => grupo.Prefijos.Any(p => k.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase)))
                         .ToList())
            {
                var valor = defecto.Get(clave);
                if (valor is not null) _settings.Set(clave, valor);
            }
            MostrarCategoria(grupo);
            Log("Categoría restablecida: " + string.Join(", ", grupo.Prefijos));
        }
    }

    // ─────────────────────────── rueda / compatibilidad ───────────────────────────

    /// <summary>Generación aproximada según el formato de ROM (informativo).</summary>
    private static int GeneracionDeRom(string? ruta)
    {
        var ext = Path.GetExtension(ruta ?? "").ToLowerInvariant();
        return ext switch
        {
            ".gb" or ".gbc" => 2,
            ".gba" => 3,
            ".nds" => 4,
            ".3ds" or ".cia" or ".cci" or ".cxi" => 6,
            ".xci" or ".xcz" or ".nsp" or ".nsz" => 8,
            _ => 0,
        };
    }

    private static int RequisitoGeneracion(string clave)
    {
        var k = clave.ToLowerInvariant();
        if (k.Contains("mega")) return 6;
        if (k.Contains("totem")) return 7;
        return 1;
    }

    // ─────────────────────────── presets y archivos ───────────────────────────

    private string DirRandomizer => Path.Combine(_ajustes.VendorDir, "SuperRandomizer");

    /// <summary>Carpeta de presets propios del launcher (se crean al guardar un preset).</summary>
    private string DirPresetsPropios
    {
        get
        {
            var baseDir = !string.IsNullOrWhiteSpace(_ajustes.DatosDir) ? _ajustes.DatosDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PokeLockeLauncher");
            var dir = Path.Combine(baseDir, "presets_randomizer");
            try { Directory.CreateDirectory(dir); } catch { /* se usará igual si existe */ }
            return dir;
        }
    }

    private void CargarPresetsVendor()
    {
        CmbPresetVendor.Items.Clear();
        try
        {
            var dir = Path.Combine(DirRandomizer, "presets");
            if (!Directory.Exists(dir)) return;
            foreach (var archivo in Directory.GetFiles(dir, "*.rnqs").OrderBy(f => f))
                CmbPresetVendor.Items.Add(Path.GetFileNameWithoutExtension(archivo));
        }
        catch { }
    }

    private void CargarPresetVendor_Click(object sender, RoutedEventArgs e)
    {
        if (CmbPresetVendor.SelectedItem is not string nombre)
        {
            Log("Elige un preset del vendor.");
            return;
        }
        try
        {
            var anterior = _settings.Get("liga.nivel100");
            _settings = RandomizerSettings.DesdePreset(DirRandomizer, nombre);
            _settings.Set("liga.nivel100", anterior);
            RefrescarTodo();
            Log("Preset «" + nombre + "» cargado (" + _settings.Valores.Count + " claves). Liga100=" + anterior);
        }
        catch (Exception ex) { Log("No se pudo cargar el preset: " + ex.Message); }
    }

    private void CargarArchivo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Cargar ajustes del randomizador",
            Filter = "Ajustes del randomizador (*.rnqs;*.properties)|*.rnqs;*.properties|Todos los archivos|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var anterior = _settings.Get("liga.nivel100");
            _settings = RandomizerSettings.Cargar(dlg.FileName);
            _settings.Set("liga.nivel100", anterior);
            RefrescarTodo();
            Log("Ajustes cargados de " + Path.GetFileName(dlg.FileName) + " (" + _settings.Valores.Count + " claves). Liga100=" + anterior);
        }
        catch (Exception ex) { Log("Error al cargar: " + ex.Message); }
    }

    private void GuardarArchivo_Click(object sender, RoutedEventArgs e)
    {
        VolcarControles();
        var dlg = new SaveFileDialog
        {
            Title = "Guardar ajustes del randomizador",
            Filter = "Ajustes del randomizador (*.rnqs)|*.rnqs",
            FileName = "mis-ajustes.rnqs",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _settings.Guardar(dlg.FileName);
            Log("Ajustes guardados en " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { Log("Error al guardar: " + ex.Message); }
    }

    private void Restablecer_Click(object sender, RoutedEventArgs e)
    {
        _settings = new RandomizerSettings();
        RefrescarTodo();
        Log("Ajustes restablecidos (valores por defecto del motor).");
    }

    private void RefrescarTodo()
    {
        LlenarCategorias();
    }

    private void ElegirRom_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecciona la ROM a randomizar",
            Filter = "ROMs|*.gba;*.gb;*.gbc;*.nds;*.3ds;*.cci;*.cia;*.cxi;*.xci;*.xcz;*.nsp;*.nsz|Todos los archivos|*.*",
        };
        if (dlg.ShowDialog() == true) PonerRom(dlg.FileName);
    }

    // ─────────────────────────── randomizar ───────────────────────────

    private async void Randomizar_Click(object sender, RoutedEventArgs e)
        => await EjecutarRandomizacionAsync(guardarPresetPrimero: false);

    /// <summary>Guarda los ajustes actuales como un preset propio del launcher.</summary>
    private void GuardarPresetPropio_Click(object sender, RoutedEventArgs e)
    {
        VolcarControles();
        var dlg = new SaveFileDialog
        {
            Title = "Guardar preset propio del randomizador",
            Filter = "Preset del randomizador (*.rnqs)|*.rnqs|Todos los archivos|*.*",
            FileName = $"preset_{DateTime.Now:yyyyMMdd-HHmmss}.rnqs",
            InitialDirectory = DirPresetsPropios,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _settings.Guardar(dlg.FileName);
            Log("Preset propio guardado en " + Path.GetFileName(dlg.FileName));
            TxtEstado.Text = "Preset guardado: " + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex) { Log("Error al guardar preset: " + ex.Message); }
    }

    /// <summary>Guarda los ajustes como preset y luego randomiza la ROM.</summary>
    private async void GuardarPresetYRandomizar_Click(object sender, RoutedEventArgs e)
        => await EjecutarRandomizacionAsync(guardarPresetPrimero: true);

    /// <summary>
    /// Randomiza la ROM con los ajustes actuales. Si <paramref name="guardarPresetPrimero"/> es true,
    /// guarda primero los ajustes como un preset propio (con timestamp) antes de enviar al motor.
    /// </summary>
    private async Task EjecutarRandomizacionAsync(bool guardarPresetPrimero)
    {
        var rom = TxtRom.Text.Trim();
        if (rom.Length == 0 || !File.Exists(rom))
        {
            Log("Elige primero la ROM a randomizar.");
            return;
        }
        var servicio = new RandomizerService(_ajustes.VendorDir);
        if (!servicio.InstalacionCompleta)
        {
            Log("No encuentro SuperRandomizer/jre en vendor: revisa ⚙ Ajustes.");
            return;
        }
        VolcarControles();

        if (guardarPresetPrimero)
        {
            try
            {
                var rutaPreset = Path.Combine(DirPresetsPropios, $"preset_{DateTime.Now:yyyyMMdd-HHmmss}.rnqs");
                _settings.Guardar(rutaPreset);
                Log("Ajustes guardados como preset propio: " + Path.GetFileName(rutaPreset));
            }
            catch (Exception ex) { Log("No se pudo guardar el preset previo: " + ex.Message); }
        }

        // Pide al usuario el nombre y destino del archivo de salida
        var dlgSalida = new SaveFileDialog
        {
            Title = "Guardar ROM randomizada como…",
            Filter = "ROMs|*.gba;*.gb;*.gbc;*.nds;*.3ds;*.cci;*.cia;*.cxi;*.xci;*.xcz;*.nsp;*.nsz|Todos los archivos|*.*",
            FileName = Path.GetFileNameWithoutExtension(rom) + "_randomized" + Path.GetExtension(rom),
        };
        if (dlgSalida.ShowDialog() != true)
        {
            BtnRandomizar.IsEnabled = true;
            BtnGuardarPresetYRandomizar.IsEnabled = true;
            return;
        }
        var salida = dlgSalida.FileName;

        BtnRandomizar.IsEnabled = false;
        BtnGuardarPresetYRandomizar.IsEnabled = false;
        TxtEstado.Text = "Randomizando… (puede tardar varios minutos)";
        Log("Iniciando: " + Path.GetFileName(rom));

        // Sanea valores .mod que el motor no admite (p. ej. COMPLETELY_RANDOM en habilidades):
        // sin esto el motor los ignora en silencio y parece que "no hace nada".
        foreach (var (claveMod, validos) in DescripcionesRandomizer.ValoresMotor)
        {
            var actualMod = _settings.Get(claveMod);
            if (string.IsNullOrWhiteSpace(actualMod)) continue;
            if (validos.Any(v => string.Equals(v, actualMod, StringComparison.OrdinalIgnoreCase))) continue;
            _settings.Set(claveMod, validos[0]);
            Log($"⚠ {claveMod}: «{actualMod}» no es válido en el motor; se usa «{validos[0]}».");
        }

        var liga100 = string.Equals(_settings.Get("liga.nivel100"), "true", StringComparison.OrdinalIgnoreCase);

        try
        {
            var resultado = await servicio.RandomizarAsync(rom, salida, _settings);
            Log("ExitCode " + resultado.ExitCode + ": " + resultado.Motivo);
            if (!string.IsNullOrWhiteSpace(resultado.Salida)) Log(resultado.Salida.Trim());
            if (resultado.Ok && File.Exists(salida))
            {
                TxtEstado.Text = "Randomizado completado";
                Log("ROM guardada en: " + salida);

                if (liga100)
                {
                    Log("Poniendo la liga (6 entrenadores de mayor nivel) al 100…");
                    var liga = await servicio.LigaANivel100Async(salida, 6);
                    Log(liga.Ok ? "Liga: " + liga.Salida.Trim() : "ERROR liga: " + liga.SalidaError.Trim());
                }
                _dueno?.AnadirRomRandomizada(salida);
            }
            else
            {
                TxtEstado.Text = "El randomizador falló: " + resultado.Motivo;
                if (!string.IsNullOrWhiteSpace(resultado.SalidaError)) Log(resultado.SalidaError.Trim());
            }
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "Error al randomizar: " + ex.Message;
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            BtnRandomizar.IsEnabled = true;
            BtnGuardarPresetYRandomizar.IsEnabled = true;
        }
    }

    // ─────────────────────────── utilidades ───────────────────────────

    private static string TituloClave(string clave)
    {
        // Nombres legibles: el motor llama «percentageLevelModifier» a la CASILLA DE ACTIVAR, y
        // el valor (levelModifier / levelModifierValue) es el porcentaje que se aplica.
        if (clave.EndsWith(".percentageLevelModifier", StringComparison.OrdinalIgnoreCase)) return "Activar modificador de nivel (%)";
        if (clave.EndsWith(".levelModifierValue", StringComparison.OrdinalIgnoreCase)) return "Modificador de nivel (%)";
        if (clave.EndsWith(".levelModifier", StringComparison.OrdinalIgnoreCase)) return "Modificador de nivel (%)";
        if (clave.Equals("trainers.levelModified", StringComparison.OrdinalIgnoreCase)) return "Activar modificador de nivel (%)";
        return TituloClaveLargo(clave);
    }

    private static string TituloClaveLargo(string clave)
    {
        var partes = clave.Split('.');
        var resto = partes.Length > 1 ? partes[1] : clave;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in resto)
        {
            if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' ');
            sb.Append(ch);
        }
        var texto = sb.ToString();
        return texto.Length == 0 ? clave : char.ToUpperInvariant(texto[0]) + texto[1..];
    }

    private static Brush Recurso(string clave, Brush porDefecto)
        => (Application.Current?.TryFindResource(clave) as Brush) ?? porDefecto;

    private void BtnAyuda_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Randomizador de Pokelobby\n\n" +
            "1. Elige la ROM a randomizar.\n" +
            "2. Configura las opciones en cada pestaña de características.\n" +
            "3. Pulsa «Randomizar ROM» o «Guardar preset y randomizar» para crear el preset y lanzar el motor.\n" +
            "\nLos presets propios se guardan en la carpeta presets_randomizer/ de los datos del launcher.",
            "Ayuda — Randomizador",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Log(string linea)
    {
        TxtEstado.Text = $"[{DateTime.Now:HH:mm:ss}] {linea}";
    }
}