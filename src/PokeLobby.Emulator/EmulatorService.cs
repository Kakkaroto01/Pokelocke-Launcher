using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeLobby.Emulator;

public enum ConsolaJuego
{
    Gba,
    Ds,
    TresDs,
    Switch,
    Anil,
}

/// <summary>Emulador externo configurable para Switch (emulador_switch.json del original).</summary>
public sealed record EmuladorExterno(string Ruta, string Argumentos);

/// <summary>
/// Lanzador de emuladores, réplica del <c>EmulatorService</c> + <c>EmuladorExternoService</c>
/// originales: resuelve los emuladores de la carpeta <c>vendor\</c> (mGBA→GBA, melonDS→DS,
/// azahar→3DS), lanza con la ROM como argumento (plantilla «{rom}» para el externo de Switch),
/// adopta un emulador ya abierto y configura Azahar antes de lanzar 3DS.
/// </summary>
public sealed class EmulatorService
{
    public const string PlantillaRom = "{rom}";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _vendorDir;

    public EmulatorService(string vendorDir) => _vendorDir = vendorDir;

    public static string Carpeta(ConsolaJuego consola) => consola switch
    {
        ConsolaJuego.Gba => "mGBA",
        ConsolaJuego.Ds => "melonDS",
        ConsolaJuego.TresDs => "azahar",
        _ => throw new NotSupportedException("Switch usa un emulador externo (emulador_switch.json)"),
    };

    public static string NombreExe(ConsolaJuego consola) => consola switch
    {
        ConsolaJuego.Gba => "mGBA.exe",
        ConsolaJuego.Ds => "melonDS.exe",
        ConsolaJuego.TresDs => "azahar.exe",
        _ => throw new NotSupportedException("Switch usa un emulador externo (emulador_switch.json)"),
    };

    public static string RutaEmuladorSwitchJson => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLockeLauncher", "datos", "emulador_switch.json");

    /// <summary>Ruta antigua (launcher original): solo se LEE como respaldo, nunca se escribe.</summary>
    public static string RutaEmuladorSwitchJsonAntigua => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PokeLobby", "Launcher", "emulador_switch.json");

    /// <summary>Ruta del ejecutable del emulador para cada consola (null si Switch sin externo).</summary>
    public string? RutaExe(ConsolaJuego consola)
        => consola == ConsolaJuego.Switch ? EmuladorSwitch()?.Ruta
         : Path.Combine(_vendorDir, Carpeta(consola), NombreExe(consola));

    /// <summary>Lee la configuración del emulador externo de Switch (si existe).</summary>
    public EmuladorExterno? EmuladorSwitch()
    {
        var ruta = File.Exists(RutaEmuladorSwitchJson) ? RutaEmuladorSwitchJson
                 : File.Exists(RutaEmuladorSwitchJsonAntigua) ? RutaEmuladorSwitchJsonAntigua
                 : null;
        if (ruta is null) return null;
        var datos = JsonSerializer.Deserialize<JsonEmuladorSwitch>(File.ReadAllText(ruta), Json);
        return datos is null ? null : new EmuladorExterno(datos.Ruta ?? "", datos.Argumentos ?? $"\"{PlantillaRom}\"");
    }

    public void GuardarEmuladorSwitch(EmuladorExterno emulador)
    {
        var dir = Path.GetDirectoryName(RutaEmuladorSwitchJson);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(RutaEmuladorSwitchJson, JsonSerializer.Serialize(
            new JsonEmuladorSwitch { Ruta = emulador.Ruta, Argumentos = emulador.Argumentos }, Json));
    }

    /// <summary>
    /// ¿Hay un emulador del vendor abierto? Se compara la RUTA del ejecutable (como el
    /// AdoptarEmuladorAbierto del original) y se excluye el propio proceso del launcher.
    /// </summary>
    public static Process? EmuladorAbierto()
    {
        foreach (var nombre in (string[])["mGBA", "melonDS", "azahar"])
        {
            Process[] procesos;
            try { procesos = Process.GetProcessesByName(nombre); }
            catch { continue; }
            foreach (var p in procesos)
            {
                if (p.Id == Environment.ProcessId) continue;
                return p;   // un emulador real abierto
            }
        }
        return null;
    }

    /// <summary>
    /// Lanza la ROM en el emulador de la consola. SIEMPRE arranca una instancia nueva con la ROM
    /// como argumento: el original solo DETECTA un emulador ya abierto al iniciar (para el
    /// estado), nunca lo adopta al pulsar JUGAR (adoptarlo abría el emulador sin la ROM).
    /// </summary>
    public Process Lanzar(ConsolaJuego consola, string rutaRom, bool adoptarAbierto = false)
    {
        string exe;
        string argumentos;

        // Fangame con .exe propio (Pokémon Añil): no hay emulador, se lanza el juego directamente.
        if (consola == ConsolaJuego.Anil)
        {
            if (!File.Exists(rutaRom))
                throw new FileNotFoundException($"no se lanza: no existe el ejecutable \"{rutaRom}\"");
            return Process.Start(new ProcessStartInfo(rutaRom)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(rutaRom) ?? Environment.CurrentDirectory,
            }) ?? throw new InvalidOperationException($"Process.Start devolvió null para \"{rutaRom}\"");
        }

        if (consola == ConsolaJuego.Switch)
        {
            var externo = EmuladorSwitch()
                ?? throw new FileNotFoundException("no se lanza: Switch sin emulador configurado en Ajustes (emulador_switch.json)");
            exe = externo.Ruta;
            argumentos = PokeLobby.PokeLocke.LanzamientoEmulador.Argumentos("Switch", rutaRom, externo.Argumentos);
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                throw new FileNotFoundException($"no se lanza: no existe el emulador externo \"{exe}\"");
        }
        else
        {
            exe = RutaExe(consola)!;
            if (!File.Exists(exe))
                throw new FileNotFoundException($"no se lanza: no existe el emulador \"{exe}\" (falta la carpeta vendor/)");
            // melonDS recibe argv por ANSI: con acentos no abre la ROM. Se pasa la ruta 8.3 y se
            // enlaza el save que melonDS derivará (POKEMO~3.sav) al save REAL, para no duplicarlo.
            var romEmulador = RutasWindows.RutaCompatible(rutaRom);
            if (!string.Equals(romEmulador, rutaRom, StringComparison.Ordinal))
                RutasWindows.EnlazarSaveCorto(romEmulador,
                    Path.Combine(Path.GetDirectoryName(rutaRom) ?? "", Path.GetFileNameWithoutExtension(rutaRom) + ".sav"));
            argumentos = PokeLobby.PokeLocke.LanzamientoEmulador.Argumentos(consola.ToString(), romEmulador);
        }

        if (!File.Exists(rutaRom))
            throw new FileNotFoundException($"no se lanza: no existe la ROM \"{rutaRom}\"");

        // Antes de lanzar 3DS, el original aplica los ajustes de Azahar (gdbstub, texturas, sin update).
        if (consola == ConsolaJuego.TresDs)
            new AzaharConfig().AplicarAjustesLauncher();

        return Process.Start(new ProcessStartInfo(exe)
        {
            Arguments = argumentos,
            UseShellExecute = false,

        }) ?? throw new InvalidOperationException($"Process.Start devolvió null para \"{exe}\"");
    }

    private sealed class JsonEmuladorSwitch
    {
        [JsonPropertyName("ruta")]
        public string? Ruta { get; set; }

        [JsonPropertyName("argumentos")]
        public string? Argumentos { get; set; }
    }
}