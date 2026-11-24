using System;
using System.IO;
using System.Linq;
using PokeLobby.PokeLocke;
using PokeLobby.Randomizer;
using Xunit;

namespace PokeLobby.Tests;

/// <summary>
/// Ajustes por defecto del randomizador: el launcher original (REA, v0.1.43) nace con TODAS
/// las claves del Universal Pokémon Randomizer ZX, y su UI las muestra agrupadas. Sin esta
/// plantilla la ventana aparecía vacía.
/// </summary>
public class RandomizerDefectosTests
{
    [Fact]
    public void Nuevo_AjustesTraeTodasLasClavesDelUprzx()
    {
        var ajustes = new RandomizerSettings();
        var valores = ajustes.Valores;

        Assert.True(valores.Count >= 190, $"esperaba ~199 claves, hay {valores.Count}");

        // grupos del UPRZX presentes
        var grupos = valores.Keys.Select(k => k.Split('.')[0]).Distinct().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var grupo in new[]
                 {
                     "types", "abilities", "basestats", "starters", "statics", "trades", "evolutions",
                     "movedata", "movesets", "wild", "trainers", "totems", "tmmoves", "tmcompat",
                     "tutormoves", "tutorcompat", "fielditems", "shops", "pickup", "misc",
                 })
        {
            Assert.Contains(grupo, grupos);
        }
    }

    [Theory] // defectos exactos del original (no todos son false/0)
    [InlineData("types.mod", "UNCHANGED")]
    [InlineData("abilities.mod", "UNCHANGED")]
    [InlineData("trainers.additionalBossCount", "1")]
    [InlineData("trainers.additionalImportantCount", "1")]
    [InlineData("trainers.additionalRegularCount", "1")]
    [InlineData("trainers.movesetForceGoodDamagingPercent", "25")]
    [InlineData("movesets.guaranteedLevel1Min", "2")]
    [InlineData("evolutions.tradeLevel", "37")]
    [InlineData("basestats.expCurveSpeed", "MEDIUM_FAST")]
    [InlineData("basestats.expCurveTarget", "LEGENDARIES")]
    public void Defectos_CoincidenConElOriginal(string clave, string esperado)
        => Assert.Equal(esperado, new RandomizerSettings().Get(clave));

    [Fact] // cargar un .rnqs SOBRESCRIBE los defectos y conserva claves desconocidas
    public void Cargar_SobrescribeDefectosYPreservaDesconocidas()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "tmp-tests", "rando-" + Guid.NewGuid().ToString("N") + ".rnqs");
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, """
            types.mod=RANDOM
            trainers.additionalBossCount=4
            clave.desconocida=valor
            """);

        var ajustes = RandomizerSettings.Cargar(ruta);

        Assert.Equal("RANDOM", ajustes.Get("types.mod"));
        Assert.Equal("4", ajustes.Get("trainers.additionalBossCount"));
        Assert.Equal("valor", ajustes.Get("clave.desconocida"));
        // las claves no presentes en el archivo conservan su defecto
        Assert.Equal("UNCHANGED", ajustes.Get("abilities.mod"));
        Assert.True(ajustes.Valores.Count >= 190);

        File.Delete(ruta);
    }

    [Fact] // texto properties completo (lo que consume el puente de Java)
    public void ToPropertiesText_ContieneTodasLasClaves()
    {
        var texto = new RandomizerSettings().ToPropertiesText();
        var lineas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lineas.Length >= 190);
        Assert.Contains("types.mod=UNCHANGED", lineas);
        Assert.Contains("wild.mod=UNCHANGED", lineas);
    }

    [Fact] // candidatas de carpeta de datos incluyen el perfil del usuario (CFA suele bloquear Documentos)
    public void CandidatasDeDatos_IncluyenPerfilYExe()
    {
        var candidatas = PermisosCarpeta.CandidatasDeDatos(null, @"C:\app");
        Assert.Contains(Path.Combine(@"C:\app", "datos"), candidatas);
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(perfil))
            Assert.Contains(candidatas, c => c.StartsWith(perfil, StringComparison.OrdinalIgnoreCase));
    }
}
