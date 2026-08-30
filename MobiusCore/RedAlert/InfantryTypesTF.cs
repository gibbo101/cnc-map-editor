//
// Tiberian Factions: the mod's TD infantry as RA engine types.
// IDs mirror the mod's InfantryType enum values (redalert/defines.h, TD range starts
// at 26). Owners from the mod's CCDATA/rules.ini; TDE6 (Engineer) is unarmed.
using MobiusEditor.Model;

namespace MobiusEditor.RedAlert
{
    public static partial class InfantryTypes
    {
        public static readonly InfantryType TdE1 = new InfantryType(26, "tde1", "TEXT_UNIT_TITLE_GDI_MINIGUNNER", "GoodGuy", UnitTypeFlag.Armed);
        public static readonly InfantryType TdE2 = new InfantryType(27, "tde2", "TEXT_UNIT_TITLE_GDI_GRENADIER", "GoodGuy", UnitTypeFlag.Armed);
        public static readonly InfantryType TdE3 = new InfantryType(28, "tde3", "TEXT_UNIT_TITLE_GDI_ROCKET_SOLDIER", "GoodGuy", UnitTypeFlag.Armed);
        public static readonly InfantryType TdE4 = new InfantryType(29, "tde4", "TEXT_UNIT_TITLE_NOD_FLAMETHROWER", "BadGuy", UnitTypeFlag.Armed);
        public static readonly InfantryType TdE5 = new InfantryType(30, "tde5", "TEXT_UNIT_TITLE_NOD_CHEM_WARRIOR", "BadGuy", UnitTypeFlag.Armed);
        public static readonly InfantryType TdE6 = new InfantryType(31, "tde6", "TEXT_UNIT_TITLE_GDI_ENGINEER", "GoodGuy");
        public static readonly InfantryType TdCommando = new InfantryType(32, "tdrmbo", "TEXT_UNIT_TITLE_GDI_COMMANDO", "GoodGuy", UnitTypeFlag.Armed);
    }
}
