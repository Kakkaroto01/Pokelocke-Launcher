using System;
using System.Collections.Generic;
using System.IO;
using PokeLobby.PokeLocke;
using Xunit;

namespace PokeLobby.Tests;

/// <summary>
/// Cuenta PokeLocke: el jugador usa un NOMBRE DE USUARIO y Firebase recibe «usuario@pokelocke.com».
/// </summary>
public class CuentaPokelockeTests
{
    [Theory]
    [InlineData("Entrenador123", "entrenador123@pokelocke.com")]
    [InlineData("  Entrenador123  ", "entrenador123@pokelocke.com")]
    [InlineData("entrenador123", "entrenador123@pokelocke.com")]
    [InlineData("Entrenador123@pokelocke.com", "entrenador123@pokelocke.com")]
    [InlineData("ENTRENADOR123@otrositio.com", "entrenador123@pokelocke.com")]
    [InlineData("José.Pérez", "jose.perez@pokelocke.com")]
    public void EmailInterno_UsaSufijoYNormaliza(string entrada, string esperado)
        => Assert.Equal(esperado, CuentaPokelocke.EmailInterno(entrada));

    [Theory]
    [InlineData("Entrenador123@pokelocke.com", "Entrenador123")]
    [InlineData("ana", "ana")]
    [InlineData("", "")]
    public void UsuarioDesdeEmail_QuitaDominio(string email, string esperado)
        => Assert.Equal(esperado, CuentaPokelocke.UsuarioDesdeEmail(email));

    [Theory]
    [InlineData("abc", true)]
    [InlineData("a.b_c-d", true)]
    [InlineData("", false)]
    [InlineData("ab", false)]
    [InlineData("con espacio", false)]
    [InlineData("con@dentro", true)]   // se toma la parte anterior a la @
    [InlineData("ñandú", true)]        // los acentos se normalizan
    public void EsValido_ValidaFormato(string usuario, bool esperado)
        => Assert.Equal(esperado, CuentaPokelocke.EsValido(usuario, out _));

    [Fact]
    public void EsValido_RechazaDemasiadoLargo()
    {
        var largo = new string('a', CuentaPokelocke.UsuarioMax + 1);
        Assert.False(CuentaPokelocke.EsValido(largo, out var motivo));
        Assert.Contains("pasar de", motivo);
    }

    [Fact]
    public void EsValido_ExplicaElMotivo()
    {
        Assert.False(CuentaPokelocke.EsValido("", out var vacio));
        Assert.False(string.IsNullOrWhiteSpace(vacio));
        Assert.False(CuentaPokelocke.EsValido("a b", out var espacio));
        Assert.False(string.IsNullOrWhiteSpace(espacio));
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("12345", false)]
    [InlineData("", false)]
    public void ContrasenaValida_MinimoSeis(string contrasena, bool esperado)
        => Assert.Equal(esperado, CuentaPokelocke.ContrasenaValida(contrasena, out _));
}

/// <summary>Carátulas: carpeta de datos, detección por nombre y utilidades.</summary>
public class CaratulasTests
{
    [Theory]
    [InlineData("Pokemon HeartGold (USA)", "heartgold")]
    [InlineData("5584 - Pokemon - White Version (USA) (En,Fr,De,Es,It)", "white")]
    [InlineData("Pokémon Rubí Omega", "rubi omega")]
    public void Normalizar_QuitaRuido(string entrada, string esperado)
        => Assert.Equal(esperado, Caratulas.Normalizar(entrada));

    [Fact]
    public void Buscar_EncuentraPorJuegoYPorNombre()
    {
        var datos = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "covers-" + Guid.NewGuid().ToString("N"));
        var covers = Caratulas.CarpetaDe(datos);
        Directory.CreateDirectory(covers);
        File.WriteAllText(Path.Combine(covers, "HeartGold.png"), "x");
        File.WriteAllText(Path.Combine(covers, "White.jpg"), "x");

        Assert.Equal(Path.Combine(covers, "HeartGold.png"), Caratulas.Buscar(datos, ["HeartGold"]));
        Assert.Equal(Path.Combine(covers, "White.jpg"),
            Caratulas.Buscar(datos, ["5584 - Pokemon - White Version (USA)"]));
        Assert.Null(Caratulas.Buscar(datos, ["Emerald"]));

        Directory.Delete(datos, recursive: true);
    }

    [Fact]
    public void EsImagen_SoloExtensionesAdmitidas()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, "a.png");
        var txt = Path.Combine(dir, "b.txt");
        File.WriteAllText(png, "x");
        File.WriteAllText(txt, "x");
        Assert.True(Caratulas.EsImagen(png));
        Assert.False(Caratulas.EsImagen(txt));
        Assert.False(Caratulas.EsImagen(null));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void SembrarDesde_CopiaLasQueFaltanYRespetaLasPropias()
    {
        var raiz = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "siembra-" + Guid.NewGuid().ToString("N"));
        var origen = Path.Combine(raiz, "incluidas");
        var datos = Path.Combine(raiz, "datos");
        Directory.CreateDirectory(origen);
        Directory.CreateDirectory(datos);

        File.WriteAllText(Path.Combine(origen, "HeartGold.png"), "incluida");
        File.WriteAllText(Path.Combine(origen, "Añil.png"), "incluida");
        File.WriteAllText(Path.Combine(origen, "notas.txt"), "no es imagen");

        // El jugador ya tenía una carátula propia con el mismo nombre: debe ganar la suya.
        var covers = Caratulas.CarpetaDe(datos);
        Directory.CreateDirectory(covers);
        File.WriteAllText(Path.Combine(covers, "HeartGold.png"), "propia");

        Assert.Equal(1, Caratulas.SembrarDesde(origen, datos));
        Assert.Equal("propia", File.ReadAllText(Path.Combine(covers, "HeartGold.png")));
        Assert.True(File.Exists(Path.Combine(covers, "Añil.png")));
        Assert.False(File.Exists(Path.Combine(covers, "notas.txt")));

        // La segunda vez ya no hay nada que copiar.
        Assert.Equal(0, Caratulas.SembrarDesde(origen, datos));

        Directory.Delete(raiz, recursive: true);
    }

    [Fact]
    public void SembrarDesde_SinCarpetaIncluida_NoFalla()
    {
        var datos = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "sin-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, Caratulas.SembrarDesde(Path.Combine(datos, "no-existe"), datos));
    }

    [Fact]
    public void Buscar_EquivalenciasInglesEspanol()
    {
        var datos = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "alias-" + Guid.NewGuid().ToString("N"));
        var covers = Caratulas.CarpetaDe(datos);
        Directory.CreateDirectory(covers);
        File.WriteAllText(Path.Combine(covers, "Rojo Fuego.png"), "x");
        File.WriteAllText(Path.Combine(covers, "Blanco 2.png"), "x");
        File.WriteAllText(Path.Combine(covers, "X.png"), "x");

        Assert.Equal(Path.Combine(covers, "Rojo Fuego.png"), Caratulas.Buscar(datos, ["FireRed"]));
        Assert.Equal(Path.Combine(covers, "Blanco 2.png"), Caratulas.Buscar(datos, ["White 2"]));
        Assert.Equal(Path.Combine(covers, "X.png"), Caratulas.Buscar(datos, ["X"]));
        Assert.Null(Caratulas.Buscar(datos, ["Z"]));

        Directory.Delete(datos, recursive: true);
    }

    [Fact]
    public void Buscar_EncuentraAnilAunqueElArchivoLlevaTilde()
    {
        var datos = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "anil-" + Guid.NewGuid().ToString("N"));
        var covers = Caratulas.CarpetaDe(datos);
        Directory.CreateDirectory(covers);
        File.WriteAllText(Path.Combine(covers, "Añil.png"), "x");

        Assert.Equal(Path.Combine(covers, "Añil.png"), Caratulas.Buscar(datos, ["Añil"]));
        Assert.Equal(Path.Combine(covers, "Añil.png"), Caratulas.Buscar(datos, ["Anil"]));

        Directory.Delete(datos, recursive: true);
    }
}

/// <summary>Detección del juego por el nombre del archivo de ROM.</summary>
public class DetectorJuegoRomTests
{
    [Theory]
    [InlineData(@"E:\juegos\Pokemon HeartGold_randomized_20261011-140310.nds", "HeartGold")]
    [InlineData(@"C:\x\5584 - Pokemon - White Version (USA).nds", "White")]
    [InlineData(@"C:\x\Pokemon Black 2 (USA).nds", "Black 2")]
    [InlineData(@"C:\x\Pokemon Blanco 2.nds", "White 2")]
    [InlineData(@"C:\x\Pokemon Negro 2.nds", "Black 2")]
    [InlineData(@"C:\x\Pokemon - Emerald Version.gba", "Emerald")]
    [InlineData(@"C:\x\Pokemon Y (USA).3ds", "Y")]
    [InlineData(@"C:\x\Pokemon Añil.gba", "Añil")]
    [InlineData(@"C:\x\Pokemon Anil.gba", "Añil")]
    [InlineData(@"C:\x\juego-desconocido.nds", "")]
    public void DetectaTitulos(string ruta, string esperado)
        => Assert.Equal(esperado, DetectorJuegoRom.Detectar(ruta));
}

/// <summary>Localización de partidas: nombre base, carpeta configurada y no asociar partidas ajenas.</summary>
public class LocalizadorPartidasTests
{
    private static string CarpetaTemporal()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "saves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Buscar_PorMismoNombreBase()
    {
        var dir = CarpetaTemporal();
        var rom = Path.Combine(dir, "Pokemon White.nds");
        var sav = Path.Combine(dir, "Pokemon White.sav");
        File.WriteAllText(rom, "rom");
        File.WriteAllText(sav, "save");

        Assert.Equal(sav, LocalizadorPartidas.Buscar(rom, null));
        File.Delete(sav);
        Assert.Null(LocalizadorPartidas.Buscar(rom, null));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Buscar_CarpetaConfiguradaTienePrioridad()
    {
        var dir = CarpetaTemporal();
        var otra = Path.Combine(dir, "partidas");
        Directory.CreateDirectory(otra);
        var rom = Path.Combine(dir, "Pokemon White.nds");
        File.WriteAllText(rom, "rom");
        var sav = Path.Combine(otra, "Pokemon White.sav");
        File.WriteAllText(sav, "save");

        Assert.Equal(sav, LocalizadorPartidas.Buscar(rom, otra));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Buscar_NoAsociaLaPartidaDeOtroJuego()
    {
        var dir = CarpetaTemporal();
        var romA = Path.Combine(dir, "Pokemon White.nds");
        var romB = Path.Combine(dir, "Pokemon Black.nds");
        File.WriteAllText(romA, "romA");
        File.WriteAllText(romB, "romB");
        File.WriteAllText(Path.Combine(dir, "Pokemon Black.sav"), "save");

        Assert.Null(LocalizadorPartidas.Buscar(romA, null));
        Assert.Equal(Path.Combine(dir, "Pokemon Black.sav"), LocalizadorPartidas.Buscar(romB, null));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void RutaPrevista_UsaNombreBaseYConvencionSav()
    {
        var dir = CarpetaTemporal();
        var rom = Path.Combine(dir, "Pokemon HeartGold.nds");
        Assert.Equal(Path.Combine(dir, "Pokemon HeartGold.sav"),
            LocalizadorPartidas.RutaPrevista(rom, null));
        Directory.Delete(dir, recursive: true);
    }
}
