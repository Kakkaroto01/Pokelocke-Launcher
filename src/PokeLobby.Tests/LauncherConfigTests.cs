using System;
using System.IO;
using PokeLobby.PokeLocke;
using Xunit;

namespace PokeLobby.Tests;

/// <summary>
/// PROMPT/REA — funciones nuevas del launcher replicadas del original:
/// detección de consola (AddRomDialog.DetectConsole) y emulador externo de Switch
/// (EmuladorExternoService: ruta + argumentos "{rom}", y bloqueo del propio launcher).
/// </summary>
public class LauncherConfigTests
{
    [Theory] // extensiones → consola, igual que el original
    [InlineData("juego.gba", "GBA")]
    [InlineData("juego.gb", "GBA")]
    [InlineData("juego.gbc", "GBA")]
    [InlineData("juego.nds", "DS")]
    [InlineData("juego.3ds", "3DS")]
    [InlineData("juego.cia", "3DS")]
    [InlineData("juego.cci", "3DS")]
    [InlineData("juego.cxi", "3DS")]
    [InlineData("juego.xci", "Switch")]
    [InlineData("juego.xcz", "Switch")]
    [InlineData("juego.nsp", "Switch")]
    [InlineData("juego.nsz", "Switch")]
    [InlineData("desconocido.bin", "GBA")]
    public void DetectaConsolaPorExtension(string archivo, string esperada)
        => Assert.Equal(esperada, ConsolaDetector.Detectar(archivo));

    [Fact] // todas las extensiones del filtro se detectan a sí mismas sin caer en el fallback
    public void ExtensionesAdmitidas_Cubiertas()
    {
        Assert.Equal(13, ConsolaDetector.Extensiones.Length);   // + .exe (Pokémon Añil)
        foreach (var ext in ConsolaDetector.Extensiones)
        {
            var consola = ConsolaDetector.Detectar("rom" + ext);
            Assert.True(consola is "GBA" or "DS" or "3DS" or "Switch" or "Añil");
        }
    }

    [Fact] // argumentos por defecto "{rom}" y sustitución entrecomillada
    public void ArgumentosSwitch_SustituyenRom()
    {
        Assert.Equal("\"{rom}\"", EmuladorSwitchConfig.ArgumentosPorDefecto);
        // la plantilla por defecto ya trae comillas: se sustituye tal cual (sin duplicarlas)
        Assert.Equal("\"C:\\juegos\\mi rom.xci\"",
            EmuladorSwitchConfig.ConstruirArgumentos(@"C:\juegos\mi rom.xci"));
        // plantilla con más argumentos
        Assert.Equal("-f C:\\x.nsp",
            EmuladorSwitchConfig.ConstruirArgumentos(@"C:\x.nsp", "-f {rom}"));
        // plantilla SIN {rom}: se añade la ruta entrecomillada al final
        Assert.Equal("--fullscreen \"C:\\x.nsp\"",
            EmuladorSwitchConfig.ConstruirArgumentos(@"C:\x.nsp", "--fullscreen"));
    }

    [Fact] // argumentos vacíos → plantilla por defecto
    public void ArgumentosVacios_UsanPorDefecto()
    {
        Assert.Equal(EmuladorSwitchConfig.ArgumentosPorDefecto,
            EmuladorSwitchConfig.NormalizarArgumentos(""));
        Assert.Equal(EmuladorSwitchConfig.ArgumentosPorDefecto,
            EmuladorSwitchConfig.NormalizarArgumentos("   "));
    }

    [Theory] // no se puede elegir el propio launcher como emulador
    [InlineData(@"C:\apps\PokeLockeLauncher.exe")]
    [InlineData(@"C:\apps\PokeLobbyLauncher.exe")]
    [InlineData(@"C:\apps\LockeLauncher.exe")]
    [InlineData(@"C:\apps\pokelockelauncher.EXE")]
    public void LauncherComoEmulador_SeRechaza(string ruta)
    {
        Assert.True(EmuladorSwitchConfig.EsElLauncher(ruta));
        Assert.Equal("", EmuladorSwitchConfig.NormalizarRuta(ruta));
    }

    [Theory]
    [InlineData(@"C:\emu\yuzu.exe")]
    [InlineData(@"C:\emu\ryujinx.exe")]
    public void EmuladorReal_SeAcepta(string ruta)
    {
        Assert.False(EmuladorSwitchConfig.EsElLauncher(ruta));
        Assert.Equal(ruta, EmuladorSwitchConfig.NormalizarRuta(ruta));
    }

    [Theory] // argumentos de lanzamiento: DS/GBA/3DS llevan la ROM entrecomillada como único argumento
    [InlineData("DS", @"E:\juegos\Pokemon HeartGold.nds", "\"E:\\juegos\\Pokemon HeartGold.nds\"")]
    [InlineData("GBA", @"C:\x\Pokemon.gba", "\"C:\\x\\Pokemon.gba\"")]
    [InlineData("3DS", @"C:\x\Pokemon.3ds", "\"C:\\x\\Pokemon.3ds\"")]
    public void ArgumentosLanzamiento_RomEntrecomillada(string consola, string rom, string esperado)
        => Assert.Equal(esperado, LanzamientoEmulador.Argumentos(consola, rom));

    [Fact] // Switch usa la plantilla del emulador externo (no la ruta suelta)
    public void ArgumentosLanzamiento_SwitchUsaPlantilla()
    {
        Assert.Equal("\"C:\\x\\juego.xci\"",
            LanzamientoEmulador.Argumentos("Switch", @"C:\x\juego.xci", "\"{rom}\""));
        Assert.Equal("-f C:\\x\\juego.nsp",
            LanzamientoEmulador.Argumentos("Switch", @"C:\x\juego.nsp", "-f {rom}"));
    }

    [Fact] // carpeta escribible vs. no escribible (evita el «Access to the path … denied» sin explicación)
    public void PermisosCarpeta_DetectaEscritura()
    {
        var carpeta = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "permisos-" + Guid.NewGuid().ToString("N"));
        Assert.True(PermisosCarpeta.PuedeEscribir(carpeta));
        Assert.True(Directory.Exists(carpeta));
        Assert.False(PermisosCarpeta.PuedeEscribir(""));
        Assert.False(PermisosCarpeta.PuedeEscribir(null));
        Assert.False(PermisosCarpeta.PuedeEscribir(@"Z:\unidad-que-no-existe\sub"));
        Assert.True(PermisosCarpeta.PuedeEscribirEnArchivo(Path.Combine(carpeta, "library.json")));
        Directory.Delete(carpeta, recursive: true);
    }

    [Fact] // auto-reparación: elige la primera candidata escribible (y null si ninguna lo es)
    public void ElegirEscribible_EligePrimeraValida()
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "elegir-" + Guid.NewGuid().ToString("N"));
        var buena = Path.Combine(baseDir, "buena");
        var elegida = PermisosCarpeta.ElegirEscribible(@"Z:\no-existe\sub", buena, Path.Combine(baseDir, "otra"));
        Assert.Equal(buena, elegida);

        var candidatas = PermisosCarpeta.CandidatasDeDatos(null, baseDir);
        Assert.Contains(Path.Combine(baseDir, "datos"), candidatas);
        Assert.Contains(candidatas, c => c.Contains("PokeLockeLauncher"));

        Directory.Delete(baseDir, recursive: true);
    }

    [Fact] // Configurado exige que el ejecutable exista
    public void Configurado_RequiereEjecutableExistente()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "emu-" + Guid.NewGuid().ToString("N") + ".exe");
        Assert.False(EmuladorSwitchConfig.Configurado(ruta));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, "stub");
        Assert.True(EmuladorSwitchConfig.Configurado(ruta));
        File.Delete(ruta);
        Assert.False(EmuladorSwitchConfig.Configurado(ruta));
    }
}
