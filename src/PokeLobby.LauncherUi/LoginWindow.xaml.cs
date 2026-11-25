using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Diálogo de inicio de sesión / creación de cuenta, con el diseño de
/// <c>Rediseño/login.html</c>. La lógica de autenticación la inyecta MainWindow
/// mediante <see cref="IniciarSesionAsync"/> y <see cref="CrearCuentaAsync"/>.
/// </summary>
public partial class LoginWindow : Window
{
    /// <summary>Devuelve null si fue bien o el mensaje de error a mostrar.</summary>
    public Func<string, string, Task<string?>>? IniciarSesionAsync { get; set; }

    /// <summary>Devuelve null si fue bien o el mensaje de error a mostrar.</summary>
    public Func<string, string, Task<string?>>? CrearCuentaAsync { get; set; }

    public bool SesionIniciada { get; private set; }

    private bool _verPassword;

    public LoginWindow(string usuarioInicial)
    {
        InitializeComponent();
        TxtUsuario.Text = usuarioInicial;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(TxtUsuario.Text)) TxtUsuario.Focus();
            else TxtPassword.Focus();
        };
    }

    private string PasswordActual =>
        _verPassword ? TxtPasswordVisible.Text : TxtPassword.Password;

    private void Cerrar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void VerPassword_Click(object sender, RoutedEventArgs e)
    {
        _verPassword = !_verPassword;
        if (_verPassword)
        {
            TxtPasswordVisible.Text = TxtPassword.Password;
            TxtPassword.Visibility = Visibility.Collapsed;
            TxtPasswordVisible.Visibility = Visibility.Visible;
            BtnVerPassword.Content = "🙈";
            TxtPasswordVisible.Focus();
            TxtPasswordVisible.CaretIndex = TxtPasswordVisible.Text.Length;
        }
        else
        {
            TxtPassword.Password = TxtPasswordVisible.Text;
            TxtPasswordVisible.Visibility = Visibility.Collapsed;
            TxtPassword.Visibility = Visibility.Visible;
            BtnVerPassword.Content = "👁";
            TxtPassword.Focus();
        }
    }

    private void Password_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_verPassword) TxtPasswordVisible.Text = TxtPassword.Password;
    }

    private void PasswordVisible_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_verPassword) TxtPassword.Password = TxtPasswordVisible.Text;
    }

    private void Credenciales_Changed(object sender, TextChangedEventArgs e)
    {
        OcultarError();
    }

    private void MostrarError(string mensaje)
    {
        TxtError.Text = mensaje;
        TxtError.Visibility = Visibility.Visible;
    }

    private void OcultarError()
    {
        TxtError.Text = "";
        TxtError.Visibility = Visibility.Collapsed;
    }

    private void Entrar_Click(object sender, RoutedEventArgs e) => _ = EjecutarAsync(crear: false);

    private void Crear_Click(object sender, RoutedEventArgs e) => _ = EjecutarAsync(crear: true);

    private async Task EjecutarAsync(bool crear)
    {
        OcultarError();
        var accion = crear ? CrearCuentaAsync : IniciarSesionAsync;
        if (accion is null) return;

        BtnEntrar.IsEnabled = false;
        BtnCrear.IsEnabled = false;
        try
        {
            var error = await accion(TxtUsuario.Text, PasswordActual);
            if (error is null)
            {
                SesionIniciada = true;
                DialogResult = true;
                Close();
            }
            else
            {
                MostrarError(error);
            }
        }
        catch (Exception ex)
        {
            MostrarError(ex.Message);
        }
        finally
        {
            BtnEntrar.IsEnabled = true;
            BtnCrear.IsEnabled = true;
        }
    }
}
