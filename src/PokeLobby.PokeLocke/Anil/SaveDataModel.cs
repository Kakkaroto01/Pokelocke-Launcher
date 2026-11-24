// ────────────────────────────────────────────────────────────────────────────────
// Adaptado de AnilHeX (https://github.com/DylanVZ5/AnilHeX), de DylanVZ5.
// Licencia original: CC BY-NC-SA 4.0. Esta adaptación mantiene la misma licencia y
// se usa en un proyecto personal no comercial. Cambios: se ha eliminado la parte de
// interfaz (WinForms) y se ha integrado el parseo/escritura del .rxdata en el launcher.
// ────────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;

public class ItemSlot 
{
    public string InternalName { get; set; }
    public string Name { get; set; }
    public int Quantity { get; set; }
    public int Pocket { get; set; }
}

public class SaveDataModel
{
    public object RootData { get; set; } 
    
    // --- CACHÉ DE VELOCIDAD EXTREMA ---
    public System.Collections.IList RubyPartyList { get; set; } 
    public System.Collections.IList RubyBoxesList { get; set; } 
    public RubyObject RubyTrainer { get; set; }
    public RubyObject RubyBag { get; set; }
    public RubyObject RubyPokedex { get; set; }
    // ----------------------------------

    public List<Pokemon> Party { get; set; } = new List<Pokemon>();
    public List<PCBox> Boxes { get; set; } = new List<PCBox>();
    public List<ItemSlot> Bag { get; set; } = new List<ItemSlot>();
}

public class PCBox
{
    public int BoxIndex { get; set; }
    public string Name { get; set; }
    public Pokemon[] Slots { get; set; } = new Pokemon[30]; 
}
