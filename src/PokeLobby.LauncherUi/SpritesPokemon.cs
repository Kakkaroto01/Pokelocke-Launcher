using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PokeLobby.LauncherUi;

/// <summary>
/// Caché de sprites de Pokémon para el selector (misma fuente que la app Android: PokeAPI).
/// Si no hay red o el sprite no existe, devuelve null y el hueco se queda sin imagen.
/// </summary>
internal static class SpritesPokemon
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<int, BitmapImage?> Cache = [];
    private static readonly Dictionary<int, Task<BitmapImage?>> EnCurso = [];

    public static Task<BitmapImage?> ObtenerAsync(int species)
    {
        if (Cache.TryGetValue(species, out var enCache)) return Task.FromResult(enCache);
        if (EnCurso.TryGetValue(species, out var tarea)) return tarea;

        var nueva = DescargarAsync(species);
        EnCurso[species] = nueva;
        return nueva;
    }

    private static async Task<BitmapImage?> DescargarAsync(int species)
    {
        BitmapImage? resultado = null;
        try
        {
            var url = $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{species}.png";
            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            resultado = bmp;
        }
        catch
        {
            resultado = null;   // sin red o sprite ausente: hueco sin imagen
        }
        Cache[species] = resultado;
        EnCurso.Remove(species);
        return resultado;
    }
}
