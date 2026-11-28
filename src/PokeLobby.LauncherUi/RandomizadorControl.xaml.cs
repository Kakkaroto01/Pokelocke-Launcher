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

    /// <summary>Claves antiguas que algunos presets del vendor todavía incluyen pero que el motor
    /// actual ya no lee. No se muestran para que no parezcan opciones funcionales.</summary>
    private static readonly HashSet<string> ClavesObsoletas = new(StringComparer.OrdinalIgnoreCase)
    {
        "trainers.doubleBattle",
        "trainers.randomShiny",
    };
    private bool _cargado;
    private string _prefijoActual = "";

    /// <summary>Alto común de desplegables y pills para que todos los controles de una fila midan igual.</summary>
    private const double AltoControlRando = 38;

    /// <summary>Separación horizontal uniforme entre celdas de una misma fila.</summary>
    private const double SeparacionOpcionesRando = 6;

    /// <summary>Rangos conocidos para claves numéricas (mínimo, máximo, prefijo, sufijo).</summary>
    private static readonly Dictionary<string, (double Min, double Max, string Prefijo, string Sufijo)> Rangos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["movesets.forceGoodDamagingPercent"] = (0, 100, "", "%"),
        ["trainers.movesetForceGoodDamagingPercent"] = (0, 100, "", "%"),
        ["tmmoves.forceGoodDamagingPercent"] = (0, 100, "", "%"),
        ["tutormoves.forceGoodDamagingPercent"] = (0, 100, "", "%"),
        ["statics.levelModifierValue"] = (-50, 100, "", "%"),
        ["wild.levelModifierValue"] = (-50, 100, "", "%"),
        ["totems.levelModifierValue"] = (-50, 100, "", "%"),
        ["statics.bstMargin"] = (0, 200, "", " pts"),
        ["evolutions.tradeLevel"] = (1, 100, "Nv. ", ""),
        // 0 = sin límite (valor por defecto del motor); no debe convertirse en Nv. 1 al abrir.
        ["evolutions.maxLevel"] = (0, 100, "Nv. ", ""),
        ["movesets.guaranteedLevel1Min"] = (1, 10, "", ""),
        ["trainers.additionalBossCount"] = (0, 6, "", ""),
        ["trainers.additionalImportantCount"] = (0, 6, "", ""),
        ["trainers.additionalRegularCount"] = (0, 6, "", ""),
        ["trainers.legendPerTeam"] = (0, 6, "", ""),
        ["trainers.legendMinPerTeam"] = (0, 6, "", ""),
        ["trainers.leagueUniqueCount"] = (0, 6, "", ""),
        ["basestats.updateGenerationTo"] = (0, 9, "Gen ", ""),
        ["movedata.updateGenerationTo"] = (0, 9, "Gen ", ""),
    };

    // ── Mapa de columnas para opciones radio/pill por sección ──
    // Devuelve el número de columnas uniformes para las opciones de selección única de cada subsección.
    private static int ColumnasRadioParaSubseccion(string subseccion, string prefijoReal)
    {
        return (prefijoReal.ToLowerInvariant(), subseccion) switch
        {
            // Características
            ("basestats", "Estadísticas Base") => 2,   // Modo aleatorización → 1 fila (calculado dinámico)
            ("evolutions", "Evoluciones") => 2,
            ("abilities", "Habilidades") => 2,
            ("types", "Tipos") => 1,                   // 3 filas de 1 → columna única, 3 ítems
            // Iniciales y Estáticos
            ("statics", "Estáticos") => 4,             // 4 opciones en 1 fila
            ("starters", "Iniciales") => 2,            // 2×2
            ("trades", "Intercambios") => 2,
            // Movimientos
            ("movesets", "Movesets") => 2,             // 2×2
            ("tmmoves", "MT/MO") => 2,                 // Movimientos MT: 2 en horizontal
            ("tmcompat", "MT/MO") => 2,                // Compatibilidad MT/MO: 2×2
            ("tutorcompat", "Tutores") => 2,
            // Salvajes
            ("wild", "Salvajes") => 2,                 // Modo encuentros 2×2, Regla adicional 2×2
            ("totems", "Tótems") => 1,                 // 3 columnas de 3 opciones cada una
            // Entrenadores
            ("trainers", "Modo de Entrenadores") => 3, // 2 filas × 3 columnas
            _ => 2,
        };
    }

    // ── Mapa de columnas para checkboxes por sección ──
    private static int ColumnasCheckParaSubseccion(string subseccion, string prefijoReal, bool esTarjetaEstrecha, bool esTarjetaAncha)
    {
        if (esTarjetaEstrecha) return 1;
        return (prefijoReal.ToLowerInvariant(), subseccion) switch
        {
            ("trainers", "Opciones Generales") => 3,   // 3 columnas: Jefes, Importantes, Normales
            _ => esTarjetaAncha ? 3 : 2,
        };
    }

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
        var ext = Path.GetExtension(rutaRom).ToLowerInvariant();
        var consola = ext switch
        {
            ".nds" => "ROM compatible · NDS",
            ".gba" => "ROM compatible · GBA",
            ".gb" or ".gbc" => "ROM compatible · GB/GBC",
            ".3ds" or ".cia" or ".cxi" => "ROM compatible · 3DS",
            _ => "Archivo seleccionado",
        };
        TxtCompatRom.Text = consola;
        TxtCompatRom.Foreground = Recurso("ColorOk", Brushes.LimeGreen);
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
        new("Movimientos",            ["movedata", "movesets", "tmmoves", "tmcompat", "tutormoves", "tutorcompat"]),
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

        // Las pestañas son botones en el XAML (BotonCategoria). Seleccionar la primera presente.
        foreach (var btn in new[] { BtnTabCaracteristicas, BtnTabIniciales, BtnTabMovimientos,
                                    BtnTabSalvajes, BtnTabEntrenadores, BtnTabObjetos, BtnTabMiscelanea })
        {
            var grupo = CategoriasAgrupadas.FirstOrDefault(g => g.Titulo == btn.Content.ToString());
            btn.Visibility = grupo != null && grupo.Prefijos.Any(p => gruposPresentes.Contains(p))
                ? Visibility.Visible : Visibility.Collapsed;
            btn.Tag = null;
        }

        // Marcar la primera visible como activa
        var primero = new[] { BtnTabCaracteristicas, BtnTabIniciales, BtnTabMovimientos,
                              BtnTabSalvajes, BtnTabEntrenadores, BtnTabObjetos, BtnTabMiscelanea }
            .FirstOrDefault(b => b.Visibility == Visibility.Visible);
        if (primero is not null)
        {
            primero.Tag = "activo";
            var grupo = CategoriasAgrupadas.First(g => g.Titulo == primero.Content.ToString());
            MostrarCategoria(grupo);
        }
    }

    private void TabCategoria_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        foreach (var b in new[] { BtnTabCaracteristicas, BtnTabIniciales, BtnTabMovimientos,
                                  BtnTabSalvajes, BtnTabEntrenadores, BtnTabObjetos, BtnTabMiscelanea })
            b.Tag = null;
        btn.Tag = "activo";

        var grupo = CategoriasAgrupadas.FirstOrDefault(g => g.Titulo == btn.Content.ToString());
        if (grupo is not null)
            MostrarCategoria(grupo);
    }

    private void MostrarCategoria(CategoriaGrupo grupo)
    {
        var opciones = _settings.Valores
            .Where(kv => grupo.Prefijos.Any(p => kv.Key.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase)))
            .Where(kv => !ClavesObsoletas.Contains(kv.Key))
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
            // Agrupar opciones en UNA tarjeta por subcategoría (Estadísticas Base, Evoluciones, etc.)
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

            var subgruposList = subgrupos.ToList();
            if (grupo.Titulo is "Pokémon Salvajes" or "Miscelánea")
            {
                // Tarjetas a ancho completo apiladas (Salvajes + Tótems, Liga + Varios)
                foreach (var subgrupo in subgruposList)
                {
                    var tarjeta = CrearTarjeta(subgrupo.Key, subgrupo.Select(x => (x.Clave, x.Valor)).ToList(), subgrupo.First().PrefijoReal);
                    tarjeta.Margin = new Thickness(0, 0, 0, 18);
                    PanelOpciones.Children.Add(tarjeta);
                }
            }
            else if (grupo.Titulo == "Objetos" && subgruposList.Count == 3)
            {
                // 3 columnas iguales (Objetos de Campo, Tiendas, Recogida)
                var grid3 = new Grid();
                for (var c = 0; c < 3; c++)
                    grid3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (var c = 0; c < 3; c++)
                {
                    var sg = subgruposList[c];
                    var tarjeta = CrearTarjeta(sg.Key, sg.Select(x => (x.Clave, x.Valor)).ToList(), sg.First().PrefijoReal);
                    tarjeta.Margin = new Thickness(c == 0 ? 0 : 8, 0, c == 2 ? 0 : 8, 18);
                    Grid.SetColumn(tarjeta, c);
                    grid3.Children.Add(tarjeta);
                }
                PanelOpciones.Children.Add(grid3);
            }
            else if (grupo.Titulo == "Movimientos")
            {
                // Movesets y MT/MO a ancho completo: así «Compatibilidad MT/MO» puede mostrar
                // sus 4 opciones en una sola fila sin recortar el texto.
                var aAncho = new[] { "Movesets", "MT/MO" }
                    .Select(titulo => subgruposList.FirstOrDefault(s => s.Key == titulo))
                    .Where(s => s is not null)
                    .Select(s => s!)
                    .ToList();
                var resto = subgruposList.Except(aAncho).ToList();

                foreach (var sg in aAncho)
                {
                    var tarjeta = CrearTarjeta(sg.Key, sg.Select(x => (x.Clave, x.Valor)).ToList(), sg.First().PrefijoReal);
                    tarjeta.Margin = new Thickness(0, 0, 0, 18);
                    PanelOpciones.Children.Add(tarjeta);
                }

                if (resto.Count > 0)
                {
                    PanelOpciones.Children.Add(ConstruirGridDosColumnas(
                        resto.Select(s => (s.Key, s.Select(x => (x.Clave, x.Valor)).ToList(), s.First().PrefijoReal)).ToList()));
                }
            }
            else if (grupo.Titulo is "Iniciales y Estáticos" or "Entrenadores")
            {
                // Tarjetas principales a ancho completo arriba, el resto en grid de 2 columnas
                var anchoCompleto = grupo.Titulo == "Iniciales y Estáticos"
                    ? subgruposList.Where(s => s.Key == "Estáticos").ToList()
                    : subgruposList.Where(s => s.Key is "Modo de Entrenadores" or "Opciones Generales").ToList();
                var enGrid = subgruposList.Except(anchoCompleto).ToList();

                foreach (var sg in anchoCompleto)
                {
                    var tarjeta = CrearTarjeta(sg.Key, sg.Select(x => (x.Clave, x.Valor)).ToList(), sg.First().PrefijoReal);
                    tarjeta.Margin = new Thickness(0, 0, 0, 18);
                    PanelOpciones.Children.Add(tarjeta);
                }
                if (enGrid.Count > 0)
                    PanelOpciones.Children.Add(ConstruirGridDosColumnas(enGrid.Select(s => (s.Key, s.Select(x => (x.Clave, x.Valor)).ToList(), s.First().PrefijoReal)).ToList()));
            }
            else if (subgruposList.Count == 1)
            {
                var subgrupo = subgruposList[0];
                var tarjeta = CrearTarjeta(subgrupo.Key, subgrupo.Select(x => (x.Clave, x.Valor)).ToList(), subgrupo.First().PrefijoReal);
                tarjeta.Margin = new Thickness(0, 0, 0, 18);
                PanelOpciones.Children.Add(tarjeta);
            }
            else
            {
                PanelOpciones.Children.Add(ConstruirGridDosColumnas(
                    subgruposList.Select(s => (s.Key, s.Select(x => (x.Clave, x.Valor)).ToList(), s.First().PrefijoReal)).ToList()));
            }
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

    private Grid ConstruirGridDosColumnas(List<(string Titulo, List<(string Clave, string Valor)> Opciones, string Prefijo)> items)
    {
        var gridTarjetas = new Grid();
        gridTarjetas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridTarjetas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var filaIdx = 0;
        var colIdx = 0;
        foreach (var item in items)
        {
            if (colIdx == 0)
                gridTarjetas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tarjeta = CrearTarjeta(item.Titulo, item.Opciones, item.Prefijo);
            tarjeta.Margin = colIdx == 0 ? new Thickness(0, 0, 9, 18) : new Thickness(9, 0, 0, 18);
            Grid.SetRow(tarjeta, filaIdx);
            Grid.SetColumn(tarjeta, colIdx);
            gridTarjetas.Children.Add(tarjeta);

            colIdx++;
            if (colIdx >= 2)
            {
                colIdx = 0;
                filaIdx++;
            }
        }
        return gridTarjetas;
    }

    /// <summary>Crea una tarjeta (Border) con título y las opciones dadas, al estilo Rediseño.</summary>
    private Border CrearTarjeta(string titulo, List<(string clave, string valor)> opciones, string prefijoReal)
    {
        var tarjeta = new Border
        {
            Background = Recurso("ColorPanelContainer", Brushes.Transparent),
            BorderBrush = Recurso("ColorBordeVariant", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var stack = new StackPanel();

        // Cabecera de la tarjeta: título a la izquierda + prefijo monospace a la derecha + borde inferior
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tituloTarjeta = new TextBlock
        {
            Text = titulo,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Recurso("ColorTexto", Brushes.White),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tituloTarjeta, 0);
        headerGrid.Children.Add(tituloTarjeta);

        var textoTag = titulo == "Varios" ? $"{prefijoReal} · {opciones.Count} opciones" : prefijoReal;
        var tagPrefijo = new TextBlock
        {
            Text = textoTag,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            Foreground = Recurso("ColorTextoSuave", Brushes.Gray),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(tagPrefijo, 1);
        headerGrid.Children.Add(tagPrefijo);

        var headerBorder = new Border
        {
            BorderBrush = Recurso("ColorBordeVariant", Brushes.Gray),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 10),
            Margin = new Thickness(0, 0, 0, 14),
            Child = headerGrid,
        };
        stack.Children.Add(headerBorder);

        // Separar opciones en bloques equilibrados como en las imágenes de referencia:
        // 1) Opciones principales (radios, desplegables, campos numéricos) → layout según subsección
        // 2) Casillas de verificación → layout según subsección
        // 3) Deslizables (sliders con etiqueta + valor + pista roja) → siempre ancho completo
        var filtradas = opciones
            .Where(o => !o.clave.Equals("trainers.percentageLevelModifier", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var esBool = (Func<(string clave, string valor), bool>)(o =>
            o.valor.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            o.valor.Equals("false", StringComparison.OrdinalIgnoreCase));

        var esSlider = (Func<(string clave, string valor), bool>)(o =>
            Rangos.ContainsKey(o.clave) &&
            double.TryParse(o.valor, NumberStyles.Any, CultureInfo.InvariantCulture, out _));

        var principales = filtradas.Where(o => !esBool(o) && !esSlider(o)).ToList();
        var checks = filtradas.Where(esBool).ToList();
        var sliders = filtradas.Where(esSlider).ToList();

        // Utilidades locales para componer las secciones con el orden exacto del rediseño.
        var valores = filtradas
            .GroupBy(o => o.clave, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().valor, StringComparer.OrdinalIgnoreCase);

        FrameworkElement? Opcion(string clave, int columnasRadios = 0)
            => valores.TryGetValue(clave, out var v)
                ? CrearFilaOpcion(clave, ValorSaneado(clave, v), columnasRadios)
                : null;

        // Algunos presets antiguos traen valores .mod que el motor ya no admite (p. ej.
        // COMPLETELY_RANDOM en entrenadores). Se normalizan al primer valor válido para que
        // la fila no gane una opción extra y el estado mostrado sea el que se enviará al motor.
        string ValorSaneado(string clave, string valor)
        {
            if (!DescripcionesRandomizer.ValoresMotor.TryGetValue(clave, out var validos) ||
                validos.Length == 0)
            {
                return valor;
            }

            if (validos.Any(v => string.Equals(v, valor, StringComparison.OrdinalIgnoreCase)))
            {
                return valor;
            }

            _settings.Set(clave, validos[0]);
            return validos[0];
        }

        void Agregar(StackPanel destino, FrameworkElement? elemento, double margenSuperior = 0)
        {
            if (elemento is null) return;
            if (margenSuperior > 0)
                elemento.Margin = new Thickness(
                    elemento.Margin.Left,
                    elemento.Margin.Top + margenSuperior,
                    elemento.Margin.Right,
                    elemento.Margin.Bottom);
            destino.Children.Add(elemento);
        }

        void AgregarChecks(StackPanel destino, IEnumerable<string> claves, int columnas, double margenSuperior = 0)
        {
            var elementos = claves
                .Where(valores.ContainsKey)
                .Select(k => CrearFilaOpcion(k, valores[k]))
                .ToList();
            if (elementos.Count == 0) return;
            var grid = ColocarEnGrid(elementos, columnas);
            grid.Margin = new Thickness(0, margenSuperior, 0, 0);
            destino.Children.Add(grid);
        }

        Grid? FilaHorizontal(FrameworkElement? izquierda, FrameworkElement? derecha, double separacion = 16)
        {
            if (izquierda is null && derecha is null) return null;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (izquierda is not null)
            {
                izquierda.Margin = new Thickness(
                    izquierda.Margin.Left,
                    izquierda.Margin.Top,
                    izquierda.Margin.Right + separacion / 2,
                    izquierda.Margin.Bottom);
                Grid.SetColumn(izquierda, 0);
                grid.Children.Add(izquierda);
            }
            if (derecha is not null)
            {
                derecha.Margin = new Thickness(
                    derecha.Margin.Left + separacion / 2,
                    derecha.Margin.Top,
                    derecha.Margin.Right,
                    derecha.Margin.Bottom);
                Grid.SetColumn(derecha, 1);
                grid.Children.Add(derecha);
            }
            return grid;
        }

        // Casilla + campo numérico en la misma línea (p. ej. «Forzar evol. completa» + nivel).
        FrameworkElement? FilaCheckConCampo(string claveCheck, string claveCampo)
        {
            var check = Opcion(claveCheck);
            var campo = Opcion(claveCampo);
            if (check is null && campo is null) return null;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (check is not null)
            {
                check.Margin = new Thickness(0, 6, 16, 6);
                check.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(check, 0);
                grid.Children.Add(check);
            }
            if (campo is not null)
            {
                campo.Margin = new Thickness(0, 0, 0, 0);
                campo.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(campo, 1);
                grid.Children.Add(campo);
            }
            return grid;
        }

        // ── Estadísticas Base: curva EXP + objetivo (apilado) + modo en una fila ──
        if (prefijoReal.Equals("basestats", StringComparison.OrdinalIgnoreCase) && titulo == "Estadísticas Base")
        {
            var filaCurvas = FilaHorizontal(
                Opcion("basestats.expCurveSpeed"),
                Opcion("basestats.expCurveTarget", 1));
            if (filaCurvas is not null) stack.Children.Add(filaCurvas);

            Agregar(stack, Opcion("basestats.mod", 3), 12);
            AgregarChecks(stack, new[]
            {
                "basestats.followEvolutions",
                "basestats.followMegaEvolutions",
                "basestats.randomizeAdded",
                "basestats.standardizeExpCurves",
                "basestats.updateGeneration",
            }, 2, 12);
            Agregar(stack, Opcion("basestats.updateGenerationTo"), 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Evoluciones: modo a ancho completo, casillas y sliders después ──
        if (prefijoReal.Equals("evolutions", StringComparison.OrdinalIgnoreCase) && titulo == "Evoluciones")
        {
            Agregar(stack, Opcion("evolutions.mod", 3));
            AgregarChecks(stack, new[]
            {
                "evolutions.allowAlternateForms",
                "evolutions.changeImpossible",
                "evolutions.forceChange",
                "evolutions.makeEasier",
                "evolutions.maxThreeStages",
                "evolutions.removeTimeBased",
                "evolutions.sameTyping",
                "evolutions.similarStrength",
            }, 2, 12);

            var filaSliders = FilaHorizontal(
                Opcion("evolutions.maxLevel"),
                Opcion("evolutions.tradeLevel"));
            if (filaSliders is not null)
            {
                filaSliders.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(filaSliders);
            }

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Habilidades: mismo tratamiento que el modo de evolución ──
        if (prefijoReal.Equals("abilities", StringComparison.OrdinalIgnoreCase) && titulo == "Habilidades")
        {
            Agregar(stack, Opcion("abilities.mod", 2));
            AgregarChecks(stack, checks.Select(o => o.clave), 2, 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Tipos: una opción por fila, ancho completo ──
        if (prefijoReal.Equals("types", StringComparison.OrdinalIgnoreCase) && titulo == "Tipos")
        {
            Agregar(stack, Opcion("types.mod", 1));
            AgregarChecks(stack, new[] { "types.forceDualTypes" }, 1, 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Estáticos: arriba casillas + sliders; debajo, los 4 modos a ancho completo ──
        if (prefijoReal.Equals("statics", StringComparison.OrdinalIgnoreCase) && titulo == "Estáticos")
        {
            var gridEstaticos = new Grid();
            gridEstaticos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridEstaticos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var colChecks = new StackPanel { Margin = new Thickness(0, 0, SeparacionOpcionesRando, 0) };
            AgregarChecks(colChecks, checks.Select(o => o.clave), 2, 0);

            var colDer = new StackPanel { Margin = new Thickness(SeparacionOpcionesRando, 0, 0, 0) };
            var filaSlidersEstaticos = FilaHorizontal(
                Opcion("statics.bstMargin"),
                Opcion("statics.levelModifierValue"));
            if (filaSlidersEstaticos is not null) colDer.Children.Add(filaSlidersEstaticos);

            Grid.SetColumn(colChecks, 0);
            Grid.SetColumn(colDer, 1);
            gridEstaticos.Children.Add(colChecks);
            gridEstaticos.Children.Add(colDer);
            stack.Children.Add(gridEstaticos);

            // Los 4 modos de estáticos ocupan todo el ancho de la tarjeta, en una sola fila.
            Agregar(stack, Opcion("statics.mod", 4), 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Iniciales: modo 2×2 primero, personalizados en horizontal y casillas al final ──
        if (prefijoReal.Equals("starters", StringComparison.OrdinalIgnoreCase) && titulo == "Iniciales")
        {
            Agregar(stack, Opcion("starters.mod", 2));

            var personalizados = new[] { "starters.custom1", "starters.custom2", "starters.custom3" }
                .Select(Opcion)
                .Where(e => e is not null)
                .Cast<FrameworkElement>()
                .ToList();
            if (personalizados.Count > 0)
            {
                var gridCustom = ColocarEnGrid(personalizados, personalizados.Count);
                gridCustom.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(gridCustom);
            }

            AgregarChecks(stack, checks.Select(o => o.clave), 2, 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Intercambios: tres modos en una fila ──
        if (prefijoReal.Equals("trades", StringComparison.OrdinalIgnoreCase) && titulo == "Intercambios")
        {
            Agregar(stack, Opcion("trades.mod", 3));
            AgregarChecks(stack, checks.Select(o => o.clave), 2, 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Movesets: modo 2×2, sliders y casillas ──
        if (prefijoReal.Equals("movesets", StringComparison.OrdinalIgnoreCase) && titulo == "Movesets")
        {
            Agregar(stack, Opcion("movesets.mod", 2));

            var filaSlidersMoves = FilaHorizontal(
                Opcion("movesets.forceGoodDamagingPercent"),
                Opcion("movesets.guaranteedLevel1Min"));
            if (filaSlidersMoves is not null)
            {
                filaSlidersMoves.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(filaSlidersMoves);
            }

            AgregarChecks(stack, checks.Select(o => o.clave), 2, 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── MT/MO: primero movimientos de MT, después compatibilidad, slider y casillas ──
        if (titulo == "MT/MO")
        {
            Agregar(stack, Opcion("tmmoves.mod", 2));
            Agregar(stack, Opcion("tmcompat.mod", 4), 12);
            Agregar(stack, Opcion("tmmoves.forceGoodDamagingPercent"), 12);
            AgregarChecks(stack, checks.Select(o => o.clave), 2, 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Tutores: compatibilidad 2×2, movimientos de tutores, slider y casillas ──
        if (titulo == "Tutores")
        {
            Agregar(stack, Opcion("tutorcompat.mod", 2));
            Agregar(stack, Opcion("tutormoves.mod", 2), 12);
            Agregar(stack, Opcion("tutormoves.forceGoodDamagingPercent"), 12);
            AgregarChecks(stack, checks.Select(o => o.clave), 2, 12);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Modo de Entrenadores: 2 filas × 3 columnas ──
        if (prefijoReal.Equals("trainers", StringComparison.OrdinalIgnoreCase) && titulo == "Modo de Entrenadores")
        {
            Agregar(stack, Opcion("trainers.mod", 3));

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Opciones Generales de entrenadores: bloques Jefes / Importantes / Normales ──
        if (prefijoReal.Equals("trainers", StringComparison.OrdinalIgnoreCase) && titulo == "Opciones Generales")
        {
            var usadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Tres columnas Jefes / Importantes / Normales. Se usan filas compartidas para que
            // cada control quede alineado con su equivalente de las otras columnas.
            var gridColumnas = new Grid();
            for (var c = 0; c < 3; c++)
                gridColumnas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var r = 0; r < 5; r++)
                gridColumnas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var columnasEntrenador = new (string Titulo, string Extra, string Cantidad, string Doble, string Objetos)[]
            {
                ("Jefes", "trainers.additionalBoss", "trainers.additionalBossCount", "trainers.doubleBattleChanceBoss", "trainers.heldItemsBoss"),
                ("Importantes", "trainers.additionalImportant", "trainers.additionalImportantCount", "trainers.doubleBattleChanceImportant", "trainers.heldItemsImportant"),
                ("Normales", "trainers.additionalRegular", "trainers.additionalRegularCount", "trainers.doubleBattleChanceRegular", "trainers.heldItemsRegular"),
            };

            for (var c = 0; c < columnasEntrenador.Length; c++)
            {
                var def = columnasEntrenador[c];
                var margen = new Thickness(SeparacionOpcionesRando, 0, SeparacionOpcionesRando, 0);

                var tituloColumna = new TextBlock
                {
                    Text = def.Titulo,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Recurso("ColorTexto", Brushes.White),
                    Margin = new Thickness(margen.Left, 0, margen.Right, 6),
                };
                Grid.SetRow(tituloColumna, 0);
                Grid.SetColumn(tituloColumna, c);
                gridColumnas.Children.Add(tituloColumna);

                usadas.Add(def.Extra);
                usadas.Add(def.Cantidad);
                usadas.Add(def.Doble);
                usadas.Add(def.Objetos);

                var extra = Opcion(def.Extra);
                if (extra is not null)
                {
                    extra.Margin = new Thickness(margen.Left, 0, margen.Right, 0);
                    Grid.SetRow(extra, 1);
                    Grid.SetColumn(extra, c);
                    gridColumnas.Children.Add(extra);
                }

                var cantidad = Opcion(def.Cantidad);
                if (cantidad is not null)
                {
                    cantidad.Margin = new Thickness(margen.Left, 4, margen.Right, 0);
                    Grid.SetRow(cantidad, 2);
                    Grid.SetColumn(cantidad, c);
                    gridColumnas.Children.Add(cantidad);
                }

                var doble = Opcion(def.Doble);
                if (doble is not null)
                {
                    doble.Margin = new Thickness(margen.Left, 8, margen.Right, 0);
                    Grid.SetRow(doble, 3);
                    Grid.SetColumn(doble, c);
                    gridColumnas.Children.Add(doble);
                }

                var objetos = Opcion(def.Objetos);
                if (objetos is not null)
                {
                    objetos.Margin = new Thickness(margen.Left, 8, margen.Right, 0);
                    Grid.SetRow(objetos, 4);
                    Grid.SetColumn(objetos, c);
                    gridColumnas.Children.Add(objetos);
                }
            }

            stack.Children.Add(gridColumnas);

            // Opciones globales de objetos. Son claves únicas del motor (no por tipo de
            // entrenador), así que se muestran una sola vez en una fila de 3 columnas.
            // TODO: el motor no expone variantes por Jefes/Importantes/Normales; si se quieren
            // replicar dentro de cada columna haría falta ampliar el esquema de Settings.
            var opcionesObjetos = new[]
            {
                "trainers.heldItemsConsumableOnly",
                "trainers.heldItemsSensible",
                "trainers.heldItemsHighestLevelOnly",
            };
            foreach (var clave in opcionesObjetos) usadas.Add(clave);
            AgregarChecks(stack, opcionesObjetos, 3, 12);

            // Forzar evolución completa y modificador de nivel, cada uno con su valor debajo.
            var filaNiveles = new Grid();
            filaNiveles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filaNiveles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var colForce = new StackPanel { Margin = new Thickness(0, 0, SeparacionOpcionesRando, 0) };
            usadas.Add("trainers.forceFullyEvolved");
            usadas.Add("trainers.forceFullyEvolvedLevel");
            Agregar(colForce, Opcion("trainers.forceFullyEvolved"));
            Agregar(colForce, Opcion("trainers.forceFullyEvolvedLevel"), 6);

            var colMod = new StackPanel { Margin = new Thickness(SeparacionOpcionesRando, 0, 0, 0) };
            usadas.Add("trainers.levelModified");
            usadas.Add("trainers.levelModifier");
            Agregar(colMod, Opcion("trainers.levelModified"));
            Agregar(colMod, Opcion("trainers.levelModifier"), 6);

            Grid.SetColumn(colForce, 0);
            Grid.SetColumn(colMod, 1);
            filaNiveles.Children.Add(colForce);
            filaNiveles.Children.Add(colMod);
            filaNiveles.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(filaNiveles);

            // Buenos ataques + probabilidad shiny en la misma fila.
            usadas.Add("trainers.movesetForceGoodDamagingPercent");
            usadas.Add("trainers.shinyChanceOneIn");
            var filaBuenos = FilaHorizontal(
                Opcion("trainers.movesetForceGoodDamagingPercent"),
                Opcion("trainers.shinyChanceOneIn"));
            if (filaBuenos is not null)
            {
                filaBuenos.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(filaBuenos);
            }

            // Resto de casillas y campos numéricos que no tengan bloque propio.
            var checksRestantes = checks
                .Select(o => o.clave)
                .Where(k => !usadas.Contains(k))
                .ToList();
            foreach (var clave in checksRestantes) usadas.Add(clave);
            AgregarChecks(stack, checksRestantes, 3, 12);

            var numerosRestantes = principales
                .Select(o => o.clave)
                .Where(k => !usadas.Contains(k))
                .ToList();
            if (numerosRestantes.Count > 0)
            {
                var elementosNumeros = numerosRestantes
                    .Where(valores.ContainsKey)
                    .Select(k => CrearFilaOpcion(k, valores[k]))
                    .ToList();
                var gridNumeros = ColocarEnGrid(elementosNumeros, 3);
                gridNumeros.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(gridNumeros);
            }

            tarjeta.Child = stack;
            return tarjeta;
        }

        var esTarjetaEstrecha = prefijoReal.ToLowerInvariant() is "fielditems" or "shops" or "pickup";
        var esTarjetaAncha = titulo is "Varios" or "Opciones Generales" or "Salvajes" or "Tótems" or "Estáticos" or "Modo de Entrenadores";

        // ── Bloque especial: Tótems → 3 columnas (Modo tótem, Modo aliado, Modo aura) ──
        if (prefijoReal.Equals("totems", StringComparison.OrdinalIgnoreCase) && titulo == "Tótems")
        {
            var grupoPrin = new List<(string clave, string valor)>();
            var grupoAliado = new List<(string clave, string valor)>();
            var grupoAura = new List<(string clave, string valor)>();
            var restoTotem = new List<(string clave, string valor)>();
            foreach (var o in filtradas)
            {
                if (o.clave.Contains(".allyMod", StringComparison.OrdinalIgnoreCase)) grupoAliado.Add(o);
                else if (o.clave.Contains(".auraMod", StringComparison.OrdinalIgnoreCase)) grupoAura.Add(o);
                else if (esBool(o) || esSlider(o)) restoTotem.Add(o);
                else grupoPrin.Add(o);
            }

            var gridTotem = new Grid();
            for (var ci = 0; ci < 3; ci++)
                gridTotem.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridTotem.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var margenTotem = new Thickness(SeparacionOpcionesRando, 0, SeparacionOpcionesRando, 0);

            // Columna 0: Modo tótem (mod principal)
            var colModStack = new StackPanel { Margin = margenTotem };
            foreach (var o in grupoPrin)
                colModStack.Children.Add(CrearFilaOpcion(o.clave, o.valor, 1));

            // Columna 1: Modo aliado
            var colAliadoStack = new StackPanel { Margin = margenTotem };
            foreach (var o in grupoAliado)
                colAliadoStack.Children.Add(CrearFilaOpcion(o.clave, o.valor, 1));

            // Columna 2: Modo aura
            var colAuraStack = new StackPanel { Margin = margenTotem };
            foreach (var o in grupoAura)
                colAuraStack.Children.Add(CrearFilaOpcion(o.clave, o.valor, 1));

            Grid.SetColumn(colModStack, 0);
            Grid.SetColumn(colAliadoStack, 1);
            Grid.SetColumn(colAuraStack, 2);
            gridTotem.Children.Add(colModStack);
            gridTotem.Children.Add(colAliadoStack);
            gridTotem.Children.Add(colAuraStack);
            stack.Children.Add(gridTotem);

            // Fila horizontal: casillas y slider en la misma línea.
            if (restoTotem.Count > 0)
            {
                var checksTotem = restoTotem.Where(esBool).ToList();
                var slidersTotem = restoTotem.Where(esSlider).ToList();

                if (checksTotem.Count > 0 && slidersTotem.Count > 0)
                {
                    var filaTotem = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                    filaTotem.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                    filaTotem.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

                    var gChecks = ColocarEnGrid(
                        checksTotem.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(),
                        Math.Max(1, checksTotem.Count));
                    gChecks.Margin = new Thickness(0, 0, SeparacionOpcionesRando, 0);

                    var gSliders = ColocarEnGrid(
                        slidersTotem.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(),
                        1);
                    gSliders.Margin = new Thickness(SeparacionOpcionesRando, 0, 0, 0);

                    Grid.SetColumn(gChecks, 0);
                    Grid.SetColumn(gSliders, 1);
                    filaTotem.Children.Add(gChecks);
                    filaTotem.Children.Add(gSliders);
                    stack.Children.Add(filaTotem);
                }
                else
                {
                    if (checksTotem.Count > 0)
                    {
                        var gChecks = ColocarEnGrid(checksTotem.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(), Math.Max(1, checksTotem.Count));
                        gChecks.Margin = new Thickness(0, 10, 0, 0);
                        stack.Children.Add(gChecks);
                    }
                    if (slidersTotem.Count > 0)
                    {
                        var gSliders = ColocarEnGrid(slidersTotem.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(), 1);
                        gSliders.Margin = new Thickness(0, 10, 0, 0);
                        stack.Children.Add(gSliders);
                    }
                }
            }

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Bloque especial: Salvajes → mitad izquierda / mitad derecha ──
        if (prefijoReal.Equals("wild", StringComparison.OrdinalIgnoreCase) && titulo == "Salvajes")
        {
            var modEncuentros = filtradas.Where(o => o.clave.Equals("wild.mod", StringComparison.OrdinalIgnoreCase)).ToList();
            var reglaAdicional = filtradas.Where(o => o.clave.Equals("wild.additionalRule", StringComparison.OrdinalIgnoreCase)).ToList();
            var nivelCaptura = filtradas.Where(o => o.clave.Equals("wild.minimumCatchRateLevel", StringComparison.OrdinalIgnoreCase)).ToList();
            var ratioShiny = filtradas.Where(o => o.clave.Equals("wild.shinyChanceOneIn", StringComparison.OrdinalIgnoreCase)).ToList();
            var slidersMod = filtradas.Where(esSlider).ToList();
            var checksWild = filtradas.Where(esBool).ToList();
            var otrosPrin = filtradas.Where(o => !esBool(o) && !esSlider(o)
                && !modEncuentros.Contains(o) && !reglaAdicional.Contains(o)
                && !nivelCaptura.Contains(o) && !ratioShiny.Contains(o)).ToList();

            var gridSalvajes = new Grid();
            gridSalvajes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridSalvajes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridSalvajes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Columna izquierda: Modo de encuentros y Regla adicional (2×2 cada uno).
            // «CrearFilaOpcion» ya pinta el título de la opción; no se añade otro encima.
            var izqStack = new StackPanel { Margin = new Thickness(0, 0, SeparacionOpcionesRando, 0) };
            if (modEncuentros.Count > 0)
            {
                var opcionMod = Opcion("wild.mod", 2);
                if (opcionMod is not null) izqStack.Children.Add(opcionMod);
            }
            if (reglaAdicional.Count > 0)
            {
                var opcionRegla = Opcion("wild.additionalRule", 2);
                if (opcionRegla is not null) Agregar(izqStack, opcionRegla, 8);
            }
            if (otrosPrin.Count > 0)
                izqStack.Children.Add(ColocarEnGrid(otrosPrin.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(), 2));

            // Columna derecha: Nivel captura + Ratio shiny, slider y casillas.
            var derStack = new StackPanel { Margin = new Thickness(SeparacionOpcionesRando, 0, 0, 0) };
            var panelHorizDer = new Grid();
            panelHorizDer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panelHorizDer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panelHorizDer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (nivelCaptura.Count > 0)
            {
                var panelNivel = CrearComboNivelCaptura(nivelCaptura[0].clave, nivelCaptura[0].valor);
                panelNivel.Margin = new Thickness(0, 0, SeparacionOpcionesRando, 0);
                Grid.SetColumn(panelNivel, 0);
                panelHorizDer.Children.Add(panelNivel);
            }
            if (ratioShiny.Count > 0)
            {
                var panelShiny = CrearFilaOpcion(ratioShiny[0].clave, ratioShiny[0].valor);
                panelShiny.Margin = new Thickness(SeparacionOpcionesRando, 8, 0, 8);
                Grid.SetColumn(panelShiny, 1);
                panelHorizDer.Children.Add(panelShiny);
            }
            if (panelHorizDer.Children.Count > 0)
                derStack.Children.Add(panelHorizDer);

            if (slidersMod.Count > 0)
            {
                var gSliders = ColocarEnGrid(slidersMod.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(), 1);
                gSliders.Margin = new Thickness(0, 10, 0, 0);
                derStack.Children.Add(gSliders);
            }
            if (checksWild.Count > 0)
            {
                var gChecks = ColocarEnGrid(checksWild.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(), 2);
                gChecks.Margin = new Thickness(0, 10, 0, 0);
                derStack.Children.Add(gChecks);
            }

            Grid.SetColumn(izqStack, 0);
            Grid.SetColumn(derStack, 1);
            gridSalvajes.Children.Add(izqStack);
            gridSalvajes.Children.Add(derStack);
            stack.Children.Add(gridSalvajes);

            tarjeta.Child = stack;
            return tarjeta;
        }

        // ── Layout general ──
        if (principales.Count > 0)
        {
            int colsPrin;
            if (esTarjetaEstrecha)
                colsPrin = 1;
            else if (principales.Count == 1)
                colsPrin = 1;
            else
                colsPrin = ColumnasRadioParaSubseccion(titulo, prefijoReal);

            // En la tarjeta estrecha de Objetos de Campo las 4 etiquetas no caben en una
            // sola fila: se apilan a ancho completo (4 filas × 1 columna).
            var columnasRadios = prefijoReal.Equals("fielditems", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            stack.Children.Add(ColocarEnGrid(
                principales.Select(o => CrearFilaOpcion(o.clave, o.valor, columnasRadios)).ToList(),
                colsPrin));
        }

        if (checks.Count > 0)
        {
            var colsCheck = ColumnasCheckParaSubseccion(titulo, prefijoReal, esTarjetaEstrecha, esTarjetaAncha);
            var gridChecks = ColocarEnGrid(checks.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(), colsCheck);
            gridChecks.Margin = new Thickness(0, principales.Count > 0 ? 10 : 0, 0, 0);
            stack.Children.Add(gridChecks);
        }

        if (sliders.Count > 0)
        {
            // Sliders siempre a ancho completo (1 columna) para mayor legibilidad
            var gridSliders = ColocarEnGrid(sliders.Select(o => CrearFilaOpcion(o.clave, o.valor)).ToList(), 1);
            gridSliders.Margin = new Thickness(0, (principales.Count > 0 || checks.Count > 0) ? 12 : 0, 0, 0);
            stack.Children.Add(gridSliders);
        }

        tarjeta.Child = stack;
        return tarjeta;
    }

    /// <summary>Crea un ComboBox de Nivel Captura (1-5) para wild.minimumCatchRateLevel.</summary>
    private FrameworkElement CrearComboNivelCaptura(string clave, string valor)
    {
        var fila = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 8, 0, 8) };
        var etiqueta = new TextBlock
        {
            Text = TituloClave(clave),
            FontSize = 12,
            Foreground = Recurso("ColorTextoSuave", Brushes.Gray),
            Margin = new Thickness(0, 0, 0, 6),
        };
        fila.Children.Add(etiqueta);

        var combo = new ComboBox
        {
            MaxDropDownHeight = 200,
            MinHeight = AltoControlRando,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        for (var i = 1; i <= 5; i++)
            combo.Items.Add(new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() });

        if (int.TryParse(valor, out var actual) && actual >= 1 && actual <= 5)
            combo.SelectedIndex = actual - 1;
        else
            combo.SelectedIndex = 0;

        combo.SelectionChanged += (_, _) => Guardar(clave, ValorCombo(combo));
        _controles[clave] = combo;
        fila.Children.Add(combo);
        return fila;
    }

    private static Grid ColocarEnGrid(List<FrameworkElement> elementos, int columnas)
    {
        var grid = new Grid();
        for (var c = 0; c < columnas; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fila = 0;
        var columna = 0;
        foreach (var el in elementos)
        {
            if (columna == 0)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(el, fila);
            Grid.SetColumn(el, columna);
            // Mismo margen en TODAS las celdas: así todas las opciones miden lo mismo y
            // la fila ocupa el ancho disponible salvo el margen uniforme.
            var margen = columnas > 1 ? SeparacionOpcionesRando : 0;
            el.Margin = new Thickness(margen, el.Margin.Top, margen, el.Margin.Bottom);
            grid.Children.Add(el);

            columna++;
            if (columna >= columnas)
            {
                columna = 0;
                fila++;
            }
        }
        return grid;
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

    /// <summary>Crea una fila de opción individual (etiqueta + control), al estilo Rediseño.
    /// <paramref name="columnasRadios"/> permite repartir las opciones de un mismo modo en
    /// varias filas/columnas (0 = todas en una sola fila).</summary>
    private FrameworkElement CrearFilaOpcion(string clave, string valor, int columnasRadios = 0)
    {
        // trainers.percentageLevelModifier no existe en el motor (la casilla real es trainers.levelModified).
        if (clave.Equals("trainers.percentageLevelModifier", StringComparison.OrdinalIgnoreCase))
            return new Grid { Height = 0 };

        // ── Casilla de verificación circular sin etiqueta duplicada encima ──
        if (valor.Equals("true", StringComparison.OrdinalIgnoreCase) || valor.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            var check = new CheckBox
            {
                Style = (Style)Resources["CheckCircularRando"],
                Content = TituloClave(clave),
                IsChecked = valor.Equals("true", StringComparison.OrdinalIgnoreCase),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 12, 6),
                ToolTip = DescripcionesRandomizer.Para(clave, valor),
            };
            ToolTipService.SetShowDuration(check, 30000);
            ToolTipService.SetInitialShowDelay(check, 350);
            check.Checked += (_, _) => Guardar(clave, "true");
            check.Unchecked += (_, _) => Guardar(clave, "false");
            return check;
        }

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

        // ── Opciones mutuamente excluyentes (.mod y claves con valores del motor): radio pills ──
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
            // Grid uniforme: cada opción ocupa el mismo ancho, sin amontonarse.
            // Si se piden menos columnas que opciones, se reparten en varias filas (2×2, 2×3…).
            var columnas = columnasRadios > 0 ? Math.Min(columnasRadios, opcionesRadio.Length) : opcionesRadio.Length;
            var gridRadios = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            for (var c = 0; c < columnas; c++)
                gridRadios.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var filaIdx = 0;
            var colIdx = 0;
            foreach (var opcion in opcionesRadio)
            {
                if (colIdx == 0)
                    gridRadios.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var radio = new RadioButton
                {
                    Style = (Style)Resources["RadioPillRando"],
                    GroupName = clave,
                    Content = new TextBlock
                    {
                        Text = DescripcionesRandomizer.ValorVisible(opcion),
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                    },
                    IsChecked = string.Equals(opcion, valor, StringComparison.OrdinalIgnoreCase),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    MinHeight = AltoControlRando,
                    // En grupos de 4 (media tarjeta) el texto necesita algo más de aire.
                    FontSize = columnas >= 4 ? 11.5 : 12,
                    ToolTip = DescripcionesRandomizer.ParaValor(clave, opcion),
                    // Margen idéntico en todas las columnas: mismas anchuras y misma separación.
                    Margin = new Thickness(columnas > 1 ? 4 : 0, 0, columnas > 1 ? 4 : 0, 4),
                };
                ToolTipService.SetShowDuration(radio, 30000);
                ToolTipService.SetInitialShowDelay(radio, 350);
                var op = opcion;
                radio.Checked += (_, _) => Guardar(clave, op);
                Grid.SetRow(radio, filaIdx);
                Grid.SetColumn(radio, colIdx);
                gridRadios.Children.Add(radio);

                colIdx++;
                if (colIdx >= columnas)
                {
                    colIdx = 0;
                    filaIdx++;
                }
            }
            fila.Children.Add(gridRadios);
            return fila;
        }

        // ── Enumerados conocidos: desplegable compacto en español ──
        if (Enumerados.TryGetValue(clave, out var opcionesEnum))
        {
            var combo = new ComboBox
            {
                MaxDropDownHeight = 300,
                MinWidth = 0,
                MinHeight = AltoControlRando,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            foreach (var opcion in opcionesEnum)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = DescripcionesRandomizer.ValorVisible(opcion),
                    Tag = opcion,
                    ToolTip = DescripcionesRandomizer.ParaValor(clave, opcion),
                });
            }
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), valor, StringComparison.OrdinalIgnoreCase));
            if (combo.SelectedItem is null)
            {
                var propio = new ComboBoxItem { Content = DescripcionesRandomizer.ValorVisible(valor), Tag = valor };
                combo.Items.Add(propio);
                combo.SelectedItem = propio;
            }
            combo.SelectionChanged += (_, _) => Guardar(clave, ValorCombo(combo));
            fila.Children.Add(combo);
            _controles[clave] = combo;
            return fila;
        }

        // ── Ratio shiny: el motor guarda «1 entre N», así que se escribe el DIVISOR ──
        if (clave.EndsWith(".shinyChanceOneIn", StringComparison.OrdinalIgnoreCase))
        {
            var unoDe = int.TryParse(valor, out var n) ? Math.Max(0, n) : 0;
            var cajaRatio = new TextBox
            {
                Style = (Style)Resources["InputRando"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = AltoControlRando,
                Text = unoDe > 0 ? unoDe.ToString(CultureInfo.InvariantCulture) : "0",
                ToolTip = "Escribe el divisor: 4096 significa 1 entre 4096. 0 = desactivado.",
            };
            var detalle = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = Recurso("ColorTextoSuave", Brushes.Gray),
                Text = unoDe > 0
                    ? $"1 de cada {unoDe} (≈ {100.0 / unoDe:0.####} %)"
                    : "Desactivado",
            };
            void Aplicar()
            {
                if (!int.TryParse(cajaRatio.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nuevoN) ||
                    nuevoN <= 0)
                {
                    Guardar(clave, "0");
                    detalle.Text = "Desactivado";
                    return;
                }
                Guardar(clave, nuevoN.ToString(CultureInfo.InvariantCulture));
                detalle.Text = $"1 de cada {nuevoN} (≈ {100.0 / nuevoN:0.####} %)";
            }
            cajaRatio.LostFocus += (_, _) => Aplicar();
            cajaRatio.KeyDown += (_, e) => { if (e.Key == Key.Enter) Aplicar(); };
            var panelRatio = new StackPanel();
            panelRatio.Children.Add(cajaRatio);
            panelRatio.Children.Add(detalle);
            fila.Children.Add(panelRatio);
            return fila;
        }

        // ── Rangos numéricos: etiqueta izquierda + valor derecha sobre slider rojo ──
        if (Rangos.TryGetValue(clave, out var rango)
                 && double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numero))
        {
            var inicial = Math.Clamp(Math.Round(numero), rango.Min, rango.Max);
            var slider = new Slider
            {
                Style = (Style)Resources["SliderRando"],
                Minimum = rango.Min,
                Maximum = rango.Max,
                Value = inicial,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                IsMoveToPointEnabled = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ToolTip = DescripcionesRandomizer.Para(clave, valor),
            };
            string TextoValor(int v)
            {
                if (v == 0 && clave.Equals("evolutions.maxLevel", StringComparison.OrdinalIgnoreCase))
                    return "Sin límite";
                if (v == 0 && (clave.Equals("basestats.updateGenerationTo", StringComparison.OrdinalIgnoreCase) ||
                               clave.Equals("movedata.updateGenerationTo", StringComparison.OrdinalIgnoreCase)))
                    return "Gen actual";
                return $"{rango.Prefijo}{v}{rango.Sufijo}";
            }
            var valorTexto = new TextBlock
            {
                Text = TextoValor((int)inicial),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = Recurso("ColorTexto", Brushes.White),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            };
            slider.ValueChanged += (_, e) =>
            {
                var v = (int)Math.Clamp(Math.Round(e.NewValue), rango.Min, rango.Max);
                valorTexto.Text = TextoValor(v);
                Guardar(clave, v.ToString(CultureInfo.InvariantCulture));
            };

            var headerSlider = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerSlider.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerSlider.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.Children.Clear();
            etiqueta.Margin = new Thickness(0);
            etiqueta.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(etiqueta, 0);
            Grid.SetColumn(valorTexto, 1);
            headerSlider.Children.Add(etiqueta);
            headerSlider.Children.Add(valorTexto);

            fila.Children.Add(headerSlider);
            fila.Children.Add(slider);
            _controles[clave] = slider;
            return fila;
        }

        // ── Número entero: campo oscuro compacto ──
        if (int.TryParse(valor, out _))
        {
            var caja = new TextBox
            {
                Style = (Style)Resources["InputRando"],
                Text = valor,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            caja.LostFocus += (_, _) => Guardar(clave, caja.Text.Trim());
            caja.KeyDown += (_, e) => { if (e.Key == Key.Enter) Guardar(clave, caja.Text.Trim()); };
            fila.Children.Add(caja);
            _controles[clave] = caja;
            return fila;
        }

        // ── Texto libre ──
        var cajaTexto = new TextBox
        {
            Style = (Style)Resources["InputRando"],
            Text = valor,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        cajaTexto.LostFocus += (_, _) => Guardar(clave, cajaTexto.Text.Trim());
        cajaTexto.KeyDown += (_, e) => { if (e.Key == Key.Enter) Guardar(clave, cajaTexto.Text.Trim()); };
        fila.Children.Add(cajaTexto);
        _controles[clave] = cajaTexto;
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
                Slider slider => ((int)Math.Round(slider.Value)).ToString(CultureInfo.InvariantCulture),
                TextBox caja => caja.Text.Trim(),
                _ => "",
            };
            if (valor.Length > 0) _settings.Set(clave, valor);
        }
    }

    /// <summary>Texto seleccionado de un ComboBox cuyos items son ComboBoxItem.</summary>
    private static string ValorCombo(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
           ?? (combo.SelectedItem as ComboBoxItem)?.Content?.ToString()
           ?? combo.SelectedItem?.ToString()
           ?? "";

    private void Guardar(string clave, string valor)
    {
        if (valor.Length > 0) _settings.Set(clave, valor);
    }

    private void ResetCategoria_Click(object sender, RoutedEventArgs e)
    {
        if (_prefijoActual.Length == 0) return;
        var defecto = new RandomizerSettings();
        var btnActivo = new[] { BtnTabCaracteristicas, BtnTabIniciales, BtnTabMovimientos,
                                BtnTabSalvajes, BtnTabEntrenadores, BtnTabObjetos, BtnTabMiscelanea }
            .FirstOrDefault(b => b.Tag is string s && s == "activo");
        if (btnActivo is null) return;
        var grupo = CategoriasAgrupadas.FirstOrDefault(g => g.Titulo == btnActivo.Content.ToString());
        if (grupo is null) return;
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
            // // TxtEstado.Text = "Preset guardado: " + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex) { Log("Error al guardar preset: " + ex.Message); }
    }

    /// <summary>Guarda los ajustes actuales como preset propio.</summary>
    private async void GuardarPreset_Click(object sender, RoutedEventArgs e)
    {
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
            Log("Preset guardado en " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { Log("Error al guardar preset: " + ex.Message); }
    }

    /// <summary>Randomiza la ROM con los ajustes actuales (guarda preset previo si se pide).</summary>
    private async void BtnRandomizar_Click(object sender, RoutedEventArgs e)
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
            BtnGuardarPreset.IsEnabled = true;
            return;
        }
        var salida = dlgSalida.FileName;

        BtnRandomizar.IsEnabled = false;
        BtnGuardarPreset.IsEnabled = false;
        // // TxtEstado.Text = "Randomizando… (puede tardar varios minutos)";
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
                // TxtEstado.Text = "Randomizado completado";
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
                // TxtEstado.Text = "El randomizador falló: " + resultado.Motivo;
                if (!string.IsNullOrWhiteSpace(resultado.SalidaError)) Log(resultado.SalidaError.Trim());
            }
        }
        catch (Exception ex)
        {
            // TxtEstado.Text = "Error al randomizar: " + ex.Message;
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            BtnRandomizar.IsEnabled = true;
            BtnGuardarPreset.IsEnabled = true;
        }
    }

    // ─────────────────────────── utilidades ───────────────────────────

    private static string TituloClave(string clave)
    {
        var traducido = DescripcionesRandomizer.TituloDe(clave);
        return !string.Equals(traducido, clave, StringComparison.OrdinalIgnoreCase)
            ? traducido
            : TituloClaveLargo(clave);
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
        // TxtEstado.Text = $"[{DateTime.Now:HH:mm:ss}] {linea}";
    }
}