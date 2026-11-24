using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeLobby.Emulator;

/// <summary>
/// Ajustes del <c>qt-config.ini</c> de Azahar que el launcher original aplica antes de
/// lanzar un juego de 3DS (ver INFORME-PokeLobby.md §5): activar el stub GDB, activar las
/// texturas personalizadas y desactivar el auto-update del emulador, junto a sus claves
/// «\default» de Qt. Ruta por defecto: %APPDATA%\azahar\config\qt-config.ini.
/// </summary>
public sealed class AzaharConfig
{
    private static readonly (string Seccion, string Clave, string Valor)[] AjustesLauncher =
    [
        ("[Debugging]", "use_gdbstub", "true"),
        ("[Debugging]", "use_gdbstub\\default", "false"),
        ("[Utility]", "custom_textures", "true"),
        ("[Utility]", "custom_textures\\default", "false"),
        ("[Miscellaneous]", "check_for_update_on_start", "false"),
        ("[Miscellaneous]", "check_for_update_on_start\\default", "false"),
    ];

    private readonly string _ruta;

    public string Ruta => _ruta;
    public bool Existe => File.Exists(_ruta);

    public static string RutaPorDefecto => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "azahar", "config", "qt-config.ini");

    public AzaharConfig(string? ruta = null) => _ruta = ruta ?? RutaPorDefecto;

    /// <summary>Aplica los ajustes del launcher, preservando el resto del INI (fusión no destructiva).</summary>
    public void AplicarAjustesLauncher()
    {
        var lineas = new List<string>();
        if (Existe)
            lineas.AddRange(File.ReadAllLines(_ruta));

        var pendientes = new List<(string Seccion, string Clave, string Valor)>(AjustesLauncher);
        var seccionActual = "";
        for (var i = 0; i < lineas.Count && pendientes.Count > 0; i++)
        {
            var limpia = lineas[i].Trim();
            if (limpia.StartsWith('[') && limpia.EndsWith(']'))
            {
                seccionActual = limpia;
                continue;
            }
            var igual = limpia.IndexOf('=');
            if (igual <= 0) continue;
            var clave = limpia[..igual].Trim();
            for (var j = pendientes.Count - 1; j >= 0; j--)
            {
                if (pendientes[j].Seccion != seccionActual || pendientes[j].Clave != clave) continue;
                lineas[i] = clave + "=" + pendientes[j].Valor;
                pendientes.RemoveAt(j);
            }
        }

        // Claves nuevas: dentro de su sección si ya existe, o sección nueva al final.
        foreach (var grupo in pendientes.GroupBy(p => p.Seccion).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var claves = grupo.Select(p => $"{p.Clave}={p.Valor}").ToList();
            var indice = lineas.FindIndex(l => l.Trim().Equals(grupo.Key, StringComparison.Ordinal));
            if (indice < 0)
            {
                lineas.Add(grupo.Key);
                lineas.AddRange(claves);
            }
            else
            {
                lineas.InsertRange(indice + 1, claves);
            }
        }

        var dir = Path.GetDirectoryName(_ruta);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temporal = _ruta + ".tmp";
        File.WriteAllLines(temporal, lineas);
        File.Move(temporal, _ruta, overwrite: true);
    }

    /// <summary>Lee un valor del INI (sección con corchetes, p. ej. «[Debugging]»); null si no está.</summary>
    public string? Leer(string seccion, string clave)
    {
        if (!Existe) return null;
        var seccionActual = "";
        foreach (var linea in File.ReadLines(_ruta))
        {
            var limpia = linea.Trim();
            if (limpia.StartsWith('[') && limpia.EndsWith(']'))
            {
                seccionActual = limpia;
                continue;
            }
            var igual = limpia.IndexOf('=');
            if (igual <= 0) continue;
            if (seccionActual == seccion && limpia[..igual].Trim() == clave)
                return limpia[(igual + 1)..].Trim();
        }
        return null;
    }
}
