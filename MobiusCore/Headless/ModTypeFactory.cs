//
// Turns ModManifest entries into engine types and merges them after the vanilla tables.
// Construction returns fresh instances on every call: per-plugin theater init mutates types
// in place, so manifest types must never be shared between plugin instances. Merge order is
// vanilla declaration order first, then manifest entries by (mod load order, id) — units
// grouped vehicle/aircraft/vessel, matching the per-kind engine enums whose ids only make
// sense within their kind. On an id or name collision the first-loaded type wins and the
// dropped entry leaves a warning.
using System;
using System.Collections.Generic;
using System.Linq;
using MobiusEditor.Model;

namespace MobiusEditor.Headless
{
    public static class ModTypeFactory
    {
        public static List<BuildingType> Buildings(ModManifest manifest) =>
            manifest.Buildings.Select(b => new BuildingType(b.Id, b.Name, b.TextId, b.PowerProduction, b.PowerUsage, b.Storage,
                b.Capturable, b.Width, b.Height, b.OccupyMask, b.Owner, b.FactoryOverlay, b.FrameOffset, b.GraphicsSource, b.Flags, b.ZOrder)
                { ModSource = manifest.ModName, NameOverride = b.DisplayName, DefaultNameOverride = b.DisplayName }).ToList();

        public static List<UnitType> Units(ModManifest manifest) =>
            manifest.Units.Select(u => (UnitType)(u.Kind switch
            {
                ManifestUnitKind.Vehicle => new VehicleType(u.Id, u.Name, u.TextId, u.Owner, u.BodyFrames, u.TurretFrames, u.Turret, u.Turret2, u.TurretOffset, u.TurretY, u.Flags) { ModSource = manifest.ModName, NameOverride = u.DisplayName, DefaultNameOverride = u.DisplayName, WalkFrames = u.WalkFrames },
                ManifestUnitKind.Aircraft => new AircraftType(u.Id, u.Name, u.TextId, u.Owner, u.BodyFrames, u.TurretFrames, u.Turret, u.Turret2, u.TurretOffset, u.TurretY, u.Flags) { ModSource = manifest.ModName, NameOverride = u.DisplayName, DefaultNameOverride = u.DisplayName, WalkFrames = u.WalkFrames },
                ManifestUnitKind.Vessel => new VesselType(u.Id, u.Name, u.TextId, u.Owner, u.BodyFrames, u.TurretFrames, u.Turret, u.Turret2, u.TurretOffset, u.TurretY, u.Flags) { ModSource = manifest.ModName, NameOverride = u.DisplayName, DefaultNameOverride = u.DisplayName, WalkFrames = u.WalkFrames },
                _ => throw new InvalidOperationException("unreachable"),
            })).ToList();

        public static List<InfantryType> Infantry(ModManifest manifest) =>
            manifest.Infantry.Select(i => new InfantryType(i.Id, i.Name, i.TextId, i.Owner, i.Flags) { ModSource = manifest.ModName, NameOverride = i.DisplayName, DefaultNameOverride = i.DisplayName }).ToList();

        public static List<TemplateType> Templates(ModManifest manifest) =>
            manifest.Templates.Select(t => t.Mask == null
                ? new TemplateType((ushort)t.Id, t.Name, t.Width, t.Height, t.Lands) { ModSource = manifest.ModName }
                : new TemplateType((ushort)t.Id, t.Name, t.Width, t.Height, t.Lands, t.Mask) { ModSource = manifest.ModName }).ToList();

        /// <summary>Manifests must already be in mod load order. allowWalls mirrors the vanilla table's Globals.AllowWallBuildings filter.</summary>
        public static List<BuildingType> MergeBuildings(IEnumerable<BuildingType> vanilla, IEnumerable<ModManifest> manifests, bool allowWalls, List<string> warnings) =>
            Merge(vanilla, manifests, m => Buildings(m).Where(b => allowWalls || !b.IsWall).OrderBy(b => b.ID),
                b => b.ID, b => b.Name, "building", warnings);

        /// <summary>Aircraft are always included; the caller applies any Globals.DisableAirUnits filter where the vanilla table would.</summary>
        public static List<UnitType> MergeUnits(IEnumerable<UnitType> vanilla, IEnumerable<ModManifest> manifests, List<string> warnings) =>
            Merge(vanilla, manifests, m => Units(m).OrderBy(KindRank).ThenBy(u => u.ID),
                u => (KindRank(u), u.ID), u => (KindRank(u), u.Name.ToUpperInvariant()), "unit", warnings);

        public static List<InfantryType> MergeInfantry(IEnumerable<InfantryType> vanilla, IEnumerable<ModManifest> manifests, List<string> warnings) =>
            Merge(vanilla, manifests, m => Infantry(m).OrderBy(i => i.ID), i => i.ID, i => i.Name, "infantry", warnings);

        public static List<TemplateType> MergeTemplates(IEnumerable<TemplateType> vanilla, IEnumerable<ModManifest> manifests, List<string> warnings) =>
            Merge(vanilla, manifests, m => Templates(m).OrderBy(t => t.ID), t => (int)t.ID, t => t.Name, "template", warnings);

        private static int KindRank(UnitType u) => u.IsAircraft ? 1 : u.IsVessel ? 2 : 0;

        private static List<T> Merge<T, TId, TName>(IEnumerable<T> vanilla, IEnumerable<ModManifest> manifests,
            Func<ModManifest, IEnumerable<T>> build, Func<T, TId> idKey, Func<T, TName> nameKey, string category, List<string> warnings)
        {
            List<T> merged = new List<T>(vanilla);
            HashSet<TId> ids = new HashSet<TId>(merged.Select(idKey));
            HashSet<TName> names = new HashSet<TName>(merged.Select(nameKey), NameComparer<TName>());
            foreach (ModManifest manifest in manifests)
            {
                foreach (T type in build(manifest))
                {
                    if (!ids.Add(idKey(type)) || !names.Add(nameKey(type)))
                    {
                        warnings.Add($"{manifest.ModName}: {category} '{nameKey(type)}' (id {idKey(type)}) collides with an already-loaded type; keeping the first.");
                        continue;
                    }
                    merged.Add(type);
                }
            }
            return merged;
        }

        private static IEqualityComparer<TName> NameComparer<TName>() =>
            typeof(TName) == typeof(string) ? (IEqualityComparer<TName>)(object)StringComparer.OrdinalIgnoreCase : EqualityComparer<TName>.Default;
    }
}
