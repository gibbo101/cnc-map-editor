//
// What a properties panel shows for a placed object — the headless port of the fork's
// ObjectProperties control rules. Units and infantry offer unit directions, missions and
// unit-eligible triggers (aircraft never get a trigger, the game loses track of them);
// buildings gate house/strength/direction/trigger behind IsPrebuilt, show direction only
// when the type has a turret, and carry the per-game extras (base priority + prebuilt,
// plus sellable/rebuild in Red Alert). NormalizeBuilding applies the fork's invariants
// after an edit: a building outside the rebuild base can never be non-prebuilt, and a
// non-prebuilt one belongs to the base house at full strength with nothing attached.
using System.Collections.Generic;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.Shell
{
    public sealed class ObjectPropertiesPresentation
    {
        public string Title { get; internal set; }
        public IReadOnlyList<string> Houses { get; internal set; } = new List<string>();
        public string House { get; internal set; }
        public bool HouseEnabled { get; internal set; }
        public bool StrengthEnabled { get; internal set; }
        public int Strength { get; internal set; }
        public bool DirectionVisible { get; internal set; }
        public bool DirectionEnabled { get; internal set; }
        public IReadOnlyList<string> Directions { get; internal set; } = new List<string>();
        public string Direction { get; internal set; }
        public bool MissionVisible { get; internal set; }
        public IReadOnlyList<string> Missions { get; internal set; } = new List<string>();
        public string Mission { get; internal set; }
        public bool TriggerEnabled { get; internal set; }
        public IReadOnlyList<string> Triggers { get; internal set; } = new List<string>();
        public string Trigger { get; internal set; }
        public bool BuildingExtrasVisible { get; internal set; }
        public bool SellableRebuildVisible { get; internal set; }
        public int BasePriority { get; internal set; }
        public bool IsPrebuilt { get; internal set; }
        public bool PrebuiltEnabled { get; internal set; }
        public bool Sellable { get; internal set; }
        public bool Rebuild { get; internal set; }
    }

    public static class ObjectPropertiesPresenter
    {
        public static ObjectPropertiesPresentation For(IGamePlugin plugin, object techno)
        {
            Map map = plugin.Map;
            ObjectPropertiesPresentation p = new ObjectPropertiesPresentation
            {
                Houses = map.Houses.Select(h => h.Type.Name).ToList(),
            };
            switch (techno)
            {
                case Unit unit:
                    p.Title = unit.Type.Name + " (" + (unit.Type.IsAircraft ? "Aircraft" : unit.Type.IsVessel ? "Vessel" : "Unit") + ")";
                    FillTechno(p, unit.House, unit.Strength, unit.Direction, unit.Trigger);
                    p.Directions = map.UnitDirectionTypes.Select(d => d.Name).ToList();
                    p.MissionVisible = true;
                    p.Missions = map.MissionTypes.ToList();
                    p.Mission = unit.Mission;
                    p.TriggerEnabled = !unit.Type.IsAircraft;
                    p.Triggers = Model.Trigger.None.Yield().Concat(map.FilterUnitTriggers().Select(t => t.Name)).ToList();
                    break;
                case Infantry infantry:
                    p.Title = infantry.Type.Name + " (Infantry)";
                    FillTechno(p, infantry.House, infantry.Strength, infantry.Direction, infantry.Trigger);
                    p.Directions = map.UnitDirectionTypes.Select(d => d.Name).ToList();
                    p.MissionVisible = true;
                    p.Missions = map.MissionTypes.ToList();
                    p.Mission = infantry.Mission;
                    p.TriggerEnabled = true;
                    p.Triggers = Model.Trigger.None.Yield().Concat(map.FilterUnitTriggers().Select(t => t.Name)).ToList();
                    break;
                case Building building:
                    p.Title = building.Type.Name + " (Building)";
                    p.House = building.House?.Name;
                    p.Strength = building.Strength;
                    p.Direction = building.Direction?.Name;
                    p.Trigger = building.Trigger;
                    p.HouseEnabled = building.IsPrebuilt;
                    p.StrengthEnabled = building.IsPrebuilt;
                    p.DirectionVisible = building.Type?.HasTurret == true;
                    p.DirectionEnabled = building.IsPrebuilt;
                    p.Directions = map.BuildingDirectionTypes.Select(d => d.Name).ToList();
                    p.TriggerEnabled = building.IsPrebuilt;
                    p.Triggers = Model.Trigger.None.Yield().Concat(map.FilterStructureTriggers().Select(t => t.Name)).ToList();
                    p.BuildingExtrasVisible = plugin.GameInfo.GameType != GameType.SoleSurvivor;
                    p.SellableRebuildVisible = plugin.GameInfo.GameType == GameType.RedAlert;
                    p.BasePriority = building.BasePriority;
                    p.IsPrebuilt = building.IsPrebuilt;
                    p.PrebuiltEnabled = building.BasePriority >= 0;
                    p.Sellable = building.Sellable;
                    p.Rebuild = building.Rebuild;
                    break;
                case Terrain terrain:
                    // Red Alert terrain carries nothing editable (no triggers on terrain).
                    p.Title = terrain.Type.Name + " (Terrain)";
                    break;
                default:
                    p.Title = "";
                    break;
            }
            return p;
        }

        private static void FillTechno(ObjectPropertiesPresentation p, HouseType house, int strength, DirectionType direction, string trigger)
        {
            p.HouseEnabled = true;
            p.House = house?.Name;
            p.StrengthEnabled = true;
            p.Strength = strength;
            p.DirectionVisible = true;
            p.DirectionEnabled = true;
            p.Direction = direction?.Name;
            p.Trigger = trigger;
        }

        /// <summary>The fork's building invariants, applied after every edit.</summary>
        public static void NormalizeBuilding(IGamePlugin plugin, Building building)
        {
            if (building.BasePriority < 0 && !building.IsPrebuilt)
            {
                building.IsPrebuilt = true;
            }
            if (!building.IsPrebuilt)
            {
                HouseType baseHouse = plugin.Map.GetBaseHouse(plugin.GameInfo);
                if (baseHouse != null && baseHouse.ID >= 0)
                {
                    building.House = baseHouse;
                }
                building.Strength = 256;
                building.Direction = plugin.Map.BuildingDirectionTypes.First(d => d.Facing == FacingType.North);
                building.Trigger = Model.Trigger.None;
                building.Sellable = false;
                building.Rebuild = false;
            }
        }
    }
}
