//
// Which palette group a placeable type belongs to. Objects group by faction — the vanilla
// sides from their owner house, mod types by the mod's faction slots (Tiberian Factions
// reuses GoodGuy/BadGuy for GDI/Nod, with the ts- name prefix marking the Tiberian Sun
// wave) — plus Civilian and Misc. Harvestables group as Resources wherever they live:
// ore/gems/tiberium overlays, the ore mine and blossom trees on the terrain tab.
using System;
using System.Collections.Generic;
using MobiusEditor.Model;

namespace MobiusEditor.Shell
{
    public static class PaletteGrouping
    {
        private static readonly HashSet<string> AlliedHouses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Spain", "Greece", "England", "Germany", "France", "Turkey" };
        private static readonly HashSet<string> SovietHouses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "USSR", "Ukraine" };

        private static readonly List<string> Order = new List<string>
        {
            "Resources", "Walls", "Decorations",
            "Allies", "Soviets", "GDI (mod)", "Nod (mod)", "TS GDI (mod)", "TS Nod (mod)",
            "Civilian", "Terrain", "Misc",
        };

        public static string GroupOf(object type)
        {
            switch (type)
            {
                case OverlayType o:
                    return o.IsResource ? "Resources" : o.IsWall ? "Walls" : "Decorations";
                case TerrainType t:
                    return t.Name.Contains("mine", StringComparison.OrdinalIgnoreCase)
                        || t.Name.Contains("split", StringComparison.OrdinalIgnoreCase) ? "Resources" : "Terrain";
                case BuildingType b:
                    return Faction(b.OwnerHouse, b.ModSource, b.Name);
                case UnitType u:
                    return Faction(u.OwnerHouse, u.ModSource, u.Name);
                case InfantryType i:
                    return Faction(i.OwnerHouse, i.ModSource, i.Name);
                default:
                    return "";
            }
        }

        public static int OrderOf(string group)
        {
            int index = Order.IndexOf(group);
            return index >= 0 ? index : Order.Count;
        }

        private static string Faction(string owner, string modSource, string name)
        {
            if (!string.IsNullOrEmpty(modSource))
            {
                bool ts = name != null && name.StartsWith("ts", StringComparison.OrdinalIgnoreCase);
                if ("GoodGuy".Equals(owner, StringComparison.OrdinalIgnoreCase)) return ts ? "TS GDI (mod)" : "GDI (mod)";
                if ("BadGuy".Equals(owner, StringComparison.OrdinalIgnoreCase)) return ts ? "TS Nod (mod)" : "Nod (mod)";
                return modSource;
            }
            if (owner != null && AlliedHouses.Contains(owner)) return "Allies";
            if (owner != null && SovietHouses.Contains(owner)) return "Soviets";
            if ("Neutral".Equals(owner, StringComparison.OrdinalIgnoreCase)) return "Civilian";
            return "Misc";
        }
    }
}
