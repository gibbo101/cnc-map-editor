//
// Tiberian Factions: the mod's TD vehicles, aircraft and vessels as RA engine types.
// IDs mirror the mod's enum values (redalert/defines.h: vehicles from 22, aircraft
// from 7, vessels from 7). Frame usages follow the TD art actually shipped in the
// mod's RA_UNITS.XML (64-frame body+turret sprites for the turreted tanks, separate
// TDBOATTUR turret for the gunboat); owners from the mod's CCDATA/rules.ini.
// The MLRS/MSAM sprite cross-wiring is baked into the mod's art names, so each
// type simply fetches its own name.
using MobiusEditor.Model;

namespace MobiusEditor.RedAlert
{
    public static partial class UnitTypes
    {
        // Vehicles (UNIT_TDMCV = 22 onward).
        public static readonly UnitType TdMCV = new VehicleType(22, "tdmcv", "TEXT_UNIT_TITLE_GDI_MCV", "GoodGuy", FrameUsage.Frames32Full, UnitTypeFlag.BuildingRemap);
        public static readonly UnitType TdHarvester = new VehicleType(23, "tdharv", "TEXT_UNIT_TITLE_GDI_HARVESTER", "GoodGuy", FrameUsage.Frames32Full, UnitTypeFlag.Harvester | UnitTypeFlag.BuildingRemap);
        public static readonly UnitType TdMTank = new VehicleType(24, "tdmtnk", "TEXT_UNIT_TITLE_GDI_MED_TANK", "GoodGuy", FrameUsage.Frames32Full, FrameUsage.Frames32Full, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdLTank = new VehicleType(25, "tdltnk", "TEXT_UNIT_TITLE_NOD_LIGHT_TANK", "BadGuy", FrameUsage.Frames32Full, FrameUsage.Frames32Full, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdHTank = new VehicleType(26, "tdhtnk", "TEXT_UNIT_TITLE_GDI_MAMMOTH_TANK", "GoodGuy", FrameUsage.Frames32Full, FrameUsage.Frames32Full, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdFTank = new VehicleType(27, "tdftnk", "TEXT_UNIT_TITLE_NOD_FLAME_TANK", "BadGuy", FrameUsage.Frames32Full, UnitTypeFlag.Armed);
        public static readonly UnitType TdBike = new VehicleType(28, "tdbike", "TEXT_UNIT_TITLE_NOD_RECON_BIKE", "BadGuy", FrameUsage.Frames32Full, UnitTypeFlag.Armed);
        public static readonly UnitType TdJeep = new VehicleType(29, "tdjeep", "TEXT_UNIT_TITLE_GDI_HUMVEE", "GoodGuy", FrameUsage.Frames32Full, FrameUsage.Frames32Full, 0, -4, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdBuggy = new VehicleType(30, "tdbggy", "TEXT_UNIT_TITLE_NOD_NOD_BUGGY", "BadGuy", FrameUsage.Frames32Full, FrameUsage.Frames32Full, 0, -4, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdAPC = new VehicleType(31, "tdapc", "TEXT_UNIT_TITLE_GDI_APC", "GoodGuy", FrameUsage.Frames32Full | FrameUsage.HasUnloadFrames, UnitTypeFlag.Armed);
        public static readonly UnitType TdSTank = new VehicleType(32, "tdstnk", "TEXT_UNIT_TITLE_NOD_STEALTH_TANK", "BadGuy", FrameUsage.Frames32Full, UnitTypeFlag.Armed);
        public static readonly UnitType TdMLRS = new VehicleType(33, "tdmlrs", "TEXT_UNIT_TITLE_GDI_MRLS", "GoodGuy", FrameUsage.Frames32Full, FrameUsage.Frames32Full | FrameUsage.OnFlatBed, 0, 0, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdSSM = new VehicleType(34, "tdmsam", "TEXT_UNIT_TITLE_NOD_SSM_LAUNCHER", "BadGuy", FrameUsage.Frames32Full, FrameUsage.Frames32Full | FrameUsage.OnFlatBed, 0, 0, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdArty = new VehicleType(35, "tdarty", "TEXT_UNIT_TITLE_NOD_ARTILLERY", "BadGuy", FrameUsage.Frames32Full, UnitTypeFlag.Armed);
        public static readonly UnitType TdVisceroid = new VehicleType(36, "tdvice", "TEXT_UNIT_TITLE_VICE", "Neutral", FrameUsage.Frames01Single, UnitTypeFlag.Armed);

        // Aircraft (AIRCRAFT_TDCARGO = 7 onward).
        public static readonly UnitType TdC17 = new AircraftType(7, "tdc17", "TEXT_UNIT_TITLE_C17", "BadGuy", FrameUsage.Frames32Full, UnitTypeFlag.FixedWing);
        public static readonly UnitType TdApache = new AircraftType(8, "tdheli", "TEXT_UNIT_TITLE_NOD_HELICOPTER", "BadGuy", FrameUsage.Frames32Full, FrameUsage.Rotor, "LROTOR", null, 0, -2, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdOrca = new AircraftType(9, "tdorca", "TEXT_UNIT_TITLE_GDI_ORCA", "GoodGuy", FrameUsage.Frames32Full, UnitTypeFlag.Armed);
        public static readonly UnitType TdA10 = new AircraftType(10, "tda10", "TEXT_UNIT_TITLE_A10", "GoodGuy", FrameUsage.Frames32Full, UnitTypeFlag.Armed | UnitTypeFlag.FixedWing);
        public static readonly UnitType TdParadropC17 = new AircraftType(11, "tdc17p", "TEXT_UNIT_TITLE_C17", "BadGuy", FrameUsage.Frames32Full, UnitTypeFlag.FixedWing);

        // Vessels (VESSEL_TDGUNBOAT = 7 onward; gunboat's IniName is TDBOAT).
        public static readonly UnitType TdGunBoat = new VesselType(7, "tdboat", "TEXT_UNIT_TITLE_WAKE", "GoodGuy", FrameUsage.Frames16Simple, FrameUsage.Frames32Full, "tdboattur", null, 14, 1, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdHover = new VesselType(8, "tdlst", "TEXT_UNIT_TITLE_LST", "GoodGuy", FrameUsage.Frames01Single | FrameUsage.HasUnloadFrames);
        public static readonly UnitType TdObeliskSub = new VesselType(9, "tdoblisub", "TEXT_UNIT_TF_TDOBLISUB", "BadGuy", FrameUsage.Frames16Simple, UnitTypeFlag.Armed);
        public static readonly UnitType TdSubmarine = new VesselType(10, "tdnsub", "TEXT_UNIT_RA_SS", "BadGuy", FrameUsage.Frames16Simple, FrameUsage.Frames32Full, UnitTypeFlag.Armed);
        public static readonly UnitType TdPTBoat = new VesselType(11, "tdpt", "TEXT_UNIT_RA_PT", "GoodGuy", FrameUsage.Frames16Simple, FrameUsage.Frames32Full, "mgun", null, 14, 1, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdDestroyer = new VesselType(12, "tddd", "TEXT_UNIT_RA_DD", "GoodGuy", FrameUsage.Frames16Simple, FrameUsage.Frames32Full, "ssam", null, -8, -4, UnitTypeFlag.Armed | UnitTypeFlag.Turret);
        public static readonly UnitType TdCruiser = new VesselType(13, "tdca", "TEXT_UNIT_RA_CA", "GoodGuy", FrameUsage.Frames16Simple, FrameUsage.Frames32Full, "turr", "turr", 22, -4, UnitTypeFlag.Armed | UnitTypeFlag.Turret | UnitTypeFlag.DoubleTurret);
        public static readonly UnitType TdMissileSub = new VesselType(14, "tdmsub", "TEXT_UNIT_RA_MSUB", "BadGuy", FrameUsage.Frames16Simple, UnitTypeFlag.Armed);
    }
}
