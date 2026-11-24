using System;
using System.Text;
using System.Text.RegularExpressions;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Cuenta PokeLocke: Firebase Authentication usa email+contraseña, pero el jugador se identifica
/// con un NOMBRE DE USUARIO. Internamente se usa «usuario@pokelocke.com» (evidencia REA: la app
/// Android crea las cuentas con el usuario en minúsculas + ese dominio).
/// </summary>
public static partial class CuentaPokelocke
{
    /// <summary>Dominio interno de las cuentas de la app.</summary>
    public const string Dominio = "pokelocke.com";

    /// <summary>Sufijo completo del correo interno.</summary>
    public const string Sufijo = "@" + Dominio;

    /// <summary>Longitud mínima y máxima del nombre de usuario.</summary>
    public const int UsuarioMin = 3;
    public const int UsuarioMax = 32;

    /// <summary>Caracteres admitidos: letras (sin acentos), dígitos, punto, guion y guion bajo.</summary>
    [GeneratedRegex("^[a-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronUsuario();

    /// <summary>
    /// Normaliza lo que escribe el jugador al usuario canónico: recorta espacios, quita el
    /// sufijo si lo escribió y pasa a minúsculas. Si pega un email completo (compatibilidad),
    /// toma la parte anterior a la @.
    /// </summary>
    public static string Normalizar(string? entrada)
    {
        var texto = (entrada ?? "").Trim();
        var arroba = texto.IndexOf('@');
        if (arroba > 0) texto = texto[..arroba];
        texto = texto.Trim().ToLowerInvariant();
        // quitar acentos y espacios internos (evita inconsistencias al teclear)
        texto = QuitarAcentos(texto);   // los espacios internos se conservan: la validación los rechaza
        return texto;
    }

    /// <summary>Correo interno que se envía a Firebase a partir del nombre de usuario.</summary>
    public static string EmailInterno(string? usuario) => Normalizar(usuario) + Sufijo;

    /// <summary>Nombre de usuario a partir del email interno (para mostrar en la UI).</summary>
    public static string UsuarioDesdeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "";
        var texto = email.Trim();
        var arroba = texto.IndexOf('@');
        return arroba > 0 ? texto[..arroba] : texto;
    }

    /// <summary>Indica si el usuario es válido; si no, devuelve el motivo en español.</summary>
    public static bool EsValido(string? entrada, out string motivo)
    {
        var usuario = Normalizar(entrada);
        if (usuario.Length == 0)
        {
            motivo = "Escribe tu nombre de usuario.";
            return false;
        }
        if (usuario.Length < UsuarioMin)
        {
            motivo = $"El usuario debe tener al menos {UsuarioMin} caracteres.";
            return false;
        }
        if (usuario.Length > UsuarioMax)
        {
            motivo = $"El usuario no puede pasar de {UsuarioMax} caracteres.";
            return false;
        }
        if (!PatronUsuario().IsMatch(usuario))
        {
            motivo = "El usuario solo admite letras sin acentos, números, punto, guion y guion bajo.";
            return false;
        }
        motivo = "";
        return true;
    }

    /// <summary>Valida la contraseña (mínimo que exige Firebase Email/Password).</summary>
    public static bool ContrasenaValida(string? contrasena, out string motivo)
    {
        if (string.IsNullOrEmpty(contrasena))
        {
            motivo = "Escribe tu contraseña.";
            return false;
        }
        if (contrasena.Length < 6)
        {
            motivo = "La contraseña debe tener al menos 6 caracteres.";
            return false;
        }
        motivo = "";
        return true;
    }

    private static string QuitarAcentos(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
