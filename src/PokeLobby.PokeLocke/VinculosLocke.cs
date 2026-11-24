using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PokeLobby.PokeLocke;

/// <summary>
/// Vínculo explícito locke de la app ↔ ROM de la biblioteca. Se guarda en
/// <c>&lt;datos&gt;\vinculos.json</c> con el formato <c>{ lockeId: rutaRom }</c>.
/// Sirve para que la cola se aplique SIEMPRE a la ROM correcta, aunque el jugador
/// tenga otra seleccionada (evita el «El locke es de X pero la partida es de B»).
/// </summary>
public sealed class VinculosLocke
{
    private readonly Dictionary<string, string> _mapa = new(StringComparer.OrdinalIgnoreCase);

    public static string RutaDe(string datosDir) => Path.Combine(datosDir, "vinculos.json");

    public static VinculosLocke Cargar(string datosDir)
    {
        var vinculos = new VinculosLocke();
        try
        {
            var ruta = RutaDe(datosDir);
            if (!File.Exists(ruta)) return vinculos;
            var nodo = JsonNode.Parse(File.ReadAllText(ruta)) as JsonObject;
            if (nodo is null) return vinculos;
            foreach (var (lockeId, valor) in nodo)
            {
                // Formato nuevo: texto (ruta de la ROM). El antiguo (lista de objetos) se ignora.
                if (valor is JsonValue v && v.TryGetValue<string>(out var rutaRom) && !string.IsNullOrWhiteSpace(rutaRom))
                    vinculos._mapa[lockeId] = rutaRom;
            }
        }
        catch
        {
            // archivo corrupto: se empieza vacío
        }
        return vinculos;
    }

    public void Guardar(string datosDir)
    {
        Directory.CreateDirectory(datosDir);
        var nodo = new JsonObject();
        foreach (var (lockeId, rutaRom) in _mapa) nodo[lockeId] = rutaRom;
        File.WriteAllText(RutaDe(datosDir), nodo.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public string? RomDe(string? lockeId)
        => !string.IsNullOrWhiteSpace(lockeId) && _mapa.TryGetValue(lockeId!, out var ruta) ? ruta : null;

    public void Asignar(string lockeId, string rutaRom) => _mapa[lockeId] = rutaRom;

    public void Quitar(string lockeId) => _mapa.Remove(lockeId);

    /// <summary>Locke vinculado a esta ROM (o null si ninguna).</summary>
    public string? LockeDeRom(string rutaRom)
        => _mapa.FirstOrDefault(kv => string.Equals(kv.Value, rutaRom, StringComparison.OrdinalIgnoreCase)).Key;

    /// <summary>Quita cualquier vínculo que apunte a esta ROM.</summary>
    public void QuitarPorRom(string rutaRom)
    {
        foreach (var clave in _mapa.Where(kv => string.Equals(kv.Value, rutaRom, StringComparison.OrdinalIgnoreCase))
                                   .Select(kv => kv.Key).ToList())
            _mapa.Remove(clave);
    }

    public int Cuantos => _mapa.Count;
}
