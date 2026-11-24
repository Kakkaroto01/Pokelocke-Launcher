namespace PokeLobby.Client.Dtos;

// ─────────────────────────────────────────────────────────────────────────────
// DTOs recreados a partir de los modelos del launcher original
// (PokeLobbyLauncher.Models.*, ver INFORME-PokeLobby.md §4) y del contrato
// verificado en vivo. Las respuestas se deserializan en modo Web
// (camelCase + insensible a mayúsculas), igual que el cliente original.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Sala (RoomInfo del launcher original: Id, JuegoId, Nombre).</summary>
public sealed class RoomInfo
{
    public int Id { get; set; }
    public int JuegoId { get; set; }
    public string Nombre { get; set; } = "";
}

/// <summary>Notificación push del hub (evento «CartaRecibida»).</summary>
public sealed class CartaRecibidaNotificacion
{
    public int SalaId { get; set; }
    public string SalaNombre { get; set; } = "";
    public string RemitenteNombre { get; set; } = "";
    public string CartaTitulo { get; set; } = "";
    public string CartaEmoji { get; set; } = "";
}

/// <summary>Entrada del historial de entregas (HistorialEntrega).</summary>
public sealed class HistorialEntrega
{
    public string Nombre { get; set; } = "";
    public DateTimeOffset? EntregadoEn { get; set; }
}

/// <summary>Item del inventario del jugador en una sala (InventoryItem).</summary>
public sealed class InventoryItem
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int? PkHexItemId { get; set; }
    public string Nombre { get; set; } = "";
    public string? NombreSprite { get; set; }
    public string? ImagenUrl { get; set; }
    public int Cantidad { get; set; }
    public string Bolsillo { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string? Rareza { get; set; }
    public bool RequiereLauncher { get; set; }
    public string? ResultadoNombre { get; set; }
    public int? EspecieId { get; set; }
    public int? EggResultId { get; set; }
    public int? TradeId { get; set; }
    public uint? RevivePid { get; set; }
    public int? MtNumber { get; set; }
    public int? MtResultId { get; set; }
    public uint? AplicarEfectoPid { get; set; }
    public string? AplicarEfectoTipo { get; set; }
    public int? AplicarEfectoValor { get; set; }
}

/// <summary>Oferta de un intercambio (TradeOferta).</summary>
public sealed class TradeOferta
{
    public uint Pid { get; set; }
    public string? Species { get; set; }
    public int Level { get; set; }
    public string? Nickname { get; set; }
    public string? Nature { get; set; }
    public string? Ability { get; set; }
    public int? AbilityNumber { get; set; }
    public string? Gender { get; set; }
    public string? HeldItem { get; set; }
    public int IV_HP { get; set; }
    public int IV_Atk { get; set; }
    public int IV_Def { get; set; }
    public int IV_SpA { get; set; }
    public int IV_SpD { get; set; }
    public int IV_Spe { get; set; }
    public int EV_HP { get; set; }
    public int EV_Atk { get; set; }
    public int EV_Def { get; set; }
    public int EV_SpA { get; set; }
    public int EV_SpD { get; set; }
    public int EV_Spe { get; set; }
    public string? Move1 { get; set; }
    public string? Move2 { get; set; }
    public string? Move3 { get; set; }
    public string? Move4 { get; set; }
}

/// <summary>Estado de un intercambio (TradeDetail: Id, Estado, MiOferta, OfertaRecibida).</summary>
public sealed class TradeDetail
{
    public int Id { get; set; }
    public string Estado { get; set; } = "";
    public TradeOferta? MiOferta { get; set; }
    public TradeOferta? OfertaRecibida { get; set; }
}

/// <summary>Resultado de un huevo misterioso (EggPokemonResult).</summary>
public sealed class EggPokemonResult
{
    public int Id { get; set; }
    public string? Species { get; set; }
    public int Level { get; set; }
    public string? Nature { get; set; }
    public string? Ability { get; set; }
    public string? Gender { get; set; }
    public bool EsRandomizer { get; set; }
    public int IvHp { get; set; }
    public int IvAtk { get; set; }
    public int IvDef { get; set; }
    public int IvSpa { get; set; }
    public int IvSpd { get; set; }
    public int IvSpe { get; set; }
    public int EvHp { get; set; }
    public int EvAtk { get; set; }
    public int EvDef { get; set; }
    public int EvSpa { get; set; }
    public int EvSpd { get; set; }
    public int EvSpe { get; set; }
    public string? Move1 { get; set; }
    public string? Move2 { get; set; }
    public string? Move3 { get; set; }
    public string? Move4 { get; set; }
}

/// <summary>
/// Payload de POST /api/saves/showdown. Los 20 campos son los observados en la
/// plantilla de log del envío del launcher original (ExportToApi,
/// ver INFORME-PokeLobby.md §3.7). «Export» es el equipo en texto Showdown.
/// </summary>
public sealed class ShowdownUpload
{
    public string Export { get; set; } = "";
    public uint[] PartyPids { get; set; } = [];
    public string[] PartyRutas { get; set; } = [];
    public int[] PartyAbilityNumbers { get; set; } = [];
    public uint TrainerId { get; set; }
    public int Medallas { get; set; }
    public int Monedas { get; set; }
    public int SalaId { get; set; }
    public int Boxes { get; set; }
    public int Muertos { get; set; }
    public bool HaGanadoLiga { get; set; }
    public int[] TotemItemIds { get; set; } = [];
    public string JuegoVersion { get; set; } = "";
    public string Entrenador { get; set; } = "";
    public long SegundosJugados { get; set; }
    public string SaveHash { get; set; } = "";
    public string LauncherVersion { get; set; } = "";
    public string RandomizerVersion { get; set; } = "";
    public int RevisionPartida { get; set; }
    public object? Senales { get; set; }
}
