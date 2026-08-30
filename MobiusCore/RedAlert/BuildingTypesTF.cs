//
// Tiberian Factions: the mod's TD buildings as RA engine types.
// IDs mirror the mod's StructType enum values (redalert/defines.h, TD range starts at 87).
// Footprints/occupy masks transcribed from the mod's bdata.cpp occupy lists; power,
// capturable, storage, bibs and owners from the mod's shipped CCDATA/rules.ini.
// TDSTEAL renders with the RA gap sprite and TDFBNK with pbox, matching the mod's
// Image= aliases; everything else has its own TD art in RA_STRUCTURES.XML.
using MobiusEditor.Model;

namespace MobiusEditor.RedAlert
{
    public static partial class BuildingTypes
    {
        public static readonly BuildingType TdObelisk = new BuildingType(87, "tdobli", "TEXT_STRUCTURE_TITLE_NOD_OBELISK", 0, 150, false, 1, 2, "0 1", "BadGuy");
        public static readonly BuildingType TdPower = new BuildingType(88, "tdnuke", "TEXT_STRUCTURE_TITLE_GDI_POWER_PLANT", 100, 0, true, 2, 2, null, "GoodGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdAdvPower = new BuildingType(89, "tdnuk2", "TEXT_STRUCTURE_TITLE_GDI_ADV_POWER_PLANT", 200, 0, true, 2, 2, null, "GoodGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdBarracks = new BuildingType(90, "tdpyle", "TEXT_STRUCTURE_TITLE_GDI_BARRACKS", 0, 20, true, 2, 2, null, "GoodGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdStorage = new BuildingType(91, "tdsilo", "TEXT_STRUCTURE_TITLE_GDI_SILO", 0, 10, 1500, true, 2, 1, "10", "GoodGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdGuardTower = new BuildingType(92, "tdgtwr", "TEXT_STRUCTURE_TITLE_GDI_GUARD_TOWER", 0, 10, false, 1, 1, null, "GoodGuy");
        public static readonly BuildingType TdAdvGuardTower = new BuildingType(93, "tdatwr", "TEXT_STRUCTURE_TITLE_GDI_ADV_GUARD_TOWER", 0, 20, false, 1, 2, "0 1", "GoodGuy");
        public static readonly BuildingType TdTurret = new BuildingType(94, "tdgun", "TEXT_STRUCTURE_TITLE_NOD_TURRET", 0, 20, false, 1, 1, null, "BadGuy", BuildingTypeFlag.Turret);
        public static readonly BuildingType TdSAM = new BuildingType(95, "tdsam", "TEXT_STRUCTURE_TITLE_NOD_SAM_SITE", 0, 20, false, 2, 1, null, "BadGuy", BuildingTypeFlag.Turret);
        public static readonly BuildingType TdHand = new BuildingType(96, "tdhand", "TEXT_STRUCTURE_TITLE_NOD_HAND_OF_NOD", 0, 20, true, 2, 3, "00 11 01", "BadGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdHelipad = new BuildingType(97, "tdhpad", "TEXT_STRUCTURE_TITLE_GDI_HELIPAD", 0, 10, true, 2, 2, null, "GoodGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdRepair = new BuildingType(98, "tdfix", "TEXT_STRUCTURE_TITLE_GDI_REPAIR_FACILITY", 0, 30, true, 3, 3, "010 111 010", "GoodGuy", BuildingTypeFlag.Bib, Globals.ZOrderPaved);
        public static readonly BuildingType TdCommand = new BuildingType(99, "tdhq", "TEXT_STRUCTURE_TITLE_GDI_COMM_CENTER", 0, 40, true, 2, 2, null, "GoodGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdWeapon = new BuildingType(100, "tdweap", "TEXT_STRUCTURE_TITLE_GDI_WEAPONS_FACTORY", 0, 30, true, 3, 3, "000 111 111", "GoodGuy", "tdweap2", null, BuildingTypeFlag.Bib);
        public static readonly BuildingType TdAirStrip = new BuildingType(101, "tdafld", "TEXT_STRUCTURE_TITLE_NOD_AIRFIELD", 0, 30, true, 4, 2, null, "BadGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdConst = new BuildingType(102, "tdfact", "TEXT_STRUCTURE_TITLE_GDI_CONSTRUCTION_YARD", 0, 0, true, 3, 2, null, "GoodGuy", BuildingTypeFlag.Bib | BuildingTypeFlag.Factory);
        public static readonly BuildingType TdRefinery = new BuildingType(103, "tdproc", "TEXT_STRUCTURE_TITLE_GDI_REFINERY", 0, 40, 2000, true, 3, 3, "010 111 000", "GoodGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdEye = new BuildingType(104, "tdeye", "TEXT_STRUCTURE_TITLE_GDI_ADV_COMM_CENTER", 0, 200, false, 2, 2, null, "GoodGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdTemple = new BuildingType(105, "tdtmpl", "TEXT_STRUCTURE_TITLE_NOD_TEMPLE_OF_NOD", 0, 150, false, 3, 3, "000 111 111", "BadGuy", BuildingTypeFlag.Bib);
        public static readonly BuildingType TdBlossom = new BuildingType(106, "tdblossom", "TEXT_PROP_TITLE_BLOSSOM_TREE", 0, 0, false, 1, 1, null, "Neutral", BuildingTypeFlag.NoRemap);
        public static readonly BuildingType TdNavalYard = new BuildingType(107, "tdgyard", "TEXT_STRUCTURE_RA_SYRD", 0, 30, true, 3, 3, null, "GoodGuy");
        public static readonly BuildingType TdSubPen = new BuildingType(108, "tdnpen", "TEXT_STRUCTURE_RA_SPEN", 0, 30, true, 3, 3, null, "BadGuy");
        public static readonly BuildingType TdGdiAirfield = new BuildingType(109, "tdgafld", "TEXT_STRUCTURE_TITLE_NOD_AIRFIELD", 0, 30, true, 3, 2, null, "GoodGuy");
        public static readonly BuildingType TdStealthGen = new BuildingType(110, "tdsteal", "TEXT_STRUCTURE_TF_TDSTEAL", 0, 100, 0, true, 1, 2, "0 1", "BadGuy", null, 13, "gap", BuildingTypeFlag.None, Globals.ZOrderDefault);
        public static readonly BuildingType TdFlameBunker = new BuildingType(111, "tdfbnk", "TEXT_STRUCTURE_TF_TDFBNK", 0, 15, false, 1, 1, null, "BadGuy", null, "pbox", BuildingTypeFlag.None);
    }
}
