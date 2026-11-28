using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PKHeX.Core;
using PokeLobby.Emulator;
using PokeLobby.PokeLocke;
using PokeLobby.Randomizer;
using PokeLobby.Watcher;

namespace PokeLobby.LauncherUi;

public partial class MainWindow : Window, IComponentConnector
{
	private sealed record LockeVista(string Id, string Nombre, string Juego, string Codigo)
	{
		public override string ToString()
		{
			var codigoVisible = Codigo.Length > 0 ? Codigo : (Id.Length >= 8 ? Id.Substring(0, 8) : Id);
			return $"{Nombre} — {Juego} [{codigoVisible}]";
		}
	}

	private readonly Ajustes _ajustes = Ajustes.Cargar();

	private List<RomEntry> _biblioteca = new List<RomEntry>();
	private VinculosLocke _vinculos = new();
	private readonly System.Windows.Threading.DispatcherTimer _timerPendientes = new() { Interval = TimeSpan.FromSeconds(20) };

	private SaveWatcher? _watcher;

	private FirebaseRtdbClient? _firebase;

	private Dictionary<string, Locke> _lockesFirebase = new Dictionary<string, Locke>();

	private Locke? _lockeActivo;

	private string _lockeId = "";

	private string _playerTag = "";

	private CancellationTokenSource? _ctsLocke;

	private RamVivoService? _ramvivo;


	private ColaLauncherService? _cola;

	private bool _procesandoCola;

	private SettingsPanel? CtrlAjustes;

	private RomEntry? _romSeleccionada;
	private string _seccionActual = "inicio";
	private Point _swipeInicio;
	private bool _swipeActivo;

	public static string RutaLog => Path.Combine(Path.GetTempPath(), "PokeLockeLauncher.log");

	public MainWindow()
	{
		InitializeComponent();
		try
		{
			var iconUri = new Uri("pack://application:,,,/pokelocke.png", UriKind.Absolute);
			Icon = BitmapFrame.Create(iconUri);
		}
		catch
		{
			// El icono es opcional
		}
		var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
		Title = version is not null ? $"PokeLocke Launcher v{version.Major}.{version.Minor}.{version.Build}" : "PokeLocke Launcher";
		CtrlRandomizador.Inicializar(_ajustes, this);
		CtrlAjustes = new SettingsPanel(_ajustes);
		CtrlAjustes.CambiosAplicados += CtrlAjustes_CambiosAplicados;
		PanelAjustes.Children.Add(CtrlAjustes);
		TemaAcento.Apply(_ajustes.TemaIndice);
		TemaFondo.Apply(_ajustes.FondoIndice);
		TemaFondo.GuardarAudioVideo(_ajustes.VideoConSonido, _ajustes.VideoVolumen);
		if (!string.IsNullOrWhiteSpace(_ajustes.FondoRuta))
		{
			TemaFondo.AplicarImagen(_ajustes.FondoRuta);
		}
		SonidosUi.Activados = _ajustes.Sonidos;
		EmuladorSwitch.Cargar(_ajustes.SwitchRuta, _ajustes.SwitchArgumentos);
		Loaded += async (object _, RoutedEventArgs _) =>
		{
			await AlCargarAsync();
		};
		Closing += (object? _, CancelEventArgs _) =>
		{
			CerrarTodo();
		};
	}

	private async Task AlCargarAsync()
	{
		if (string.IsNullOrWhiteSpace(_ajustes.VendorDir))
		{
			_ajustes.VendorDir = Ajustes.BuscarVendorDir();
		}
		CargarBiblioteca();
		ActualizarPortadasInicio();
		ActualizarVideoFondo();
		AsegurarCarpetaDatos();
		SembrarCaratulasIncluidas();
		await ConectarFirebaseAsync();
		AvisarSavesConChecksumsInvalidos();
		// Sin bloquear la carga: solo avisa si hay versión nueva.
		_ = Actualizador.ComprobarAsync();
	}

	private void AvisarSavesConChecksumsInvalidos()
	{
		try
		{
			string path = Path.Combine(_ajustes.DatosDir, "huellas.json");
			if (!File.Exists(path))
			{
				return;
			}
			JsonObject jsonObject = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
			if (jsonObject == null)
			{
				return;
			}
			List<(string, string, string)> list = new List<(string, string, string)>();
			foreach (RomEntry item2 in _biblioteca)
			{
				string text;
				try
				{
					text = ResolverSavePath(item2);
				}
				catch
				{
					continue;
				}
				if (text == null || !File.Exists(text) || (string?)jsonObject[text.ToLowerInvariant()]?["RomMotivo"] != "launcher-aplicó")
				{
					continue;
				}
				try
				{
					SaveFile saveFile = SaveUtil.GetSaveFile(text);
					if (saveFile != null && !saveFile.ChecksumsValid)
					{
						list.Add((text, "checksums inválidos", saveFile.ChecksumInfo));
					}
				}
				catch
				{
				}
			}
			if (list.Count == 0)
			{
				return;
			}
			string value = string.Join("\n", list.Select(((string Ruta, string Motivo, string Info) c) => "• " + c.Ruta));
			MessageBoxResult messageBoxResult = MessageBox.Show(this, $"He detectado {list.Count} partida(s) que una versión anterior del launcher escribió con los checksums inválidos (el juego las trataría como dañadas):\n\n{value}\n\n" + "¿Quieres repararlas ahora? (se hace una copia .bak y se reescriben con checksums válidos)", "Reparar partidas con checksums inválidos", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
			if (messageBoxResult != MessageBoxResult.Yes)
			{
				return;
			}
			foreach (var item3 in list)
			{
				string item = item3.Item1;
				try
				{
					SaveFile saveFile2 = SaveUtil.GetSaveFile(item);
					if (saveFile2 != null)
					{
						File.Copy(item, $"{Path.GetFileNameWithoutExtension(item)}.bak-reparacion-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
						InyectorEquipo.GuardarSave(saveFile2, item);
						SaveFile saveFile3 = SaveUtil.GetSaveFile(item);
						Log((saveFile3 != null && saveFile3.ChecksumsValid) ? ("✔ Reparada: " + Path.GetFileName(item) + " (checksums válidos)") : ("✗ No se pudo reparar: " + Path.GetFileName(item)));
					}
				}
				catch (Exception ex)
				{
					Log("✗ Error reparando " + Path.GetFileName(item) + ": " + ex.Message);
				}
			}
		}
		catch (Exception ex2)
		{
			Log("Aviso de checksums: " + ex2.Message);
		}
	}

	private async Task ConectarFirebaseAsync()
	{
		if (string.IsNullOrWhiteSpace(_ajustes.FirebaseUrl))
		{
			// DotFirebase eliminado en el rediseño
			// DotFirebase.Fill = new SolidColorBrush(Colors.Gray);
			// TxtFirebaseEstado.Text = "Firebase: sin configurar (⚙ Ajustes)";
		if (!string.IsNullOrWhiteSpace(_ajustes.CuentaRefreshToken))
		{
			// Hay sesión guardada pero aún no se conoce el email (restaurando): no decir «sin cuenta».
			TxtCuenta.Text = "👤 Cuenta guardada";
			BtnCuenta.Content = "Cerrar sesión";
			return;
		}
			TxtCuenta.Text = "Sin cuenta";
			BtnCuenta.Content = "Iniciar sesión";
			return;
		}
		try
		{
			_firebase?.Dispose();
			_firebase = new FirebaseRtdbClient(_ajustes.FirebaseUrl, _ajustes.FirebaseApiKey, _ajustes.FirebaseAuthToken);
			_firebase.SesionRenovada += (SesionCuenta sesion) =>
			{
				if (!string.IsNullOrWhiteSpace(sesion.Email))
				{
					_ajustes.CuentaEmail = sesion.Email;
					_ajustes.CuentaUsuario = PokeLobby.PokeLocke.CuentaPokelocke.UsuarioDesdeEmail(sesion.Email);
				}
				_ajustes.CuentaRefreshToken = sesion.RefreshToken;
				_ajustes.Guardar();
				ActualizarBotonCuenta();
				ActualizarEstadoFirebase();
			};
			PrepararServicioCola();
			// DotFirebase eliminado en el rediseño
			// DotFirebase.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80));
			// TxtFirebaseEstado.Text = "Firebase ✓";
			if (!string.IsNullOrWhiteSpace(_ajustes.CuentaRefreshToken))
			{
				try
				{
					await _firebase.RestaurarSesionAsync(_ajustes.CuentaRefreshToken);
					await TrasIniciarSesionAsync();
					return;
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Log("La sesión guardada ya no es válida (" + ex2.Message + ") — inicia sesión de nuevo.");
					var errorSesion = ex2.Message ?? "";
					var errorDefinitivo = errorSesion.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
						|| errorSesion.Contains("USER", StringComparison.OrdinalIgnoreCase)
						|| errorSesion.Contains("INVALID", StringComparison.OrdinalIgnoreCase)
						|| errorSesion.Contains("401", StringComparison.Ordinal)
						|| errorSesion.Contains("400", StringComparison.Ordinal);
					if (!errorDefinitivo)
					{
						Log("No se pudo restaurar la sesión ahora (¿sin conexión?): se reintentará al reconectar.");
						ActualizarBotonCuenta();
						ActualizarEstadoFirebase();
						return;
					}
					_ajustes.CuentaRefreshToken = "";
					_ajustes.Guardar();
				}
			}
			ActualizarBotonCuenta();
			ActualizarEstadoFirebase();
		}
		catch (Exception ex3)
		{
			// DotFirebase eliminado en el rediseño
			// DotFirebase.Fill = new SolidColorBrush(Color.FromRgb(239, 83, 80));
			// TxtFirebaseEstado.Text = "Firebase: " + ex3.Message;
		}
	}

	private void AsegurarCarpetaDatos()
	{
		if (PermisosCarpeta.PuedeEscribir(_ajustes.DatosDir))
		{
			Log("Carpeta de datos: " + _ajustes.DatosDir + " (escribible)");
			return;
		}
		string datosDir = _ajustes.DatosDir;
		Log("✗ La carpeta de datos no es escribible: " + datosDir);
		string text = PermisosCarpeta.ElegirEscribible(PermisosCarpeta.CandidatasDeDatos(null, AppContext.BaseDirectory));
		if (text == null)
		{
			Log("✗ Ninguna carpeta candidata es escribible: revisa los permisos del sistema.");
			Estado("Sin carpeta de datos escribible", "#EF5B5B");
			return;
		}
		Log("→ Uso una carpeta de datos alternativa: " + text);
		try
		{
			if (!string.IsNullOrWhiteSpace(datosDir) && Directory.Exists(datosDir))
			{
				foreach (string nombre in new string[4] { "library.json", "huellas.json", "covers.json", "vinculos.json" })
				{
					string origen = Path.Combine(datosDir, nombre);
					string destino = Path.Combine(text, nombre);
					if (File.Exists(origen) && !File.Exists(destino))
						File.Copy(origen, destino, overwrite: false);
				}
				string coversOrigen = Path.Combine(datosDir, "covers");
				if (Directory.Exists(coversOrigen))
				{
					string coversDestino = Path.Combine(text, "covers");
					Directory.CreateDirectory(coversDestino);
					foreach (string archivo in Directory.GetFiles(coversOrigen))
					{
						string destino = Path.Combine(coversDestino, Path.GetFileName(archivo));
						if (!File.Exists(destino)) File.Copy(archivo, destino, overwrite: false);
					}
				}
			}
			_ajustes.DatosDir = text;
			try { _ajustes.Guardar(); } catch { }
			Log("✔ Carpeta de datos cambiada a: " + text + " (las ROMs NO se mueven)");
			Estado("Carpeta de datos movida a " + text, "#F0A24A");
			MessageBox.Show(this,
				"No tenía permiso para escribir en:\n" + datosDir +
				"\n\nHe pasado la carpeta de datos (biblioteca, huellas, carátulas) a:\n" + text +
				"\n\nTus ROMs y partidas NO se mueven: siguen donde estaban.\nPuedes cambiarla cuando quieras en ⚙ Ajustes.",
				"Carpeta de datos cambiada", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		catch (Exception ex)
		{
			Log("✗ No se pudo migrar la carpeta de datos: " + ex.Message);
		}
	}

	private void SembrarCaratulasIncluidas()
	{
		try
		{
			int num = Caratulas.Sembrar(_ajustes.DatosDir);
			if (num > 0)
			{
				Log("✔ Carátulas incluidas copiadas a " + Caratulas.CarpetaDe(_ajustes.DatosDir) + " (" + num + ")");
			}
		}
		catch (Exception ex)
		{
			Log("✗ No se pudieron copiar las carátulas incluidas: " + ex.Message);
		}
	}

	private async Task ActualizarPendientesAsync()
	{
		// PanelPendientes y TxtPendientes eliminados en el rediseño
		return;
	}

	private async void BtnAplicarPendientes_Click(object sender, RoutedEventArgs e)
	{
		await AplicarPendientesCoreAsync(conConfirmacion: false, RomParaElLocke(_lockeActivo?.Juego, _lockeId));
	}

	private void BtnCarameloraros_Click(object sender, RoutedEventArgs e) => DarObjetosBiblioteca("Rare Candy", "Carameloraros");

	private void BtnEscamasCorazon_Click(object sender, RoutedEventArgs e) => DarObjetosBiblioteca("Heart Scale", "Escamas Corazón");

	/// <summary>Botón de la biblioteca: añade objetos (caramelos, escamas…) a la partida de la ROM seleccionada.</summary>
	/// <summary>Biblioteca: vincula la ROM seleccionada con un locke de la app (se guarda en vinculos.json).</summary>
	private void BtnVincularLocke_Click(object sender, RoutedEventArgs e)
	{
		if (_romSeleccionada is not RomEntry rom)
		{
			Aviso("Selecciona una ROM en la biblioteca.");
			return;
		}
		if (_lockesFirebase.Count == 0)
		{
			Aviso("Inicia sesión y espera a que carguen tus lockes.");
			return;
		}
		var panel = new StackPanel();
		panel.Children.Add(new TextBlock { Text = "ROM: " + rom.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
		panel.Children.Add(new TextBlock
		{
			Text = "Elige el locke que se jugará con esta ROM. La cola de ese locke se aplicará siempre a esta partida,",
			TextWrapping = TextWrapping.Wrap,
			Foreground = (Brush)(TryFindResource("ColorTextoSuave") ?? Brushes.Gray),
			Margin = new Thickness(0, 0, 0, 6),
		});
		panel.Children.Add(new TextBlock
		{
			Text = "aunque tengas otra ROM seleccionada (evita «El locke es de X pero la partida es de B»).",
			TextWrapping = TextWrapping.Wrap,
			Foreground = (Brush)(TryFindResource("ColorTextoSuave") ?? Brushes.Gray),
			Margin = new Thickness(0, 0, 0, 10),
		});
		var combo = new ComboBox { Margin = new Thickness(0, 4, 0, 0), MaxDropDownHeight = 260 };
		var vistas = _lockesFirebase.Select(kv => new LockeVista(kv.Key, kv.Value.Nombre, kv.Value.Juego, kv.Value.Codigo)).ToList();
		foreach (var vista in vistas) combo.Items.Add(vista);
		var actual = vistas.FirstOrDefault(v => string.Equals(_vinculos.RomDe(v.Id), rom.RomPath, StringComparison.OrdinalIgnoreCase));
		if (actual is not null) combo.SelectedItem = actual;
		else if (vistas.Count == 1) combo.SelectedIndex = 0;
		panel.Children.Add(combo);
		var ventana = CrearDialogo(this, "Vincular locke ↔ ROM", panel, 340);
		var filaBotones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
		var btnOk = new Button { Content = "Vincular", Padding = new Thickness(16, 7, 16, 7) };
		var btnQuitar = new Button { Content = "Quitar vínculo", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
		var btnCancelar = new Button { Content = "Cancelar", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
		filaBotones.Children.Add(btnOk);
		filaBotones.Children.Add(btnQuitar);
		filaBotones.Children.Add(btnCancelar);
		panel.Children.Add(filaBotones);
		string? elegido = null;
		var quitar = false;
		btnOk.Click += (_, _) =>
		{
			if (combo.SelectedItem is not LockeVista vista)
			{
				Aviso("Elige un locke de la lista.");
				return;
			}
			elegido = vista.Id;
			ventana.DialogResult = true;
		};
		btnQuitar.Click += (_, _) => { quitar = true; ventana.DialogResult = true; };
		btnCancelar.Click += (_, _) => ventana.DialogResult = false;
		if (ventana.ShowDialog() != true) return;
		if (quitar)
		{
			_vinculos.QuitarPorRom(rom.RomPath);
			_vinculos.Guardar(_ajustes.DatosDir);
			Log("🔗 Vínculo quitado de «" + rom.Name + "»");
			Aviso("Vínculo eliminado. Esta ROM ya no está atada a ningún locke.");
			return;
		}
		if (elegido is null) return;
		_vinculos.Asignar(elegido, rom.RomPath);
		_vinculos.Guardar(_ajustes.DatosDir);
		var nombre = _lockesFirebase.TryGetValue(elegido, out var locke) ? locke.Nombre : elegido;
		Log($"🔗 «{rom.Name}» vinculada al locke «{nombre}»");
		Aviso($"Listo: la cola de «{nombre}» se aplicará a «{rom.Name}» (partida: {ResolverSavePath(rom) ?? "sin partida todavía"}).");
	}

	private void DarObjetosBiblioteca(string nombreIngles, string nombreVisible)
	{
		if (EmuladorAbierto())
		{
			Aviso("Cierra el emulador antes de añadir objetos (el emulador sobrescribe el .sav al salir).");
			return;
		}
		if (_romSeleccionada is not RomEntry rom)
		{
			Aviso("Selecciona una ROM en la biblioteca.");
			return;
		}
		var save = ResolverSavePath(rom);
		if (save is null)
		{
			Aviso("Esa ROM todavía no tiene partida guardada.");
			return;
		}
		try
		{
			var sav = SaveUtil.GetSaveFile(save) ?? throw new InvalidOperationException("no se pudo leer la partida");
			var cantidad = sav.Generation >= 6 ? 995 : 99;
			var puestos = EfectosCartas.DarObjetoPorNombre(sav, [nombreIngles], cantidad);
			InyectorEquipo.GuardarSave(sav, save);
			Log($"🎁 +{puestos} {nombreVisible} en «{rom.Name}»");
			Aviso(puestos > 0
				? $"Añadidos {puestos} {nombreVisible} a la mochila de «{rom.Name}»."
				: $"No se pudo añadir {nombreVisible}: este juego no tiene ese objeto en la mochila.");
		}
		catch (Exception ex)
		{
			Log(nombreVisible + ": " + ex.Message);
			Aviso("No se pudieron añadir: " + ex.Message);
		}
	}


	/// <summary>Publica el equipo real del .sav en myTeam para «Buildeo → Mi Equipo» de la app.</summary>
	/// <summary>Selector de Pokémon de Pokémon Añil: muestra equipo y cajas con sus datos.</summary>
	private int? DialogoPokemonAnil(string titulo, IReadOnlyList<(string Etiqueta, Pokemon Mon)> lista)
	{
		if (lista.Count == 0) { Aviso("La partida de Añil no tiene Pokémon."); return null; }
		var combo = new ComboBox { MaxDropDownHeight = 420 };
		foreach (var (etiqueta, mon) in lista)
		{
			var ivs = string.Join("/", mon.IVs);
			var evs = string.Join("/", mon.EVs);
			var movs = string.Join(", ", mon.Moves.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name));
			combo.Items.Add($"{etiqueta} · {mon.Nature} · {mon.Ability} · IV {ivs} · EV {evs} · {movs}");
		}
		combo.SelectedIndex = 0;
		int?[] elegido = new int?[1];
		var panel = new StackPanel { Margin = new Thickness(18) };
		panel.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)), TextWrapping = TextWrapping.Wrap });
		panel.Children.Add(TituloDialogo("Equipo y cajas de la partida (Añil):"));
		panel.Children.Add(combo);
		var ok = BotonConfirmacion("Elegir");
		panel.Children.Add(ok);
		var ventana = CrearDialogo(this, titulo, panel, 260);
		ok.Click += (object _, RoutedEventArgs _) =>
		{
			elegido[0] = combo.SelectedIndex;
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	private int? DialogoStatAnil(string titulo)
	{
		string[] nombres = ["HP", "Ataque", "Defensa", "At. Especial", "Def. Especial", "Velocidad"];
		var combo = new ComboBox { ItemsSource = nombres, SelectedIndex = 0 };
		int?[] elegido = new int?[1];
		var panel = new StackPanel { Margin = new Thickness(18) };
		panel.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)), TextWrapping = TextWrapping.Wrap });
		panel.Children.Add(TituloDialogo("Estadística:"));
		panel.Children.Add(combo);
		var ok = BotonConfirmacion("Aceptar");
		panel.Children.Add(ok);
		var ventana = CrearDialogo(this, titulo, panel, 220);
		ok.Click += (object _, RoutedEventArgs _) => { elegido[0] = combo.SelectedIndex; ventana.Close(); };
		ventana.ShowDialog();
		return elegido[0];
	}

	private string? DialogoTextoDeLista(string titulo, string ayuda, IReadOnlyList<string> opciones)
	{
		var caja = new TextBox();
		var lista = new ListBox { MaxHeight = 220 };
		void Filtrar()
		{
			var texto = caja.Text.Trim();
			lista.Items.Clear();
			foreach (var op in opciones.Where(o => texto.Length == 0 || o.Contains(texto, StringComparison.OrdinalIgnoreCase)).Take(300))
				lista.Items.Add(op);
			if (lista.Items.Count > 0) lista.SelectedIndex = 0;
		}
		caja.TextChanged += (object _, TextChangedEventArgs _) => Filtrar();
		Filtrar();
		string?[] elegido = new string?[1];
		var panel = new StackPanel { Margin = new Thickness(18) };
		panel.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)), TextWrapping = TextWrapping.Wrap });
		panel.Children.Add(TituloDialogo(ayuda));
		panel.Children.Add(caja);
		panel.Children.Add(lista);
		var ok = BotonConfirmacion("Aceptar");
		panel.Children.Add(ok);
		var ventana = CrearDialogo(this, titulo, panel, 420);
		ok.Click += (object _, RoutedEventArgs _) =>
		{
			elegido[0] = lista.SelectedItem as string ?? (caja.Text.Trim().Length > 0 ? caja.Text.Trim() : null);
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}
	private async Task SincronizarEquipoAsync(string? rutaSave)
	{
		if (_firebase is null || _lockeId.Length == 0 || _playerTag.Length == 0) return;
		if (rutaSave is null || !File.Exists(rutaSave)) return;
		try
		{
			// Pokémon Añil: su partida es un .rxdata (Ruby Marshal), no un .sav de PKHeX.
			if (rutaSave.EndsWith(".rxdata", StringComparison.OrdinalIgnoreCase))
			{
				var pbs = PartidaAnil.PbsDe(_cola?.CarpetaJuegoAnil) ?? PartidaAnil.PbsDe(Path.GetDirectoryName(rutaSave));
				var partida = PartidaAnil.Cargar(rutaSave, pbs);
				var okAnil = await _firebase.EscribirEquipoAsync(_lockeId, _playerTag, EquipoAnil.Construir(partida));
				Log(okAnil ? "👥 Equipo de Añil publicado en myTeam" : "No se pudo publicar myTeam (Añil)");
				if (okAnil) TxtLockeLiveUltimaSubida.Text = $"{DateTime.Now:HH:mm} · Equipo publicado";
				return;
			}
			var sav = SaveUtil.GetSaveFile(rutaSave);
			if (sav is null) return;
			var ok = await _firebase.EscribirEquipoAsync(_lockeId, _playerTag, EquipoRemoto.Construir(sav));
			Log(ok ? "👥 Equipo publicado en myTeam (6 huecos)" : "No se pudo publicar myTeam");
			if (ok) TxtLockeLiveUltimaSubida.Text = $"{DateTime.Now:HH:mm} · {sav.PartyCount} Pokémon";
		}
		catch (Exception ex)
		{
			Log("myTeam: " + ex.Message);
		}
	}

	private async Task AplicarPendientesCoreAsync(bool conConfirmacion, RomEntry? romObjetivo = null)
	{
		if (_procesandoCola)
		{
			return;
		}
		try
		{
			try
			{
				if (_cola == null || _firebase == null)
				{
					Aviso("Conecta Firebase e inicia sesión primero.");
					return;
				}
				if (_lockeId.Length == 0 || _playerTag.Length == 0)
				{
					Aviso("Conecta un locke primero (lista Locke → Conectar en vivo).");
					return;
				}
				if (EmuladorAbierto())
				{
					Aviso("El emulador está abierto: ciérralo antes de aplicar acciones (el emulador sobrescribe el .sav al salir).");
					return;
							}

			// CLAVE: se aplica a la ROM con la que se está jugando (o a la elegida), NO a la última usada.
			var rom = romObjetivo ?? RomParaElLocke(_lockeActivo?.Juego, _lockeId) ?? RomDeLaUltimaPartida();
			if (rom is null)
			{
				Aviso("No hay ninguna ROM en la biblioteca. Abre la Biblioteca y añade una.");
				return;
			}
			string? save = ResolverSavePath(rom);
			if (save is not null) Log($"Aplicando acciones a «{rom.Name}» · partida: {save}");
			else if (!string.IsNullOrWhiteSpace(_lockeActivo?.Juego) && !CoincideJuegoRom(rom, _lockeActivo!.Juego))
				Log($"⚠ No encuentro en la biblioteca una ROM de «{_lockeActivo.Juego}»: usando «{rom.Name}».");
			await SincronizarEquipoAsync(save);
			if (save is null)
			{
				Estado("Sin partida guardada: guarda en el emulador y reintenta", "#FFA726");
				Log("No hay partida guardada todavía para «" + rom.Name + "»: no se aplican acciones (el emulador la creará al guardar).");
				return;
			}
				List<KeyValuePair<string, AccionLauncher>> pendientes = (from kv in (await _firebase.LeerColaAsync(_lockeId, _playerTag)).Where((KeyValuePair<string, AccionLauncher> kv) =>
					{
						string status = kv.Value.Status;
						return (status == "pending" || status == "applying") ? true : false;
					})
					orderby kv.Value.CreatedAt
					select kv).ToList();
				if (pendientes.Count == 0)
				{
					if (!conConfirmacion)
					{
						Log("No hay acciones pendientes.");
					Estado("No hay acciones pendientes ahora", "#4CAF50");
					}
					return;
				}
				string lista = string.Join("\n", pendientes.Select((KeyValuePair<string, AccionLauncher> kv) =>
				{
					AccionLauncher value = kv.Value;
					string type = value.Type;
					if (1 == 0)
					{
					}
					string text = type switch
					{
						"CARD_EFFECT" => $"carta {value.CardId} ({value.CardName ?? "?"})", 
						"GACHA_POKEMON" => $"Gachapón #{value.SpeciesId} {value.SpeciesName}", 
						"ITEM_GRANT" => $"objeto {value.ItemName} ×{value.Quantity ?? 1}", 
						"MINT_USE" => "menta " + value.Nature, 
						_ => value.Type, 
					};
					if (1 == 0)
					{
					}
					string text2 = text;
					return "• " + text2 + ((value.Status == "applying") ? " (reanudada)" : "");
				}));
				if (conConfirmacion)
				{
					MessageBoxResult seguir = MessageBox.Show(this, $"Hay {pendientes.Count} acción(es) pendientes de la app que se aplicarán a la partida ANTES de jugar:\n\n{lista}\n\n¿Continuar?", "Aplicar pendientes de la app", MessageBoxButton.YesNo, MessageBoxImage.Question);
					if (seguir != MessageBoxResult.Yes)
					{
						return;
					}
					goto IL_03e1;
				}
				Log($"⚡ Aplicando {pendientes.Count} acción(es): {lista.Replace("\n", " · ")}");
				goto IL_03e1;
				IL_03e1:
				_procesandoCola = true;
				Estado("Aplicando acciones de la app…", "#FFA726");
				foreach (ResultadoAccion r in await _cola.ProcesarColaAsync(_lockeId, _playerTag, save))
				{
					Log("  → [" + r.Estado + "] " + r.ResultMessage + ((r.ErrorMessage == null) ? "" : (" (" + r.ErrorMessage + ")")));
				}
				Estado("Acciones de la app procesadas", "#4CAF50");
				goto end_IL_0047;
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Log("ERROR al aplicar pendientes: " + ex2.Message);
				Estado("Error aplicando acciones", "#EF5350");
				goto end_IL_0047;
			}
			end_IL_0047:;
		}
		finally
		{
			_procesandoCola = false;
			await ActualizarPendientesAsync();
		}
	}

	private async void TimerPendientes_Tick(object? sender, EventArgs e)
	{
		if (_procesandoCola || _lockeId.Length == 0 || _playerTag.Length == 0) return;
		await ActualizarPendientesAsync();
	}


	private static Window CrearDialogo(Window owner, string titulo, StackPanel panel, int alto = 420)
	{
		return new Window
		{
			Title = titulo,
			Width = 520.0,
			Height = alto,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Owner = owner,
			Background = new SolidColorBrush(Color.FromRgb(18, 18, 31)),
			Content = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Content = panel
			}
		};
	}

	private static TextBlock TituloDialogo(string texto)
	{
		return new TextBlock
		{
			Text = texto,
			FontWeight = FontWeights.SemiBold,
			Foreground = new SolidColorBrush(Color.FromRgb(154, 154, 176)),
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0),
			TextWrapping = TextWrapping.Wrap
		};
	}

	private static Button BotonConfirmacion(string texto)
	{
		return new Button
		{
			Content = texto,
			Padding = new Thickness(14.0, 8.0, 14.0, 8.0),
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
	}

	private UbicacionPkm? DialogoObjetivo(string titulo, List<ObjetivoPkm> objetivos)
	{
		if (objetivos.Count == 0)
		{
			Aviso("No hay Pokémon en el equipo ni en las cajas.");
			return null;
		}
		var selector = new SelectorPokemon(titulo, objetivos)
		{
			Owner = this
		};
		return selector.ShowDialog() == true ? selector.Elegido : null;
	}

	private string? DialogoStat(string titulo)
	{
		ComboBox cmb = new ComboBox
		{
			ItemsSource = EfectosCartas.Stats
		};
		string?[] elegido = new string[1];
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240))
		});
		stackPanel.Children.Add(TituloDialogo("Stat (hp | atk | def | spa | spd | spe):"));
		stackPanel.Children.Add(cmb);
		Button button = BotonConfirmacion("Aceptar");
		stackPanel.Children.Add(button);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 280);
		button.Click += (object _, RoutedEventArgs _) =>
		{
			if (cmb.SelectedItem is string text)
			{
				elegido[0] = text;
			}
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	private List<string>? DialogoDosStats(string titulo)
	{
		ComboBox cmb1 = new ComboBox
		{
			ItemsSource = EfectosCartas.Stats
		};
		ComboBox cmb2 = new ComboBox
		{
			ItemsSource = EfectosCartas.Stats
		};
		List<string>?[] resultado = new List<string>[1];
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240))
		});
		stackPanel.Children.Add(TituloDialogo("Primera stat:"));
		stackPanel.Children.Add(cmb1);
		stackPanel.Children.Add(TituloDialogo("Segunda stat:"));
		stackPanel.Children.Add(cmb2);
		Button button = BotonConfirmacion("Aceptar");
		stackPanel.Children.Add(button);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 360);
		button.Click += (object _, RoutedEventArgs _) =>
		{
			if (cmb1.SelectedItem is string text && cmb2.SelectedItem is string text2 && text != text2)
			{
				List<string>?[] array = resultado;
				int num = 2;
				List<string> list = new List<string>(num);
				CollectionsMarshal.SetCount(list, num);
				Span<string> span = CollectionsMarshal.AsSpan(list);
				span[0] = text;
				span[1] = text2;
				array[0] = list;
			}
			ventana.Close();
		};
		ventana.ShowDialog();
		return resultado[0];
	}

	private ushort? DialogoMovimiento(string titulo, ushort maxMov)
	{
		GameStrings strings = GameInfo.GetStrings("es");
		List<(string Nombre, int Id)> nombres = (from x in strings.Move.Select((string n, int i) => (Nombre: n, Id: i))
			where x.Id > 0 && x.Id <= maxMov && !string.IsNullOrWhiteSpace(x.Nombre)
			select x).OrderBy(((string Nombre, int Id) x) => x.Nombre, StringComparer.CurrentCulture).ToList();
		ComboBox cmb = new ComboBox { MaxDropDownHeight = 320.0 };
		foreach (var item in nombres) cmb.Items.Add(item.Nombre + " (#" + item.Id + ")");
		ushort?[] elegido = new ushort?[1];
		StackPanel stackPanel = new StackPanel { Margin = new Thickness(18.0) };
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)),
		});
		stackPanel.Children.Add(TituloDialogo($"Movimiento (solo de esta generación, hasta #{maxMov}):"));
		stackPanel.Children.Add(cmb);
		Button boton = BotonConfirmacion("Aceptar");
		stackPanel.Children.Add(boton);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 380);
		boton.Click += (object _, RoutedEventArgs _) =>
		{
			if (cmb.SelectedIndex >= 0 && cmb.SelectedIndex < nombres.Count)
				elegido[0] = (ushort)nombres[cmb.SelectedIndex].Id;
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	private int? DialogoHabilidad(string titulo, IReadOnlyList<(int Id, string Nombre)> opciones)
	{
		ComboBox cmb = new ComboBox { MaxDropDownHeight = 320.0 };
		foreach (var item in opciones) cmb.Items.Add(item.Nombre + " (#" + item.Id + ")");
		int?[] elegido = new int?[1];
		StackPanel stackPanel = new StackPanel { Margin = new Thickness(18.0) };
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)),
		});
		stackPanel.Children.Add(TituloDialogo("Solo habilidades que esta especie puede tener en esta generación:"));
		stackPanel.Children.Add(cmb);
		Button boton = BotonConfirmacion("Aceptar");
		stackPanel.Children.Add(boton);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 380);
		boton.Click += (object _, RoutedEventArgs _) =>
		{
			if (cmb.SelectedIndex >= 0 && cmb.SelectedIndex < opciones.Count)
				elegido[0] = opciones[cmb.SelectedIndex].Id;
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	/// <summary>Sin hueco libre: elige qué movimiento reemplazar (0..3).</summary>
	private int? DialogoMovimientoReemplazo(string titulo, IReadOnlyList<ushort> actuales)
	{
		GameStrings strings = GameInfo.GetStrings("es");
		ComboBox cmb = new ComboBox { MaxDropDownHeight = 320.0 };
		for (var i = 0; i < actuales.Count && i < 4; i++)
		{
			var id = actuales[i];
			var nombre = id > 0 && id < strings.Move.Count ? strings.Move[id] : "(vacío)";
			cmb.Items.Add((i + 1) + ". " + nombre);
		}
		cmb.SelectedIndex = 0;
		int?[] elegido = new int?[1];
		StackPanel stackPanel = new StackPanel { Margin = new Thickness(18.0) };
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)),
		});
		stackPanel.Children.Add(TituloDialogo("No hay hueco libre: ¿qué movimiento reemplazar?"));
		stackPanel.Children.Add(cmb);
		Button boton = BotonConfirmacion("Reemplazar");
		stackPanel.Children.Add(boton);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 380);
		boton.Click += (object _, RoutedEventArgs _) =>
		{
			if (cmb.SelectedIndex >= 0) elegido[0] = cmb.SelectedIndex;
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	/// <summary>Elige uno de los movimientos dados (los que conoce otro Pokémon).</summary>
	private ushort? DialogoMovimientoDeLista(string titulo, IReadOnlyList<ushort> opciones)
	{
		GameStrings strings = GameInfo.GetStrings("es");
		ComboBox cmb = new ComboBox { MaxDropDownHeight = 320.0 };
		foreach (var id in opciones)
		{
			var nombre = id > 0 && id < strings.Move.Count ? strings.Move[id] : "#" + id;
			cmb.Items.Add(nombre + " (#" + id + ")");
		}
		if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
		ushort?[] elegido = new ushort?[1];
		StackPanel stackPanel = new StackPanel { Margin = new Thickness(18.0) };
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)),
		});
		stackPanel.Children.Add(TituloDialogo("Movimiento:"));
		stackPanel.Children.Add(cmb);
		Button boton = BotonConfirmacion("Aceptar");
		stackPanel.Children.Add(boton);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 380);
		boton.Click += (object _, RoutedEventArgs _) =>
		{
			if (cmb.SelectedIndex >= 0 && cmb.SelectedIndex < opciones.Count)
				elegido[0] = opciones[cmb.SelectedIndex];
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	/// <summary>Elige un objeto de una lista (p. ej. qué megapiedra) mostrando su nombre e id.</summary>
	private int? DialogoObjetoDeLista(string titulo, IReadOnlyList<(int Id, string Nombre)> opciones)
	{
		var cmb = new ComboBox { MaxDropDownHeight = 320.0 };
		foreach (var (id, nombre) in opciones) cmb.Items.Add(nombre + " (#" + id + ")");
		if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
		int?[] elegido = new int?[1];
		StackPanel panel = new StackPanel { Margin = new Thickness(18.0) };
		panel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)),
		});
		panel.Children.Add(TituloDialogo("Objeto:"));
		panel.Children.Add(cmb);
		Button boton = BotonConfirmacion("Aceptar");
		panel.Children.Add(boton);
		Window ventana = CrearDialogo(this, titulo, panel, 380);
		boton.Click += (object _, RoutedEventArgs _) =>
		{
			if (cmb.SelectedIndex >= 0 && cmb.SelectedIndex < opciones.Count)
				elegido[0] = opciones[cmb.SelectedIndex].Id;
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	/// <summary>Editor de IVs (0..31) y EVs (0..252, total ≤ 510) mostrando los valores actuales.</summary>
	private (int[] Ivs, int[] Evs)? DialogoEdicionStats(string titulo, PKM pkm, int maxEvs)
		=> DialogoEdicionStatsComun(titulo,
			new[] { pkm.IV_HP, pkm.IV_ATK, pkm.IV_DEF, pkm.IV_SPA, pkm.IV_SPD, pkm.IV_SPE },
			new[] { pkm.EV_HP, pkm.EV_ATK, pkm.EV_DEF, pkm.EV_SPA, pkm.EV_SPD, pkm.EV_SPE }, maxEvs);

	private (int[] Ivs, int[] Evs)? DialogoEdicionStatsAnil(string titulo, int[] ivsIn, int[] evsIn, int maxEvs)
		=> DialogoEdicionStatsComun(titulo, ivsIn, evsIn, maxEvs);

	private (int[] Ivs, int[] Evs)? DialogoEdicionStatsComun(string titulo, int[] ivsIn, int[] evsIn, int maxEvs)
	{
		var orden = new[] { "hp", "atk", "def", "spa", "spd", "spe" };
		var ivActual = ivsIn;
		var evActual = evsIn;
		var cajasIv = new List<TextBox>();
		var cajasEv = new List<TextBox>();
		Grid grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
		for (var c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
		for (var r = 0; r <= orden.Length; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		string[] cabeceras = ["Stat", "IV (0-31)", "EV (0-252)"];
		for (var c = 0; c < 3; c++)
		{
			var cab = new TextBlock
			{
				Text = cabeceras[c],
				FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(Color.FromRgb(154, 154, 176)),
				Margin = new Thickness(0, 0, 10, 4),
			};
			Grid.SetRow(cab, 0); Grid.SetColumn(cab, c);
			grid.Children.Add(cab);
		}
		for (var i = 0; i < orden.Length; i++)
		{
			var etiqueta = new TextBlock
			{
				Text = orden[i].ToUpperInvariant(),
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)),
			};
			Grid.SetRow(etiqueta, i + 1); Grid.SetColumn(etiqueta, 0);
			grid.Children.Add(etiqueta);
			var iv = new TextBox { Text = ivActual[i].ToString(), Margin = new Thickness(0, 2, 10, 2) };
			Grid.SetRow(iv, i + 1); Grid.SetColumn(iv, 1);
			grid.Children.Add(iv);
			cajasIv.Add(iv);
			var ev = new TextBox { Text = evActual[i].ToString(), Margin = new Thickness(0, 2, 0, 2) };
			Grid.SetRow(ev, i + 1); Grid.SetColumn(ev, 2);
			grid.Children.Add(ev);
			cajasEv.Add(ev);
		}
		TextBlock total = new TextBlock
		{
			Foreground = new SolidColorBrush(Color.FromRgb(154, 154, 176)),
			Margin = new Thickness(0, 8, 0, 0),
			Text = "Total EVs: " + evActual.Sum(),
		};
		(int[] Ivs, int[] Evs)?[] elegido = new (int[], int[])?[1];
		StackPanel stackPanel = new StackPanel { Margin = new Thickness(18.0) };
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)),
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"Ajusta los valores. Máximo 31 por IV y {maxEvs} EVs en total (252 por stat).",
			Foreground = new SolidColorBrush(Color.FromRgb(154, 154, 176)),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 2, 0, 6),
		});
		stackPanel.Children.Add(grid);
		stackPanel.Children.Add(total);
		Button boton = BotonConfirmacion("Aplicar");
		stackPanel.Children.Add(boton);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 420);
		boton.Click += (object _, RoutedEventArgs _) =>
		{
			var ivs = new int[6];
			var evs = new int[6];
			for (var i = 0; i < 6; i++)
			{
				if (!int.TryParse(cajasIv[i].Text.Trim(), out ivs[i]) || ivs[i] < 0 || ivs[i] > 31)
				{
					total.Text = "IV de " + orden[i].ToUpperInvariant() + " inválido (0-31)";
					total.Foreground = new SolidColorBrush(Color.FromRgb(239, 83, 80));
					return;
				}
				if (!int.TryParse(cajasEv[i].Text.Trim(), out evs[i]) || evs[i] < 0 || evs[i] > 252)
				{
					total.Text = "EV de " + orden[i].ToUpperInvariant() + " inválido (0-252)";
					total.Foreground = new SolidColorBrush(Color.FromRgb(239, 83, 80));
					return;
				}
			}
			if (evs.Sum() > maxEvs)
			{
				total.Text = "Los EVs suman " + evs.Sum() + " (máximo " + maxEvs + ")";
				total.Foreground = new SolidColorBrush(Color.FromRgb(239, 83, 80));
				return;
			}
			elegido[0] = (ivs, evs);
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	/// <summary>Diálogo de texto simple (mote).</summary>
	private string? DialogoMote(string titulo)
	{
		TextBox caja = new TextBox { Text = "" };
		string?[] elegido = new string?[1];
		StackPanel stackPanel = new StackPanel { Margin = new Thickness(18.0) };
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240)),
			TextWrapping = TextWrapping.Wrap,
		});
		stackPanel.Children.Add(TituloDialogo("Mote:"));
		stackPanel.Children.Add(caja);
		Button boton = BotonConfirmacion("Aceptar");
		stackPanel.Children.Add(boton);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 380);
		boton.Click += (object _, RoutedEventArgs _) =>
		{
			elegido[0] = caja.Text.Trim();
			ventana.Close();
		};
		ventana.ShowDialog();
		return elegido[0];
	}

	private Dictionary<string, int>? DialogoRepartoEvs(string titulo)
	{
		Dictionary<string, TextBox> cajas = new Dictionary<string, TextBox>();
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240))
		});
		stackPanel.Children.Add(TituloDialogo("Máx. 252 por stat · total ≤ 510"));
		TextBlock txtTotal = new TextBlock
		{
			Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 167, 38)),
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		foreach (string stat in EfectosCartas.Stats)
		{
			StackPanel stackPanel2 = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
			};
			stackPanel2.Children.Add(new TextBlock
			{
				Text = stat.ToUpperInvariant(),
				Width = 60.0,
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240))
			});
			TextBox textBox = new TextBox
			{
				Width = 90.0,
				Text = "0"
			};
			textBox.TextChanged += (object _, TextChangedEventArgs _) =>
			{
				int num = cajas.Values.Sum((TextBox c) => int.TryParse(c.Text, out var result) ? Math.Clamp(result, 0, 252) : 0);
				txtTotal.Text = $"Total: {num} / 510";
				txtTotal.Foreground = new SolidColorBrush((num > 510) ? Color.FromRgb(239, 83, 80) : Color.FromRgb(byte.MaxValue, 167, 38));
			};
			cajas[stat] = textBox;
			stackPanel2.Children.Add(textBox);
			stackPanel.Children.Add(stackPanel2);
		}
		stackPanel.Children.Add(txtTotal);
		Dictionary<string, int>?[] resultado = new Dictionary<string, int>[1];
		Button button = BotonConfirmacion("Aplicar reparto");
		stackPanel.Children.Add(button);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 460);
		button.Click += (object _, RoutedEventArgs _) =>
		{
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			foreach (var (key, textBox3) in cajas)
			{
				dictionary[key] = (int.TryParse(textBox3.Text, out var result) ? Math.Clamp(result, 0, 252) : 0);
			}
			if (ColaLauncherService.RepartoValido(dictionary, out string motivo))
			{
				resultado[0] = dictionary;
				ventana.Close();
			}
			else
			{
				MessageBox.Show(ventana, motivo, "Reparto no válido", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		};
		ventana.ShowDialog();
		return resultado[0];
	}

	private int? DialogoNivel(string titulo)
	{
		TextBox caja = new TextBox
		{
			Text = ""
		};
		int?[] resultado = new int?[1];
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = titulo,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(232, 232, 240))
		});
		stackPanel.Children.Add(TituloDialogo("Nivel (1-100, vacío = propuesto):"));
		stackPanel.Children.Add(caja);
		Button button = BotonConfirmacion("Aceptar");
		stackPanel.Children.Add(button);
		Window ventana = CrearDialogo(this, titulo, stackPanel, 280);
		button.Click += (object _, RoutedEventArgs _) =>
		{
			if (int.TryParse(caja.Text, out var result))
			{
				resultado[0] = Math.Clamp(result, 1, 100);
			}
			ventana.Close();
		};
		ventana.ShowDialog();
		return resultado[0];
	}

	private void ActualizarBotonCuenta()
	{
		Log($"[sesion] email=[{_ajustes.CuentaEmail}] usuario=[{_ajustes.CuentaUsuario}] token={(string.IsNullOrWhiteSpace(_ajustes.CuentaRefreshToken) ? "no" : "sí")} sesion={_firebase?.SesionIniciada}");
		var firebase = _firebase;
		// El nombre debe verse SIEMPRE que haya cuenta guardada, aunque el token se esté
		// renovando o la sesión aún no esté marcada como iniciada.
		var email = firebase?.EmailSesion;
		if (string.IsNullOrWhiteSpace(email)) email = _ajustes.CuentaEmail;
		// Con el email guardado basta: el nombre se ve siempre que haya cuenta,
		// aunque el token se esté renovando o la sesión aún no esté restaurada.
		var hayCuenta = !string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(_ajustes.CuentaUsuario) || !string.IsNullOrWhiteSpace(_ajustes.CuentaRefreshToken);
		if (hayCuenta)
		{
			var nombreVisible = !string.IsNullOrWhiteSpace(_ajustes.CuentaUsuario) ? _ajustes.CuentaUsuario : PokeLobby.PokeLocke.CuentaPokelocke.UsuarioDesdeEmail(email ?? "");
			TxtCuenta.Text = "👤 " + nombreVisible;
			BtnCuenta.Content = "Cerrar sesión";
			return;
		}
		TxtCuenta.Text = "Sin cuenta";
		BtnCuenta.Content = "Iniciar sesión";
		CmbLockesLocke.ItemsSource = null;
		CmbPlayersLocke.ItemsSource = null;
		// TxtLockeConectado eliminado en el rediseño
		// TxtLockeConectado.Text = "";
	}

	/// <summary>Actualiza el indicador de estado de Firebase en la barra lateral.</summary>
	private void ActualizarEstadoFirebase()
	{
		var iniciado = _firebase is not null && _firebase.SesionIniciada;
		TxtFirebaseEstado.Text = iniciado ? "Firebase conectado" : "Firebase Desconectado";
		TxtFirebaseEstado.Foreground = iniciado
			? new SolidColorBrush(Color.FromRgb(74, 225, 118))   // ColorOk (#4AE176)
			: new SolidColorBrush(Color.FromRgb(255, 180, 171)); // ColorError (#FFB4AB)
	}

	private void BtnCuenta_Click(object sender, RoutedEventArgs e)
	{
		if (_firebase == null)
		{
			Aviso("Configura la URL de Firebase en ⚙ Ajustes primero.");
		}
		else if (_firebase.SesionIniciada)
		{
			if (MessageBox.Show(this, "¿Cerrar la sesión de tu cuenta PokeLocke?", "Cerrar sesión", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
			{
				_firebase.CerrarSesion();
				_timerPendientes.Stop();
				_ajustes.CuentaEmail = "";
				_ajustes.CuentaUsuario = "";
				_ajustes.CuentaRefreshToken = "";
				_ajustes.Guardar();
				_lockesFirebase.Clear();
				CmbLockesLocke.ItemsSource = null;
				CmbPlayersLocke.ItemsSource = null;
				ActualizarBotonCuenta();
				ActualizarEstadoFirebase();
				// TxtLockeConectado eliminado en el rediseño
				// TxtLockeConectado.Text = "";
				Log("Sesión cerrada.");
			}
		}
		else
		{
			VentanaLogin();
		}
	}

	private void VentanaLogin()
	{
		var dialogo = new LoginWindow(PokeLobby.PokeLocke.CuentaPokelocke.UsuarioDesdeEmail(_ajustes.CuentaEmail))
		{
			Owner = this
		};
		dialogo.IniciarSesionAsync = async (usuario, contrasena) =>
		{
			if (!ValidarCredenciales(usuario, contrasena, out var motivo)) return motivo;
			try
			{
				string email = PokeLobby.PokeLocke.CuentaPokelocke.EmailInterno(usuario);
				if (_firebase is null) return "Firebase no está configurado (revisa Ajustes).";
				await _firebase.IniciarSesionAsync(email, contrasena);
				GuardarSesion(email);
				await TrasIniciarSesionAsync();
				return null;
			}
			catch (Exception ex)
			{
				return MensajeDeError(ex);
			}
		};
		dialogo.CrearCuentaAsync = async (usuario, contrasena) =>
		{
			if (!ValidarCredenciales(usuario, contrasena, out var motivo)) return motivo;
			try
			{
				string email = PokeLobby.PokeLocke.CuentaPokelocke.EmailInterno(usuario);
				if (_firebase is null) return "Firebase no está configurado (revisa Ajustes).";
				await _firebase.CrearCuentaAsync(email, contrasena);
				GuardarSesion(email);
				await TrasIniciarSesionAsync();
				return null;
			}
			catch (Exception ex)
			{
				return MensajeDeError(ex);
			}
		};
		dialogo.ShowDialog();
		ActualizarBotonCuenta();
		ActualizarEstadoFirebase();
	}

	/// <summary>Valida usuario (formato @pokelocke.com) y contraseña antes de llamar a Firebase.</summary>
	private static bool ValidarCredenciales(string usuario, string contrasena, out string motivo)
	{
		if (!PokeLobby.PokeLocke.CuentaPokelocke.EsValido(usuario, out motivo))
		{
			return false;
		}
		if (!PokeLobby.PokeLocke.CuentaPokelocke.ContrasenaValida(contrasena, out motivo))
		{
			return false;
		}
		motivo = "";
		return true;
	}

	/// <summary>Persiste la sesión (email interno + refresh token) para re-entrar sin contraseña.</summary>
	private void GuardarSesion(string email)
	{
		if (!string.IsNullOrWhiteSpace(_ajustes.CuentaEmail))
			_ajustes.CuentaUsuario = PokeLobby.PokeLocke.CuentaPokelocke.UsuarioDesdeEmail(_ajustes.CuentaEmail);
		_ajustes.CuentaEmail = _firebase?.EmailSesion ?? email;
		_ajustes.CuentaRefreshToken = _firebase?.RefresTokenActual ?? "";
		_ajustes.Guardar();
		Log("Sesión iniciada: " + PokeLobby.PokeLocke.CuentaPokelocke.UsuarioDesdeEmail(_ajustes.CuentaEmail));
	}

	/// <summary>Traduce los errores de Firebase a mensajes claros en español.</summary>
	private static string MensajeDeError(Exception ex)
	{
		var mensaje = ex.Message ?? "";
		if (mensaje.Contains("EMAIL_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
			|| mensaje.Contains("INVALID_LOGIN_CREDENTIALS", StringComparison.OrdinalIgnoreCase)
			|| mensaje.Contains("INVALID_PASSWORD", StringComparison.OrdinalIgnoreCase))
			return "Ese usuario no existe o la contraseña es incorrecta.";
		if (mensaje.Contains("EMAIL_EXISTS", StringComparison.OrdinalIgnoreCase))
			return "Ya existe una cuenta con ese usuario.";
		if (mensaje.Contains("WEAK_PASSWORD", StringComparison.OrdinalIgnoreCase))
			return "La contraseña debe tener al menos 6 caracteres.";
		if (mensaje.Contains("INVALID_EMAIL", StringComparison.OrdinalIgnoreCase))
			return "El nombre de usuario no es válido para la cuenta.";
		return mensaje;
	}
	private async Task TrasIniciarSesionAsync()
	{
		_timerPendientes.Stop();
		_timerPendientes.Tick -= TimerPendientes_Tick;
		_timerPendientes.Tick += TimerPendientes_Tick;
		_timerPendientes.Start();
		await ActualizarPendientesAsync();
		if (!string.IsNullOrWhiteSpace(_firebase?.EmailSesion))
		{
			_ajustes.CuentaEmail = _firebase.EmailSesion;
			_ajustes.Guardar();
		}
		ActualizarBotonCuenta();
		ActualizarEstadoFirebase();
		try
		{
			_playerTag = await _firebase.MiPlayerTagAsync();
			// TxtLockeConectado eliminado en el rediseño
			// TxtLockeConectado.Text = "Cuenta: " + _playerTag;
			Log($"\ud83d\udc64 Cuenta: {_playerTag} ({_firebase.EmailSesion})");
			List<string> ids = await _firebase.LeerIdsMisLockesAsync();
			List<LockeVista> vistas = new List<LockeVista>();
			_lockesFirebase.Clear();
			foreach (string id in ids)
			{
				try
				{
					Locke locke = await _firebase.ObtenerLockeAsync(id);
					if (locke != null)
					{
						_lockesFirebase[id] = locke;
						vistas.Add(new LockeVista(id, locke.Nombre, locke.Juego, locke.Codigo));
					}
				}
				catch
				{
				}
			}
			CmbLockesLocke.ItemsSource = vistas;
			Log($"Tus lockes: {vistas.Count}");
			LockeVista mia = vistas.FirstOrDefault((LockeVista v) => _lockesFirebase.TryGetValue(v.Id, out Locke value) && value.Players.ContainsKey(_playerTag));
			if ((object)mia != null)
			{
				CmbLockesLocke.SelectedItem = mia;
				Log("Locke tuyo detectado y preseleccionado: " + mia.ToString());
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Log("Perfil/lockes: " + ex2.Message);
		}
	}


	private void CerrarTodo()
	{
		try
		{
			_watcher?.Stop();
			_watcher?.Dispose();
		}
		catch
		{
		}
		try
		{
			_ctsLocke?.Cancel();
		}
		catch
		{
		}
		try
		{
			_ramvivo?.Detener();
		}
		catch
		{
		}
	}

	private void Log(string linea)
	{
		var hora = DateTime.Now.ToString("HH:mm:ss");
		((DispatcherObject)this).Dispatcher.Invoke((Action)(() =>
		{
			if (TxtLogLocke is null) return;
			var doc = TxtLogLocke.Document;
			while (doc.Blocks.Count >= 200)
				doc.Blocks.Remove(doc.Blocks.FirstBlock);

			var colorHora = "#DFE2F2";
			if (linea.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || linea.Contains("error", StringComparison.Ordinal))
				colorHora = "#FFB4AB";
			else if (linea.Contains("pendiente", StringComparison.OrdinalIgnoreCase) || linea.Contains("cola", StringComparison.OrdinalIgnoreCase) || linea.Contains("sav", StringComparison.OrdinalIgnoreCase))
				colorHora = "#FFB95F";
			else if (linea.Contains("firebase", StringComparison.OrdinalIgnoreCase) || linea.Contains("conectado", StringComparison.OrdinalIgnoreCase) || linea.Contains("publicado", StringComparison.OrdinalIgnoreCase) || linea.Contains("🔒", StringComparison.Ordinal) || linea.Contains("👥", StringComparison.Ordinal) || linea.Contains("🔗", StringComparison.Ordinal))
				colorHora = "#4AE176";

			var p = new System.Windows.Documents.Paragraph { Margin = new Thickness(0, 0, 0, 6) };
			p.Inlines.Add(new System.Windows.Documents.Run($"[{hora}] ")
			{
				Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHora))
			});
			p.Inlines.Add(new System.Windows.Documents.Run(linea)
			{
				Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7ABBA"))
			});
			doc.Blocks.Add(p);
			TxtLogLocke.ScrollToEnd();
		}));
		try
		{
			File.AppendAllText(RutaLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {linea}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	private void Estado(string texto, string colorHex)
	{
		// DotEstado y TxtEstado eliminados en el rediseño
		// DotEstado.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
		// TxtEstado.Text = texto;
	}

	private void Aviso(string mensaje)
	{
		MessageBox.Show(this, mensaje, "PokeLocke Launcher", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	    public IReadOnlyList<RomEntry> Biblioteca => _biblioteca;
    public string RutaBiblioteca => Path.Combine(_ajustes.DatosDir, "library.json");
    public event Action? BibliotecaCambiada;

    private void CargarBiblioteca()
    {
        var tienda = new LibraryStore(RutaBiblioteca);
        _biblioteca = tienda.Load();
        // Las carátulas que apuntaban a la carpeta antigua de PokeLobby pasan a la propia
        // (la migración ya copió los archivos; aquí solo se corrige la ruta guardada).
        if (RemapearCaratulasAntiguas())
            tienda.Save(_biblioteca);
        _vinculos = VinculosLocke.Cargar(_ajustes.DatosDir);
        ActualizarUltimaRom();
        if (_romSeleccionada is null || !_biblioteca.Any(r => string.Equals(r.RomPath, _romSeleccionada.RomPath, StringComparison.OrdinalIgnoreCase)))
            _romSeleccionada = RomDeLaUltimaPartida();
        ActualizarListaBiblioteca();
        ActualizarDetalleBiblioteca();
        ActualizarHeroUltimaPartida();
        ActualizarBibliotecaReciente();
        _ = ActualizarLockeEnVivo();
        _ = ActualizarMetricasBiblioteca();
        BibliotecaCambiada?.Invoke();
    }

    /// <summary>Actualiza los contadores dinámicos de las tres tarjetas de métrica de la biblioteca.</summary>
    private async Task ActualizarMetricasBiblioteca()
    {
        // Roms en biblioteca
        TxtMetricaRoms.Text = _biblioteca.Count == 1 ? "1 partida" : $"{_biblioteca.Count} partidas";

        // Lockes vinculados (vínculos en vinculos.json o ROMs con SalaId > 0)
        var vinculados = _biblioteca.Count(r => r.SalaId > 0 || !string.IsNullOrWhiteSpace(_vinculos.LockeDeRom(r.RomPath)));
        TxtMetricaLockes.Text = vinculados == 0 ? "Sin vinculados" : $"{vinculados} vinculado{(vinculados > 1 ? "s" : "")}";

        // Pendientes al .sav (cola del locke conectado)
        try
        {
            if (_firebase is not null && !string.IsNullOrWhiteSpace(_lockeId) && !string.IsNullOrWhiteSpace(_playerTag))
            {
                var cola = await _firebase.LeerColaAsync(_lockeId, _playerTag);
                var pendientes = cola.Values.Count(v => v.Status == "pending" || v.Status == "applying");
                TxtMetricaPendientes.Text = pendientes == 0 ? "Sin pendientes" : $"{pendientes} objeto{(pendientes > 1 ? "s" : "")}";
            }
            else
            {
                TxtMetricaPendientes.Text = "Sin locke conectado";
            }
        }
        catch
        {
            TxtMetricaPendientes.Text = "? objetos";
        }
    }

    /// <summary>Obtiene una ruta de carátula válida para un RomEntry, buscando si es necesario.</summary>
    private static string? ResolverCaratula(RomEntry rom, string datosDir)
    {
        if (!string.IsNullOrWhiteSpace(rom.CoverPath) && File.Exists(rom.CoverPath))
            return rom.CoverPath;
        var encontrada = Caratulas.Buscar(datosDir, new[] { rom.DetectedGame, rom.Name });
        return string.IsNullOrWhiteSpace(encontrada) ? null : encontrada;
    }

    /// <summary>Actualiza las imágenes de portada de la página de Inicio con carátulas reales.</summary>
    private void ActualizarPortadasInicio()
    {
        var portadas = _biblioteca
            .Select(r => ResolverCaratula(r, _ajustes.DatosDir))
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();

        ImgPortadaHero.Source = portadas.Count > 0 ? CargarImagen(portadas[0]) : null;
        ImgCoverReciente1.Source = portadas.Count > 1 ? CargarImagen(portadas[1]) : null;
        ImgCoverReciente2.Source = portadas.Count > 2 ? CargarImagen(portadas[2]) : null;
    }

    /// <summary>Texto legible de la consola de una ROM (p. ej. «DS», «GBA»).</summary>
    private static string TextoConsola(RomEntry rom)
    {
        var c = ConsolaDe(rom.ConsoleName);
        return c switch
        {
            ConsolaJuego.Ds => "DS",
            ConsolaJuego.Gba => "GBA",
            ConsolaJuego.TresDs => "3DS",
            ConsolaJuego.Switch => "Switch",
            ConsolaJuego.Anil => "Añil",
            _ => string.IsNullOrWhiteSpace(rom.ConsoleName) ? "?" : rom.ConsoleName,
        };
    }

    /// <summary>Formatea una fecha «yyyy-MM-dd HH:mm» a «dd MMM · HH:mm» (es-ES).</summary>
    private static string FormatoFechaCorto(string? lastPlayed)
    {
        if (string.IsNullOrWhiteSpace(lastPlayed)) return "";
        if (DateTime.TryParse(lastPlayed, out var fecha))
            return fecha.ToString("dd MMM", new System.Globalization.CultureInfo("es-ES")) + " · " + fecha.ToString("HH:mm");
        return lastPlayed;
    }

    /// <summary>Actualiza el Hero de «Última partida» con la ROM más reciente (consola, fecha, portada).</summary>
    private void ActualizarHeroUltimaPartida()
    {
        var rom = RomDeLaUltimaPartida();
        if (rom is null)
        {
            TxtHeroConsola.Text = "";
            TxtHeroFecha.Text = "";
            TxtHeroTitulo.Text = "Sin partidas todavía";
            TxtHeroArchivo.Text = "Añade una ROM con «Añadir ROM» y juega al menos una vez.";
            TxtHeroDescripcion.Text = "";
            BtnJugarUltima.IsEnabled = false;
            BtnAplicarCambiosSav.IsEnabled = false;
            ImgPortadaHero.Source = null;
            return;
        }

        var consola = TextoConsola(rom);
        var fecha = FormatoFechaCorto(rom.LastPlayed);
        TxtHeroConsola.Text = consola;
        TxtHeroFecha.Text = fecha;
        TxtHeroTitulo.Text = string.IsNullOrWhiteSpace(rom.Name) ? "(sin nombre)" : rom.Name;
        TxtHeroArchivo.Text = Path.GetFileName(rom.RomPath);
        TxtHeroDescripcion.Text = "Al iniciar se aplicarán automáticamente los objetos y cartas pendientes de tu sala en Pokelocke sobre el archivo de guardado."
            + (rom.SalaId > 0 ? " Vinculada a la sala «" + rom.RoomName + "»." : "");
        BtnJugarUltima.IsEnabled = true;
        BtnAplicarCambiosSav.IsEnabled = true;
        ImgPortadaHero.Source = ResolverCaratula(rom, _ajustes.DatosDir) is { } cover ? CargarImagen(cover) : null;
    }

    /// <summary>Rellena hasta 3 filas de la biblioteca reciente con los últimos juegos jugados.</summary>
    private void ActualizarBibliotecaReciente()
    {
        var recientes = _biblioteca
            .Where(r => !string.IsNullOrEmpty(r.LastPlayed))
            .OrderByDescending(r => r.LastPlayed)
            .Take(3)
            .ToList();

        var filas = new[]
        {
            (FilaReciente1, ImgCoverReciente1, TxtReciente1Nombre, TxtReciente1Info, TxtReciente1Fecha, BtnJugarReciente1),
            (FilaReciente2, ImgCoverReciente2, TxtReciente2Nombre, TxtReciente2Info, TxtReciente2Fecha, BtnJugarReciente2),
            (FilaReciente3, ImgCoverReciente3, TxtReciente3Nombre, TxtReciente3Info, TxtReciente3Fecha, BtnJugarReciente3),
        };

        TxtBibliotecaCount.Text = _biblioteca.Count == 1
            ? "1 ROM registrada en tu equipo"
            : _biblioteca.Count + " ROMs registradas en tu equipo";

        for (var i = 0; i < filas.Length; i++)
        {
            var (fila, img, nombre, info, fecha, btnJugar) = filas[i];
            if (i < recientes.Count)
            {
                var rom = recientes[i];
                fila.Visibility = Visibility.Visible;
                img.Source = ResolverCaratula(rom, _ajustes.DatosDir) is { } cover ? CargarImagen(cover) : null;
                nombre.Text = string.IsNullOrWhiteSpace(rom.Name) ? "(sin nombre)" : rom.Name;
                var consola = TextoConsola(rom);
                var nombreSala = rom.SalaId > 0 && !string.IsNullOrWhiteSpace(rom.RoomName) ? rom.RoomName : null;
                info.Text = nombreSala is not null ? consola + " " + nombreSala : "Sin locke vinculado";
                fecha.Text = FormatoFechaCorto(rom.LastPlayed);
                btnJugar.Tag = rom;
                btnJugar.Visibility = Visibility.Visible;
            }
            else
            {
                fila.Visibility = Visibility.Collapsed;
                btnJugar.Visibility = Visibility.Collapsed;
                btnJugar.Tag = null;
            }
        }

        TxtBibliotecaVacia.Visibility = _biblioteca.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Actualiza la card de Locke en vivo con el locke conectado o el estado vacío.</summary>
    private async Task ActualizarLockeEnVivo()
    {
        PanelLockeVacio.Visibility = Visibility.Collapsed;
        PanelLockeInfo.Visibility = Visibility.Collapsed;
        TxtLockeNombre.Text = "";
        TxtLockeSave.Text = "";
        TxtLockePendientes.Text = "0 objetos";
        BtnPublicarEquipoInicio.IsEnabled = false;

        TxtLockeLiveRom.Text = "Sin ROM vinculada";
        TxtLockeLiveSav.Text = "—";
        TxtLockeLiveCola.Text = "0 objetos pendientes";
        TxtLockeLiveCola.Foreground = (Brush)FindResource("ColorTextoSuave");
        PathLockeCloudSync.Fill = (Brush)FindResource("ColorTextoSuave");

        if (_lockeActivo is null || string.IsNullOrWhiteSpace(_lockeId) || string.IsNullOrWhiteSpace(_playerTag))
        {
            PanelLockeVacio.Visibility = Visibility.Visible;
            return;
        }

        PanelLockeInfo.Visibility = Visibility.Visible;
        TxtLockeNombre.Text = _lockeActivo.Nombre;
        PathLockeCloudSync.Fill = (Brush)FindResource("ColorOk");

        var rom = RomParaElLocke(_lockeActivo.Juego, _lockeId) ?? RomDeLaUltimaPartida();
        if (rom is not null)
        {
            TxtLockeLiveRom.Text = rom.Name;
            var save = ResolverSavePath(rom);
            var saveFile = save is not null ? Path.GetFileName(save) : "Sin partida";
            TxtLockeSave.Text = saveFile;
            TxtLockeLiveSav.Text = saveFile;
            BtnPublicarEquipoInicio.IsEnabled = save is not null;
        }
        else
        {
            TxtLockeSave.Text = "Sin ROM vinculada";
            TxtLockeLiveRom.Text = "Sin ROM vinculada";
            TxtLockeLiveSav.Text = "—";
        }

        // Contar cambios pendientes en la cola del jugador.
        try
        {
            if (_firebase is not null)
            {
                var cola = await _firebase.LeerColaAsync(_lockeId, _playerTag);
                var pendientes = cola.Values.Count(v => v.Status == "pending" || v.Status == "applying");
                TxtLockePendientes.Text = pendientes == 1 ? "1 objeto" : pendientes + " objetos";
                TxtLockeLiveCola.Text = pendientes == 1 ? "1 objeto pendiente" : pendientes + " objetos pendientes";
                TxtLockeLiveCola.Foreground = (Brush)FindResource(pendientes > 0 ? "ColorAviso" : "ColorTextoSuave");
            }
        }
        catch
        {
            TxtLockePendientes.Text = "? objetos";
            TxtLockeLiveCola.Text = "? objetos pendientes";
        }
    }

    /// <summary>
    /// Corrige las carátulas guardadas con la ruta de una carpeta de datos heredada
    /// (PokeLobby o la etapa intermedia en LOCALAPPDATA). Devuelve true si cambió alguna.
    /// </summary>
    private bool RemapearCaratulasAntiguas()
    {
        var antiguas = new[]
        {
            RutasLauncher.DatosAntiguos,
            Path.Combine(RutasLauncher.BaseAntiguaLocal, "datos"),
        };
        var cambio = false;
        foreach (var rom in _biblioteca)
        {
            if (string.IsNullOrWhiteSpace(rom.CoverPath)) continue;
            foreach (var antigua in antiguas)
            {
                string relativa;
                try { relativa = Path.GetRelativePath(antigua, rom.CoverPath); }
                catch { continue; }
                if (Path.IsPathRooted(relativa) || relativa.StartsWith("..", StringComparison.Ordinal)) continue;
                var nueva = Path.Combine(_ajustes.DatosDir, relativa);
                if (!File.Exists(nueva)) continue;
                rom.CoverPath = nueva;
                cambio = true;
                break;
            }
        }
        return cambio;
    }

    // ListaRoms eliminado en el rediseño
    // private void ListaRoms_SelectionChanged(object sender, SelectionChangedEventArgs e)
    //     => ActualizarDetalleBiblioteca();

    /// <summary>Reconstruye dinámicamente la lista de ROMs en la pestaña Biblioteca.</summary>
    private void ActualizarListaBiblioteca()
    {
        PanelListaRoms.Children.Clear();
        if (_biblioteca.Count == 0)
        {
            PanelListaRoms.Children.Add(new TextBlock
            {
                Text = "No hay ROMs en la biblioteca todavía. Pulsa «Añadir ROM» para empezar.",
                FontSize = 12,
                Foreground = (Brush)FindResource("ColorTextoSuave"),
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var rom in _biblioteca)
        {
            var esSeleccionada = _romSeleccionada is not null &&
                string.Equals(rom.RomPath, _romSeleccionada.RomPath, StringComparison.OrdinalIgnoreCase);

            var card = new Border
            {
                Background = (Brush)FindResource(esSeleccionada ? "ColorPanelContainerHigh" : "ColorPanelContainer"),
                BorderBrush = esSeleccionada
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B92318"))
                    : (Brush)FindResource("ColorBordeVariant"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Carátula
            var imgBorder = new Border
            {
                Width = 48, Height = 48, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0F17")),
                Margin = new Thickness(0, 0, 12, 0),
                ClipToBounds = true,
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            var cover = ResolverCaratula(rom, _ajustes.DatosDir);
            if (cover is not null) img.Source = CargarImagen(cover);
            imgBorder.Child = img;
            Grid.SetColumn(imgBorder, 0);
            grid.Children.Add(imgBorder);

            // Centro: nombre + consola + locke
            var centerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            centerStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(rom.Name) ? "(sin nombre)" : rom.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("ColorTexto"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var consolaBadge = new Border
            {
                Background = (Brush)FindResource("ColorPanelContainerLow"),
                BorderBrush = (Brush)FindResource("ColorBordeVariant"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = TextoConsola(rom),
                    FontSize = 10.5,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("ColorTexto"),
                }
            };
            metaRow.Children.Add(consolaBadge);

            var lockeIdRom = _vinculos.LockeDeRom(rom.RomPath);
            string lockeTexto;
            bool tieneLocke;
            if (!string.IsNullOrWhiteSpace(lockeIdRom) && _lockesFirebase.TryGetValue(lockeIdRom, out var lockeObj))
            {
                lockeTexto = string.IsNullOrWhiteSpace(lockeObj.Codigo) ? lockeObj.Nombre : $"Sala {lockeObj.Codigo} · {lockeObj.Nombre}";
                tieneLocke = true;
            }
            else if (rom.SalaId > 0)
            {
                lockeTexto = $"Sala {rom.SalaId} · {rom.RoomName}";
                tieneLocke = true;
            }
            else
            {
                lockeTexto = "Sin locke vinculado";
                tieneLocke = false;
            }

            if (tieneLocke)
            {
                metaRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D2B0D")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7A5314")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2, 8, 2),
                    Child = new TextBlock
                    {
                        Text = lockeTexto,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("ColorAviso"),
                    }
                });
            }
            else
            {
                metaRow.Children.Add(new TextBlock
                {
                    Text = lockeTexto,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("ColorTextoSuave"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            centerStack.Children.Add(metaRow);
            Grid.SetColumn(centerStack, 1);
            grid.Children.Add(centerStack);

            // Derecha: fecha + estado seleccionada
            var rightStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var fechaTexto = string.IsNullOrWhiteSpace(rom.LastPlayed)
                ? "Sin jugar"
                : (rom.LastPlayed.Length >= 10 ? rom.LastPlayed[..10] : rom.LastPlayed);
            rightStack.Children.Add(new TextBlock
            {
                Text = fechaTexto,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("ColorTextoSuave"),
                HorizontalAlignment = HorizontalAlignment.Right,
            });
            if (esSeleccionada)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = "Seleccionada",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AcentoBrush"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 3, 0, 0),
                });
            }
            else if (!string.IsNullOrWhiteSpace(rom.LastPlayed) && rom.LastPlayed.Length >= 16)
            {
                rightStack.Children.Add(new TextBlock
                {
                    Text = rom.LastPlayed.Substring(11, 5),
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = (Brush)FindResource("ColorTextoSuave"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 3, 0, 0),
                });
            }
            Grid.SetColumn(rightStack, 2);
            grid.Children.Add(rightStack);

            card.Child = grid;
            var romLocal = rom;
            card.MouseLeftButtonUp += (_, _) =>
            {
                _romSeleccionada = romLocal;
                ActualizarListaBiblioteca();
                ActualizarDetalleBiblioteca();
            };

            PanelListaRoms.Children.Add(card);
        }
    }

    /// <summary>Rellena la ficha de la ROM seleccionada (carátula, partida, última vez…).</summary>
    private void ActualizarDetalleBiblioteca()
    {
        var rom = _romSeleccionada;
        var hayRom = rom is not null;
        BtnJugar.IsEnabled = hayRom;
        BtnCambiarCaratula.IsEnabled = hayRom;
        BtnVincularLocke.IsEnabled = hayRom;
        BtnPublicarEquipoBib.IsEnabled = hayRom;
        BtnEscamasCorazon.IsEnabled = hayRom;
        BtnCarameloraros.IsEnabled = hayRom;
        BtnBorrarRom.IsEnabled = hayRom;

        if (rom is null)
        {
            TxtDetConsolaJuego.Text = "SIN SELECCIÓN";
            TxtDetNombre.Text = "Selecciona una ROM";
            TxtDetLocke.Text = "Sin vincular";
            TxtDetJugador.Text = "—";
            TxtDetUltima.Text = "—";
            TxtDetSave.Text = "—";
            ImgCoverDet.Source = null;
            return;
        }

        var juego = string.IsNullOrWhiteSpace(rom.DetectedGame) ? "Juego sin detectar" : rom.DetectedGame;
        TxtDetConsolaJuego.Text = $"{TextoConsola(rom).ToUpperInvariant()} · {juego.ToUpperInvariant()}";
        TxtDetNombre.Text = string.IsNullOrWhiteSpace(rom.Name) ? "(sin nombre)" : rom.Name;

        var lockeIdRom = _vinculos.LockeDeRom(rom.RomPath);
        if (!string.IsNullOrWhiteSpace(lockeIdRom) && _lockesFirebase.TryGetValue(lockeIdRom, out var lockeObj))
            TxtDetLocke.Text = $"«{lockeObj.Nombre}»";
        else if (rom.SalaId > 0)
            TxtDetLocke.Text = $"«{rom.RoomName}»";
        else
            TxtDetLocke.Text = "Sin vincular";

        TxtDetJugador.Text = !string.IsNullOrWhiteSpace(_playerTag)
            ? _playerTag
            : (TxtCuenta.Text != "Sin cuenta" ? TxtCuenta.Text.Replace("👤 ", "") : "—");
        TxtDetUltima.Text = string.IsNullOrWhiteSpace(rom.LastPlayed) ? "Nunca" : rom.LastPlayed;

        var save = ResolverSavePath(rom);
        TxtDetSave.Text = save ?? "Todavía no existe (el emulador la creará al guardar)";

        var cover = ResolverCaratula(rom, _ajustes.DatosDir);
        ImgCoverDet.Source = cover is not null ? CargarImagen(cover) : null;
    }

    private static System.Windows.Media.Imaging.BitmapImage? CargarImagen(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta)) return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(ruta);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private void BtnBorrarRom_Click(object sender, RoutedEventArgs e)
    {
        if (_romSeleccionada is not RomEntry rom)
        {
            Aviso("Selecciona la ROM que quieres borrar de la biblioteca.");
            return;
        }
        var respuesta = MessageBox.Show(this,
            "¿Borrar «" + rom.Name + "» de la biblioteca?" + Environment.NewLine + Environment.NewLine + rom.RomPath +
            Environment.NewLine + Environment.NewLine + "Solo se quita de la lista: los archivos de la ROM y su partida NO se borran.",
            "Borrar ROM", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (respuesta == MessageBoxResult.Yes)
            BorrarRom(rom);
    }

    private async void BtnJugar_Click(object sender, RoutedEventArgs e)
    {
        if (_romSeleccionada is not RomEntry rom)
        {
            Aviso("Selecciona una ROM de la biblioteca.");
            return;
        }
        await JugarRomAsync(rom);
    }

    private async void BtnPublicarEquipoBiblioteca_Click(object sender, RoutedEventArgs e)
    {
        if (_romSeleccionada is not RomEntry rom)
        {
            Aviso("Selecciona una ROM de la biblioteca.");
            return;
        }
        if (_lockeId.Length == 0 || _playerTag.Length == 0)
        {
            Aviso("Conecta un locke primero en «Locke en vivo».");
            return;
        }
        var save = ResolverSavePath(rom);
        if (save is null)
        {
            Aviso("Esa ROM todavía no tiene partida guardada.");
            return;
        }
        await SincronizarEquipoAsync(save);
    }

    private void BtnCambiarCaratula_Click(object sender, RoutedEventArgs e)
    {
        if (_romSeleccionada is not RomEntry rom)
        {
            Aviso("Selecciona una ROM de la biblioteca.");
            return;
        }
        string? nueva = CaratulaDialogos.Elegir(this, _ajustes, rom.DetectedGame, rom.Name);
        if (nueva is null) return;
        rom.CoverPath = nueva;
        if (GuardarBiblioteca())
        {
            ActualizarListaBiblioteca();
            ActualizarDetalleBiblioteca();
            ActualizarHeroUltimaPartida();
            ActualizarBibliotecaReciente();
            Log("🖼 Carátula actualizada: " + rom.Name + (nueva.Length == 0 ? " (quitada)" : " → " + Path.GetFileName(nueva)));
        }
    }

    /// <summary>ROM más recientemente jugada (o la primera con partida conocida).</summary>
    private RomEntry? RomDeLaUltimaPartida()
    {
        return _biblioteca
            .Where(r => !string.IsNullOrEmpty(r.LastPlayed))
            .OrderByDescending(r => r.LastPlayed)
            .FirstOrDefault()
            ?? _biblioteca.FirstOrDefault(r => ResolverSavePath(r) is not null)
            ?? _biblioteca.FirstOrDefault();
    }

    private void ActualizarUltimaRom()
    {
        var rom = RomDeLaUltimaPartida();
        // TxtUltimaRom eliminado en el rediseño: la última partida se muestra en el Hero card de Inicio
        // TxtUltimaRom.Text = rom is null
        //     ? "Aún no hay ROMs en la biblioteca: pulsa «Añadir ROM» o abre la Biblioteca."
        //     : "Última partida: " + rom.Name + " — " + rom.LastPlayed;
    }

    public void RecargarBiblioteca()
    {
        CargarBiblioteca();
        ActualizarPortadasInicio();
        // Migración: cualquier ROM que sea un .exe es un fangame (Pokémon Añil), no Switch/GBA.
        var cambiado = false;
        foreach (var rom in _biblioteca)
        {
            if (rom.RomPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(rom.ConsoleName, "Añil", StringComparison.OrdinalIgnoreCase))
            {
                rom.ConsoleName = "Añil";
                cambiado = true;
            }
        }
        if (cambiado) GuardarBiblioteca();
        Log($"Biblioteca recargada: {_biblioteca.Count} ROM(s)");
    }

    public void AnadirRomDesdeDialogo()
    {
        SonidosUi.Click();
        var dlg = new AddRomDialog(_ajustes) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Resultado is null) return;
        AnadirRomALaBiblioteca(dlg.Resultado);
    }

    /// <summary>Quita una ROM de la biblioteca (los archivos NO se borran).</summary>
    public void BorrarRom(RomEntry rom)
    {
        if (!_biblioteca.Remove(rom))
        {
            Log("✗ La ROM ya no está en la biblioteca: " + rom.Name);
            return;
        }
        if (GuardarBiblioteca())
        {
            Log("🗑 ROM borrada de la biblioteca: " + rom.Name + " (los archivos siguen en disco)");
            Estado("ROM borrada de la biblioteca", "#F0A24A");
        }
        CargarBiblioteca();
        ActualizarPortadasInicio();
    }
    /// <summary>
    /// Guarda library.json. Si la carpeta de datos no es escribible, avisa con la causa y la
    /// solución (cambiar la carpeta en Ajustes) en vez de lanzar «Access to the path … denied».
    /// </summary>
    private bool GuardarBiblioteca()
    {
        try
        {
            new LibraryStore(RutaBiblioteca).Save(_biblioteca);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Log("✗ No se pudo guardar la biblioteca en «" + _ajustes.DatosDir + "»: " + ex.Message);
            var cambiar = MessageBox.Show(this,
                "No tengo permiso para escribir en la carpeta de datos:\n" + _ajustes.DatosDir + "\n\n" +
                "Detalle: " + ex.Message + "\n\n" +
                "¿Quieres elegir otra carpeta de datos ahora? (p. ej. Documentos\\PokeLockeLauncher)",
                "No se pudo guardar library.json", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (cambiar == MessageBoxResult.Yes)
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Nueva carpeta de datos del launcher" };
                if (dlg.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    _ajustes.DatosDir = dlg.FolderName;
                    try { _ajustes.Guardar(); } catch { }
                    try
                    {
                        Directory.CreateDirectory(_ajustes.DatosDir);
                        new LibraryStore(RutaBiblioteca).Save(_biblioteca);
                        Log("✔ Biblioteca guardada en la nueva carpeta: " + _ajustes.DatosDir);
                        return true;
                    }
                    catch (Exception ex2)
                    {
                        Log("✗ Tampoco se pudo guardar en la carpeta nueva: " + ex2.Message);
                    }
                }
            }
            return false;
        }
    }

    private void BtnRecargar_Click(object sender, RoutedEventArgs e) => RecargarBiblioteca();

    /// <summary>ResolverSave para la ventana de biblioteca.</summary>
    public string? ResolverSave(RomEntry rom) => ResolverSavePath(rom);
	/// <summary>
	/// Busca en la biblioteca la ROM que corresponde al juego del locke (p. ej. «X» → «Pokemon X Legend»).
	/// Sin esto se aplicaba la cola a la ROM seleccionada en ese momento y la comprobación de juego
	/// fallaba con «El locke es de X pero la partida es de B».
	/// </summary>
	private RomEntry? RomParaElLocke(string? juego, string? lockeId = null)
	{
		// 1) Vínculo explícito locke → ROM (Biblioteca → «🔗 Vincular a locke»).
		var vinculada = _vinculos.RomDe(lockeId);
		if (!string.IsNullOrWhiteSpace(vinculada))
		{
			var entrada = _biblioteca.FirstOrDefault(r => string.Equals(r.RomPath, vinculada, StringComparison.OrdinalIgnoreCase));
			if (entrada is not null) return entrada;
		}
		if (string.IsNullOrWhiteSpace(juego)) return null;
		var candidatas = _biblioteca.Where(r => CoincideJuegoRom(r, juego)).ToList();
		if (candidatas.Count == 0) return null;
		var consola = ConsolaDeJuego(juego);
		if (consola is not null)
		{
			var mismaConsola = candidatas.Where(r => string.Equals(r.ConsoleName, consola, StringComparison.OrdinalIgnoreCase)).ToList();
			if (mismaConsola.Count > 0) candidatas = mismaConsola;
		}
		// Si hay varias, la del nombre más parecido (menos caracteres de diferencia).
		var objetivo = JuegosApp.Normalizar(juego);
		return candidatas.OrderBy(r => Math.Abs(JuegosApp.Normalizar(r.Name).Length - objetivo.Length)).First();
	}

	/// <summary>¿El nombre de la ROM contiene el nombre del juego como palabra (X, Negro, Rubí Omega…)?</summary>
	private static bool CoincideJuegoRom(RomEntry rom, string juego)
	{
		var nombre = JuegosApp.Normalizar(rom.Name);
		var buscado = JuegosApp.Normalizar(juego);
		if (buscado.Length == 0) return false;
		if (string.Equals(nombre, buscado, StringComparison.OrdinalIgnoreCase)) return true;
		// «Pokemon X Legend» contiene « x » como palabra; «Xtest» no.
		return (" " + nombre + " ").Contains(" " + buscado + " ", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Consola esperada para un juego del contrato de la app (para desambiguar nombres).</summary>
	private static string? ConsolaDeJuego(string? juego)
	{
		var n = JuegosApp.Normalizar(juego ?? "");
		if (n.Length == 0) return null;
		if (n is "x" or "y" or "or" or "as" or "sol" or "lun" or "us" or "um"
			|| n.Contains("rubi omega") || n.Contains("zafiro alfa") || n.Contains("ultrasol") || n.Contains("ultraluna"))
			return "3DS";
		if (n is "negro" or "blanco" or "negro 2" or "blanco 2" or "diamante" or "perla" or "platino"
			|| n.Contains("heartgold") || n.Contains("soulsilver") || n.Contains("oro heartgold") || n.Contains("plata alma"))
			return "DS";
		if (n is "rubi" or "zafiro" or "esmeralda" or "rojo fuego" or "verde hoja" or "amarillo" or "oro" or "plata" or "cristal" or "rojo" or "azul")
			return "GBA";
		return null;
	}

private string? ResolverSavePath(RomEntry rom)
	{
		Dictionary<string, string> huellas = new(StringComparer.OrdinalIgnoreCase);
		try
		{
			foreach (var kv in new HuellasStore(Path.Combine(_ajustes.DatosDir, "huellas.json")).Load())
			{
				if (!string.IsNullOrWhiteSpace(kv.Value.SavePath))
					huellas[kv.Key] = kv.Value.SavePath;
			}
		}
		catch { /* huellas ilegibles */ }
		return LocalizadorPartidas.Buscar(rom.RomPath, rom.SavePath, huellas);
	}

	private static ConsolaJuego? ConsolaDe(string? nombre)
	{
		string text = nombre?.Trim().ToLowerInvariant();
		if (1 == 0)
		{
		}
		ConsolaJuego? result = text switch
		{
			"gba" => (ConsolaJuego?)ConsolaJuego.Gba, 
			"ds" => ConsolaJuego.Ds, 
			"3ds" => ConsolaJuego.TresDs, 
			"switch" => ConsolaJuego.Switch, 
			"anil" or "añil" or "exe" or "fangame" => ConsolaJuego.Anil, 
			_ => null, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	    private async void BtnJugarUltima_Click(object sender, RoutedEventArgs e)
    {
        var rom = RomDeLaUltimaPartida();
        if (rom is null)
        {
            Aviso("No hay ROMs en la biblioteca. Añade una con «➕ Añadir ROM».");
            return;
        }
        await JugarRomAsync(rom);
    }

    /// <summary>▶ JUGAR de Pokémon Añil: aplica la cola al .rxdata y abre el juego directamente.</summary>
    private async Task JugarAnilAsync(RomEntry rom)
    {
        if (!File.Exists(rom.RomPath))
        {
            Aviso("No existe el ejecutable del juego:\n" + rom.RomPath);
            return;
        }

        var carpetaJuego = Path.GetDirectoryName(rom.RomPath)!;
        if (_cola is not null) _cola.CarpetaJuegoAnil = carpetaJuego;

        var save = ResolverSavePath(rom) ?? GuardadoAnil.Buscar(rom.RomPath, rom.SavePath);
        if (save is null)
            Log("⚠ No encuentro la partida de Añil todavía (¿has guardado alguna vez en el juego?).");
        else
            Log("Partida de Añil: " + save);

        var nombreProceso = Path.GetFileNameWithoutExtension(rom.RomPath);
        if (Process.GetProcessesByName(nombreProceso).Length > 0)
        {
            Aviso("Pokémon Añil está abierto. Ciérralo antes de aplicar acciones (el juego guarda al salir).");
            return;
        }

        await AplicarPendientesCoreAsync(conConfirmacion: true, rom);
        if (save is not null) await SincronizarEquipoAsync(save);

        var proceso = Process.Start(new ProcessStartInfo(rom.RomPath)
        {
            UseShellExecute = true,
            WorkingDirectory = carpetaJuego,
        }) ?? throw new InvalidOperationException("No se pudo abrir el ejecutable del juego.");

        Log($"▶ Juego lanzado: {proceso.ProcessName} (pid {proceso.Id}) — {Path.GetFileName(rom.RomPath)}");
        if (save is not null)
        {
            _watcher?.Stop();
            _watcher?.Dispose();
            var watcher = new SaveWatcher(new HuellasStore(Path.Combine(_ajustes.DatosDir, "huellas.json")), conResumen: true);
            watcher.GuardadoCambiado += AlCambiarPartida;
            watcher.Start(Path.GetDirectoryName(save)!);
            _watcher = watcher;
        }
        rom.LastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        GuardarBiblioteca();
        ActualizarHeroUltimaPartida();
        ActualizarBibliotecaReciente();
        _ = ActualizarLockeEnVivo();
        Estado("Jugando " + rom.Name + " (Añil)", "#4CAF50");
    }

    /// <summary>▶ JUGAR sobre una ROM concreta (la llama la ventana de biblioteca).</summary>
    public async Task JugarRomAsync(RomEntry rom)
    {
        try
        {
			// Pokémon Añil: fangame con .exe propio; no usa emulador ni .sav junto a la ROM.
			if (GuardadoAnil.EsJuegoAnil(rom.RomPath))
			{
				await JugarAnilAsync(rom);
				return;
			}
			ConsolaJuego? consola = ConsolaDe(rom.ConsoleName);
			if (!consola.HasValue)
			{
				Aviso("Consola no soportada: «" + rom.ConsoleName + "»");
				return;
			}
			bool switchExterno = consola == ConsolaJuego.Switch && EmuladorSwitch.Configurado;
			string vendor = _ajustes.VendorDir;
			if (!switchExterno && (string.IsNullOrWhiteSpace(vendor) || !Directory.Exists(vendor)))
			{
				Aviso((consola == ConsolaJuego.Switch) ? "Esta ROM es de Switch: configura el ejecutable del emulador en ⚙ Ajustes (o la carpeta vendor)." : "No encuentro la carpeta vendor\\ con los emuladores. Configúrala en ⚙ Ajustes.");
				return;
			}
			if (!File.Exists(rom.RomPath))
			{
				Aviso("La ROM no existe:\n" + rom.RomPath);
				return;
			}
			string save = ResolverSavePath(rom) ?? LocalizadorPartidas.RutaPrevista(rom.RomPath, rom.SavePath);
			string? carpetaPartidas = LocalizadorPartidas.CarpetaDe(rom.RomPath, rom.SavePath);
			if (carpetaPartidas is null)
			{
				Aviso("No encuentro la carpeta donde el emulador debe guardar la partida de esta ROM.");
				return;
			}
			if (!PermisosCarpeta.PuedeEscribir(carpetaPartidas))
			{
				Log("✗ La carpeta de partidas no es escribible: " + carpetaPartidas);
				Aviso("La carpeta de partidas no tiene permiso de escritura:\n" + carpetaPartidas +
					  "\n\nEl emulador no podrá guardar (melonDS: «Unable to write to … save»).\n" +
					  "Elige otra carpeta en «Añadir ROM» o quita el atributo de solo lectura.");
				return;
			}
			if (!File.Exists(save))
				Log("La partida aún no existe: se creará al guardar en «" + save + "».");
			if (!PermisosCarpeta.PuedeEscribirEnArchivo(save))
			{
				Log("✗ La carpeta de la partida no es escribible: " + Path.GetDirectoryName(save));
				Aviso("La carpeta de la partida no tiene permiso de escritura:\n" + Path.GetDirectoryName(save) + "\n\nEl emulador no podrá guardar (melonDS: «Unable to write to … save»).\nQuita el atributo de solo lectura o mueve la ROM y su .sav a otra carpeta.");
				return;
			}
			await AplicarPendientesCoreAsync(conConfirmacion: true, rom);
			if (EmuladorAbierto())
			{
				Log("⚠ Hay un emulador abierto: no se aplican acciones al .sav mientras corre.");
				MessageBoxResult abrirIgual = MessageBox.Show(this, "Ya hay un emulador abierto.\n\nSi es el que abriste antes (sin la ROM), ciérralo y vuelve a pulsar JUGAR para que se apliquen las acciones pendientes y se cargue la ROM.\n\n¿Quieres abrirlo igualmente con la ROM ahora?", "Emulador ya abierto", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (abrirIgual != MessageBoxResult.Yes) return;
			}
			if (switchExterno)
			{
				string args = EmuladorSwitch.ConstruirArgumentos(rom.RomPath);
				if (Process.Start(new ProcessStartInfo
				{
					FileName = EmuladorSwitch.Ruta,
					Arguments = args,
					UseShellExecute = true
				}) == null)
				{
					throw new InvalidOperationException("El emulador de Switch no arrancó.");
				}
				Log("▶ Emulador de Switch: " + Path.GetFileName(EmuladorSwitch.Ruta) + " " + args);
			}
			else
			{
				EmulatorService servicio = new EmulatorService(vendor);
				Process proceso = servicio.Lanzar(consola.Value, rom.RomPath);
				Log($"▶ Emulador lanzado: {proceso.ProcessName} (pid {proceso.Id}) — {Path.GetFileName(rom.RomPath)}");
			}
			_watcher?.Stop();
			_watcher?.Dispose();
			SaveWatcher watcher = new SaveWatcher(new HuellasStore(Path.Combine(_ajustes.DatosDir, "huellas.json")), conResumen: true);
			watcher.GuardadoCambiado += AlCambiarPartida;
			watcher.Start(Path.GetDirectoryName(save));
			_watcher = watcher;
			rom.LastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
			GuardarBiblioteca();
			ActualizarHeroUltimaPartida();
			ActualizarBibliotecaReciente();
			_ = ActualizarLockeEnVivo();
			Estado("Jugando " + rom.Name + " (vigilando la partida)", "#4CAF50");
			_ramvivo?.Detener();
			if (consola == ConsolaJuego.TresDs && _lockeActivo != null && _firebase != null && rom.SalaId > 0)
			{
				string lockeId = _lockeId;
				_ramvivo = new RamVivoService(async (int sala, List<object> lote) => await _firebase.GuardarEventosVivoAsync(lockeId, lote));
				_ramvivo.Iniciar(rom.RomPath, () => save, rom.SalaId, "3DS");
				Log("RAM-vivo activa → eventos en vivo al locke " + _lockeActivo.Nombre);
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Estado("Error al lanzar", "#EF5350");
			Log("ERROR JUGAR: " + ex2.Message);
		}
	}

	private async void AlCambiarPartida(object? sender, SaveChange cambio)
	{
		try
		{
			ResumenPartida resumen = cambio.Resumen;
			string showdown = null;
			try
			{
				SaveFile sav = ResumenPartidaService.Abrir(cambio.FilePath);
				if (resumen == null)
				{
					resumen = ResumenPartidaService.Extraer(sav);
				}
				showdown = ResumenPartidaService.ExportarShowdown(sav);
			}
			catch
			{
			}
			((DispatcherObject)this).Dispatcher.Invoke((Action)(() =>
			{
				Log($"\ud83d\udcbe Guardado: {Path.GetFileName(cambio.FilePath)} ({cambio.Size:N0} bytes, {cambio.Motivo})");
				if (resumen != null)
				{
					ResumenPartida resumenPartida = resumen;
					if (true)
					{
						Log($"   {resumenPartida.Entrenador} · {resumenPartida.Pokemon.Count} Pokémon · medallas {resumenPartida.Medallas} · dinero {resumenPartida.Dinero:N0}");
						Estado($"Partida sincronizada ✔ ({resumenPartida.Pokemon.Count} Pokémon)", "#4CAF50");
						goto IL_01d0;
					}
				}
				Estado("Guardado detectado (resumen pendiente)", "#FFA726");
				goto IL_01d0;
				IL_01d0:
				if (!string.IsNullOrWhiteSpace(showdown))
				{
					Log("   Equipo (Showdown):" + Environment.NewLine + Indent(showdown));
				}
			}));
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			((DispatcherObject)this).Dispatcher.Invoke((Action)(() =>
			{
				Estado("Error leyendo la partida", "#EF5350");
				Log("ERROR resumen: " + ex3.Message);
			}));
		}
	}

	private static string Indent(string texto)
	{
		return string.Join(Environment.NewLine, from l in texto.Split('\n')
			select "   | " + l.TrimEnd());
	}

	private void CmbLockesLocke_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(CmbLockesLocke.SelectedItem is LockeVista lockeVista) || !_lockesFirebase.TryGetValue(lockeVista.Id, out Locke value))
		{
			CmbPlayersLocke.ItemsSource = null;
			return;
		}
		List<string> list = value.Players.Keys.OrderBy((string k) => k).ToList();
		CmbPlayersLocke.ItemsSource = list;
		if (list.Count > 0)
		{
			CmbPlayersLocke.SelectedIndex = 0;
		}
	}

	private async void BtnConectarLocke_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (_firebase == null)
			{
				Aviso("Configura Firebase en ⚙ Ajustes primero.");
				return;
			}
			object selectedItem = CmbLockesLocke.SelectedItem;
			if (!(selectedItem is LockeVista vista))
			{
				Aviso("Selecciona un locke de la lista.");
				return;
			}
			_lockeId = vista.Id;
			_lockeActivo = (await _firebase.ObtenerLockeAsync(_lockeId)) ?? throw new InvalidOperationException("El locke no existe (o las rules no permiten leerlo).");
			try
			{
				var romVinculada = _vinculos.RomDe(_lockeId);
				if (!string.IsNullOrWhiteSpace(romVinculada))
				{
					var entradaVinculada = _biblioteca.FirstOrDefault(r => string.Equals(r.RomPath, romVinculada, StringComparison.OrdinalIgnoreCase));
					if (entradaVinculada is not null)
					{
						// ListaRoms eliminado en el rediseño
						// null = entradaVinculada;
						Log("🔗 ROM vinculada a este locke: " + entradaVinculada.Name);
					}
				}
			}
			catch { }
			_playerTag = (CmbPlayersLocke.SelectedItem as string) ?? _lockeActivo.Players.Keys.FirstOrDefault() ?? "";
			if (_playerTag.Length == 0)
			{
				Aviso("El locke no tiene jugadores todavía.");
				return;
			}
			// TxtLockeConectado eliminado en el rediseño
			// TxtLockeConectado.Text = $"Locke «{_lockeActivo.Nombre}» ({_lockeActivo.Juego}) · jugador {_playerTag}";
			Log("\ud83d\udd12 Locke conectado en vivo: «" + _lockeActivo.Nombre + "» — jugadores: " + string.Join(", ", _lockeActivo.Players.Keys));
			_ctsLocke?.Cancel();
			_ctsLocke = new CancellationTokenSource();
			SuscribirLockeEnVivoAsync(_lockeId, _ctsLocke.Token);
			PlayerLocke miJugador = (_lockeActivo.Players.TryGetValue(_playerTag, out PlayerLocke jugador) ? jugador : null);
			Log(myTeamResumen(miJugador));
			await ActualizarPendientesAsync();
			await ActualizarLockeEnVivo();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Log("ERROR locke: " + ex2.Message);
			Estado("Error conectando al locke", "#EF5350");
		}
	}

	private static string myTeamResumen(PlayerLocke? jugador)
	{
		if (jugador == null)
		{
			return "Jugador no encontrado en el locke.";
		}
		List<MonEquipo> list = jugador.MyTeam?.Slots;
		return (list != null && list.Count > 0) ? ("Equipo de la app: " + string.Join(", ", list.Select((MonEquipo s) => $"#{s.Id} {s.Name} Nv{s.Level}"))) : "La app aún no ha definido myTeam (se inyectará cuando la app lo escriba).";
	}

	private async Task SuscribirLockeEnVivoAsync(string lockeId, CancellationToken ct)
	{
		try
		{
			await _firebase.SuscribirLockeAsync(lockeId, (string evento) =>
			{
				string text = DescribirEvento(evento);
				Log("\ud83d\udd01 " + text);
				bool esCola = evento.Contains("launcherQueue", StringComparison.Ordinal);
				bool tocaMiEquipo = evento.Contains("players/" + _playerTag, StringComparison.Ordinal) && (evento.Contains("inventario", StringComparison.Ordinal) || evento.Contains("myTeam", StringComparison.Ordinal));
				((DispatcherObject)this).Dispatcher.Invoke<Task>((Func<Task>)(async () =>
				{
					if (esCola)
					{
						Estado("¡La app envió una acción a la cola!", "#FFA726");
						await ActualizarPendientesAsync();
					}
					else if (tocaMiEquipo)
					{
						Estado("¡Cambios en tu locke! (cartas/equipo)", "#FFA726");
					}
					if (evento.Contains("\"path\":\"/\"", StringComparison.Ordinal))
					{
						await ActualizarPendientesAsync();
					}
				}));
			}, ct);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			Log("Suscripción en vivo terminada: " + ex3.Message);
		}
	}

	private static string DescribirEvento(string eventoJson)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(eventoJson);
			string text = jsonDocument.RootElement.GetProperty("path").GetString() ?? "/";
			JsonElement property = jsonDocument.RootElement.GetProperty("data");
			return "cambio en " + text + ((property.ValueKind == JsonValueKind.Object) ? "" : $" → {property}");
		}
		catch
		{
			return (eventoJson.Length > 120) ? (eventoJson.Substring(0, 120) + "…") : eventoJson;
		}
	}

	private async void BtnPublicarEquipo_Click(object sender, RoutedEventArgs e)
	{
		var rom = (/*null as RomEntry*/ null) ?? RomDeLaUltimaPartida();
		if (rom is null)
		{
			Aviso("No hay ninguna ROM en la biblioteca.");
			return;
		}
		if (_lockeId.Length == 0 || _playerTag.Length == 0)
		{
			Aviso("Conecta un locke primero (lista Locke → Conectar en vivo).");
			return;
		}
		var save = ResolverSavePath(rom);
		if (save is null)
		{
			Aviso("Esa ROM todavía no tiene partida guardada.");
			return;
		}
		await SincronizarEquipoAsync(save);
		Aviso("Equipo del .sav publicado en la app (Buildeo → Mi Equipo).");
	}

	private async void BtnInyectarEquipo_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (_firebase == null || _lockeActivo == null || _playerTag.Length == 0)
			{
				Aviso("Conecta un locke primero (lista Locke → Conectar en vivo).");
				return;
						}
			var rom = (/*null as RomEntry*/ null) ?? RomDeLaUltimaPartida();
			if (rom is null)
            {
				Aviso("No hay ROMs en la biblioteca con partida conocida.");
				return;
			}
			string save = ResolverSavePath(rom) ?? throw new InvalidOperationException("No encuentro la partida (.sav) de esa ROM.");
			MyTeam equipo = await _firebase.LeerEquipoAsync(_lockeId, _playerTag);
			List<MonEquipo> list = equipo?.Slots;
			if (list == null || list.Count <= 0)
			{
				Aviso("Tu app aún no ha escrito myTeam para " + _playerTag + " en este locke.");
				return;
			}
			MessageBoxResult confirmar = MessageBox.Show(this, $"¿Inyectar el equipo de la app ({equipo.Slots.Count} Pokémon) en la partida?\n\n{save}", "Inyectar equipo de PokeLocke", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (confirmar == MessageBoxResult.Yes)
			{
				SaveFile sav = ResumenPartidaService.Abrir(save);
				List<PKM> inyectados = InyectorEquipo.Inyectar(sav, new PlayerLocke
				{
					PlayerTag = _playerTag,
					MyTeam = equipo
				});
				InyectorEquipo.GuardarSave(sav, save);
				Log($"\ud83d\udce5 Equipo de la app inyectado: {inyectados.Count} Pokémon → {save}");
				Log("   " + string.Join(", ", inyectados.Select((PKM p) => $"#{p.Species} (PID {p.PID})")));
				Estado("Equipo de la app inyectado ✔", "#4CAF50");
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Log("ERROR INYECCIÓN: " + ex2.Message);
			Estado("Error inyectando el equipo", "#EF5350");
		}
	}


	    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button boton) return;
        SonidosUi.Click();
        if (ReferenceEquals(boton, BtnNavLocke))
            MostrarSeccion("locke");
        else if (ReferenceEquals(boton, BtnNavBiblioteca))
            MostrarSeccion("biblioteca");
        else
            MostrarSeccion("inicio");
        foreach (var b in new[] { BtnNavInicio, BtnNavBiblioteca, BtnNavLocke })
            b.Tag = ReferenceEquals(b, boton) ? "activo" : null;
        BtnNavRandomizar.Tag = null;
        BtnNavAjustes.Tag = null;
    }

    private void BtnNavBiblioteca_Click(object sender, RoutedEventArgs e)
    {
        SonidosUi.Click();
        MostrarSeccion("biblioteca");
        foreach (var b in new[] { BtnNavInicio, BtnNavLocke, BtnNavRandomizar, BtnNavAjustes })
            b.Tag = null;
        BtnNavBiblioteca.Tag = "activo";
    }

    private void BtnAbrirBiblioteca_Click(object sender, RoutedEventArgs e)
    {
        SonidosUi.Click();
        MostrarSeccion("biblioteca");
        foreach (var b in new[] { BtnNavInicio, BtnNavLocke, BtnNavRandomizar, BtnNavAjustes })
            b.Tag = null;
        BtnNavBiblioteca.Tag = "activo";
    }

    private void BtnAbrirPanelLocke_Click(object sender, RoutedEventArgs e)
    {
        SonidosUi.Click();
        MostrarSeccion("locke");
        foreach (var b in new[] { BtnNavInicio, BtnNavBiblioteca, BtnNavRandomizar, BtnNavAjustes })
            b.Tag = null;
        BtnNavLocke.Tag = "activo";
    }

    /// <summary>Cambia la sección visible de la ventana principal (inicio/locke/biblioteca/randomizador/ajustes).</summary>
    private void MostrarSeccion(string seccion)
    {
        _seccionActual = seccion;
        PanelInicio.Visibility = seccion == "inicio" ? Visibility.Visible : Visibility.Collapsed;
        PanelLocke.Visibility = seccion == "locke" ? Visibility.Visible : Visibility.Collapsed;
        PanelBiblioteca.Visibility = seccion == "biblioteca" ? Visibility.Visible : Visibility.Collapsed;
        PanelRandomizador.Visibility = seccion == "randomizador" ? Visibility.Visible : Visibility.Collapsed;
        PanelAjustes.Visibility = seccion == "ajustes" ? Visibility.Visible : Visibility.Collapsed;
        TxtTituloSeccion.Text = seccion switch
        {
            "biblioteca" => "Biblioteca de ROMs",
            "randomizador" => "Randomizador",
            "locke" => "Locke en vivo",
            "ajustes" => "Ajustes",
            _ => "Inicio",
        };
        TxtSubtituloSeccion.Text = seccion switch
        {
            "inicio" => "Retoma tu última partida o sincroniza cambios con Pokelocke Android",
            "locke" => "Sincronización en tiempo real con la app móvil Pokelocke Android vía Firebase",
            _ => "",
        };
    }

    private static readonly string[] OrdenSecciones = ["inicio", "biblioteca", "locke", "randomizador", "ajustes"];

    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (EsControlInteractivo(e.OriginalSource as DependencyObject))
        {
            _swipeActivo = false;
            return;
        }
        _swipeInicio = e.GetPosition(this);
        _swipeActivo = true;
    }

    private void MainWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_swipeActivo) return;
        _swipeActivo = false;

        var fin = e.GetPosition(this);
        var dx = fin.X - _swipeInicio.X;
        var dy = fin.Y - _swipeInicio.Y;

        if (Math.Abs(dx) < 90 || Math.Abs(dx) <= Math.Abs(dy) * 1.8)
            return;

        // Deslizar a la izquierda (dx < 0) avanza; a la derecha (dx > 0) retrocede.
        var idx = Array.IndexOf(OrdenSecciones, _seccionActual);
        if (idx < 0) idx = 0;
        var siguiente = dx < 0 ? idx + 1 : idx - 1;
        if (siguiente < 0 || siguiente >= OrdenSecciones.Length)
            return;

        NavegarPorDeslizamiento(OrdenSecciones[siguiente]);
    }

    private void NavegarPorDeslizamiento(string seccion)
    {
        SonidosUi.Click();
        MostrarSeccion(seccion);
        foreach (var b in new[] { BtnNavInicio, BtnNavBiblioteca, BtnNavLocke, BtnNavRandomizar, BtnNavAjustes })
            b.Tag = null;
        var botonActivo = seccion switch
        {
            "biblioteca" => BtnNavBiblioteca,
            "locke" => BtnNavLocke,
            "randomizador" => BtnNavRandomizar,
            "ajustes" => BtnNavAjustes,
            _ => BtnNavInicio,
        };
        botonActivo.Tag = "activo";
    }

    private static bool EsControlInteractivo(DependencyObject? origen)
    {
        while (origen is not null)
        {
            if (origen is ButtonBase || origen is TextBoxBase || origen is PasswordBox ||
                origen is ComboBox || origen is Slider || origen is ScrollBar ||
                origen is Selector || origen is Thumb || origen is RichTextBox)
                return true;
            origen = VisualTreeHelper.GetParent(origen);
        }
        return false;
    }


    private void BtnNavRandomizar_Click(object sender, RoutedEventArgs e) => MostrarRandomizador(null);

    /// <summary>Cambia a la pestaña del randomizador dentro de la ventana (ROM opcional).</summary>
    public void MostrarRandomizador(string? romPath)
    {
        SonidosUi.Click();
        MostrarSeccion("randomizador");
        BtnNavRandomizar.Tag = "activo";
        foreach (var b in new[] { BtnNavInicio, BtnNavBiblioteca, BtnNavLocke, BtnNavAjustes }) b.Tag = null;
        if (romPath != null) CtrlRandomizador.PonerRom(romPath);
    }
    private void BtnAnadirRom_Click(object sender, RoutedEventArgs e) => AnadirRomDesdeDialogo();

	private void AnadirRomALaBiblioteca(RomEntry rom)
	{
		if (_biblioteca.Any((RomEntry r) => string.Equals(r.RomPath, rom.RomPath, StringComparison.OrdinalIgnoreCase)))
		{
			Aviso("Esa ROM ya está en la biblioteca:\n" + rom.RomPath);
			return;
		}
		_biblioteca.Add(rom);
		if (!GuardarBiblioteca())
		{
			CargarBiblioteca();
			ActualizarPortadasInicio();
			return;
		}
		CargarBiblioteca();
		ActualizarPortadasInicio();
		Log($"➕ ROM añadida: {rom.Name} ({rom.ConsoleName})");
		Estado("ROM añadida a la biblioteca", "#4CAF50");
	}

	public void AnadirRomRandomizada(string rutaRom)
	{
		if (File.Exists(rutaRom))
		{
			AnadirRomALaBiblioteca(new RomEntry
			{
				Name = Path.GetFileNameWithoutExtension(rutaRom),
				ConsoleName = ConsolaDetector.Detectar(rutaRom),
				RomPath = rutaRom,
				SavePath = (Path.GetDirectoryName(rutaRom) ?? ""),
				LastPlayed = "Never"
			});
		}
	}

	private void ActualizarVideoFondo()
	{
		try
		{
			string videoActual = TemaFondo.VideoActual;
			if (videoActual == null)
			{
				FondoVideo.Stop();
				FondoVideo.Visibility = Visibility.Collapsed;
				return;
			}
			FondoVideo.Visibility = Visibility.Visible;
			FondoVideo.Source = new Uri(videoActual, UriKind.Absolute);
			FondoVideo.Volume = (TemaFondo.VideoConSonido ? ((double)TemaFondo.VideoVolumen / 100.0) : 0.0);
			FondoVideo.IsMuted = !TemaFondo.VideoConSonido;
			FondoVideo.Play();
		}
		catch (Exception ex)
		{
			Log("Fondo de vídeo: " + ex.Message);
		}
	}

	private void FondoVideo_MediaEnded(object sender, RoutedEventArgs e)
	{
		try
		{
			FondoVideo.Position = TimeSpan.Zero;
			FondoVideo.Play();
		}
		catch
		{
		}
	}

	private void FondoVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
	{
		Log("No se pudo reproducir el vídeo de fondo: " + (e.ErrorException?.Message ?? "error"));
		FondoVideo.Visibility = Visibility.Collapsed;
	}

	private void BtnAjustes_Click(object sender, RoutedEventArgs e)
	{
		SonidosUi.Click();
		BtnNavAjustes.Tag = "activo";
		Button[] array = new Button[4] { BtnNavInicio, BtnNavBiblioteca, BtnNavLocke, BtnNavRandomizar };
		foreach (Button button in array)
		{
			button.Tag = null;
		}
		MostrarSeccion("ajustes");
	}

	private void CtrlAjustes_CambiosAplicados()
	{
		TemaAcento.Apply(_ajustes.TemaIndice);
		TemaFondo.Apply(_ajustes.FondoIndice);
		SonidosUi.Activados = _ajustes.Sonidos;
		EmuladorSwitch.Cargar(_ajustes.SwitchRuta, _ajustes.SwitchArgumentos);
		ActualizarVideoFondo();
		if (string.IsNullOrWhiteSpace(_ajustes.VendorDir))
		{
			_ajustes.VendorDir = Ajustes.BuscarVendorDir();
		}
		CargarBiblioteca();
		ActualizarPortadasInicio();
		ConectarFirebaseAsync();
		ActualizarVideoFondo();
	}

	private void PrepararServicioCola()
	{
		if (_firebase == null)
		{
			return;
		}
		_firebase.Registro = (string msg) =>
		{
			((DispatcherObject)this).Dispatcher.Invoke((Action)(() =>
			{
				Log(msg);
			}));
		};
		_cola = new ColaLauncherService(_firebase, _ajustes.DatosDir, EmuladorAbierto)
		{
			AlProgresar = (string msg) =>
			{
				((DispatcherObject)this).Dispatcher.Invoke((Action)(() =>
				{
					Log(msg);
				}));
			},
			PedirObjetivo = (string titulo, List<ObjetivoPkm> objetivos) => ((DispatcherObject)this).Dispatcher.Invoke<UbicacionPkm>((Func<UbicacionPkm>)(() => DialogoObjetivo(titulo, objetivos))),
			PedirStat = (string titulo) => ((DispatcherObject)this).Dispatcher.Invoke<string>((Func<string>)(() => DialogoStat(titulo))),
			PedirDosStats = (string titulo) => ((DispatcherObject)this).Dispatcher.Invoke<List<string>>((Func<List<string>>)(() => DialogoDosStats(titulo))),
			PedirMovimiento = (string titulo, ushort maxMov) => ((DispatcherObject)this).Dispatcher.Invoke<ushort?>((Func<ushort?>)(() => DialogoMovimiento(titulo, maxMov))),
			PedirMovimientoReemplazo = (string titulo, IReadOnlyList<ushort> actuales) => ((DispatcherObject)this).Dispatcher.Invoke<int?>((Func<int?>)(() => DialogoMovimientoReemplazo(titulo, actuales))),
			PedirMovimientoDeLista = (string titulo, IReadOnlyList<ushort> opciones) => ((DispatcherObject)this).Dispatcher.Invoke<ushort?>((Func<ushort?>)(() => DialogoMovimientoDeLista(titulo, opciones))),
		PedirObjetoDeLista = (string titulo, IReadOnlyList<(int Id, string Nombre)> opciones) => ((DispatcherObject)this).Dispatcher.Invoke<int?>((Func<int?>)(() => DialogoObjetoDeLista(titulo, opciones))),
			PedirEdicionStats = (string titulo, PKM pkm, int maxEvs) => ((DispatcherObject)this).Dispatcher.Invoke<(int[], int[])?>((Func<(int[], int[])?>)(() => DialogoEdicionStats(titulo, pkm, maxEvs))),
			PedirMote = (string titulo) => ((DispatcherObject)this).Dispatcher.Invoke<string>((Func<string>)(() => DialogoMote(titulo))),
			PedirHabilidad = (string titulo, IReadOnlyList<(int Id, string Nombre)> opciones) => ((DispatcherObject)this).Dispatcher.Invoke<int?>((Func<int?>)(() => DialogoHabilidad(titulo, opciones))),
			PedirRepartoEvs = (string titulo) => ((DispatcherObject)this).Dispatcher.Invoke<Dictionary<string, int>>((Func<Dictionary<string, int>>)(() => DialogoRepartoEvs(titulo))),
			PedirNivel = (string titulo) => ((DispatcherObject)this).Dispatcher.Invoke<int?>((Func<int?>)(() => DialogoNivel(titulo))),
			PedirPokemonAnil = (string titulo, IReadOnlyList<(string Etiqueta, Pokemon Mon)> lista) => ((DispatcherObject)this).Dispatcher.Invoke<int?>((Func<int?>)(() => DialogoPokemonAnil(titulo, lista))),
			PedirStatAnil = (string titulo) => ((DispatcherObject)this).Dispatcher.Invoke<int?>((Func<int?>)(() => DialogoStatAnil(titulo))),
			PedirHabilidadAnil = (string titulo, IReadOnlyList<string> opciones) => ((DispatcherObject)this).Dispatcher.Invoke<string>((Func<string>)(() => DialogoTextoDeLista(titulo, "Habilidad (escribe para filtrar):", opciones))),
			PedirMovimientoAnil = (string titulo, IReadOnlyList<string> opciones) => ((DispatcherObject)this).Dispatcher.Invoke<string>((Func<string>)(() => DialogoTextoDeLista(titulo, "Movimiento (escribe para filtrar):", opciones))),
			PedirEdicionStatsAnil = (string titulo, int[] ivs, int[] evs, int maxEvs) => ((DispatcherObject)this).Dispatcher.Invoke<(int[], int[])?>((Func<(int[], int[])?>)(() => DialogoEdicionStatsAnil(titulo, ivs, evs, maxEvs)))
		};
	}

	private static bool EmuladorAbierto()
	{
		return EmulatorService.EmuladorAbierto() != null;
	}

    /// <summary>Aplica las acciones pendientes de la cola (botón «Aplicar cambios al .sav»).</summary>
    private async void BtnAplicarCambiosSav_Click(object sender, RoutedEventArgs e)
    {
        await AplicarPendientesCoreAsync(conConfirmacion: false);
    }

    /// <summary>Navega a la página de biblioteca (botón «Ver todas ›»).</summary>
    private void BtnVerTodasBiblioteca_Click(object sender, RoutedEventArgs e)
    {
        SonidosUi.Click();
        MostrarSeccion("biblioteca");
        foreach (var b in new[] { BtnNavInicio, BtnNavLocke, BtnNavRandomizar, BtnNavAjustes })
            b.Tag = null;
        BtnNavBiblioteca.Tag = "activo";
    }

    /// <summary>Juega la ROM guardada en el Tag del botón de fila reciente.</summary>
    private async void BtnJugarReciente_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not RomEntry rom) return;
        await JugarRomAsync(rom);
    }

    private void BtnJugarReciente1_Click(object sender, RoutedEventArgs e) => BtnJugarReciente_Click(sender, e);
    private void BtnJugarReciente2_Click(object sender, RoutedEventArgs e) => BtnJugarReciente_Click(sender, e);
    private void BtnJugarReciente3_Click(object sender, RoutedEventArgs e) => BtnJugarReciente_Click(sender, e);

    /// <summary>Limpia el registro de la página «Locke en vivo».</summary>
    private void BtnLimpiarLogLocke_Click(object sender, RoutedEventArgs e)
    {
        TxtLogLocke.Document.Blocks.Clear();
    }
}