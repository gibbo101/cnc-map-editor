using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MobiusEditor.Headless;
using MobiusEditor.Model;
using MobiusEditor.RedAlert;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// Transition proof: the mapeditor.json the Tiberian Factions build ships constructs
    /// exactly the types the compiled-in *TF.cs tables define — every ctor-fed field, and
    /// the same sequence order once merged after the vanilla tables. Holds the line while
    /// the compiled tables are deleted; reduced to a manifest smoke test afterwards.
    /// </summary>
    public class ModManifestParityTests
    {
        private static ModManifest LoadManifest()
        {
            ModInfo mod = ModDiscovery.Read(TestPaths.ModDir);
            Assert.NotNull(mod);
            Assert.NotNull(mod.EditorManifestPath);
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Load(mod.EditorManifestPath, mod.Name, warnings);
            Assert.NotNull(m);
            Assert.Empty(warnings);
            return m;
        }

        private static string NameId(object type)
        {
            for (Type t = type.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField("nameId", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return (string)f.GetValue(type);
            }
            throw new MissingFieldException(type.GetType().Name, "nameId");
        }

        private static void AssertMasksEqual(bool[,] expected, bool[,] actual, string context)
        {
            Assert.True(expected.GetLength(0) == actual.GetLength(0) && expected.GetLength(1) == actual.GetLength(1), context + ": mask dimensions");
            Assert.True(expected.Cast<bool>().SequenceEqual(actual.Cast<bool>()), context + ": mask contents");
        }

        [Fact]
        public void BuildingsMatchTheCompiledTable()
        {
            List<BuildingType> manifest = ModTypeFactory.Buildings(LoadManifest());
            List<BuildingType> compiled = BuildingTypes.GetTypes().Where(b => b.ID >= 87).ToList();
            Assert.Equal(compiled.Select(b => b.ID), manifest.Select(b => b.ID));
            foreach ((BuildingType want, BuildingType got) in compiled.Zip(manifest))
            {
                string c = want.Name;
                Assert.Equal(want.Name, got.Name);
                Assert.Equal(NameId(want), NameId(got));
                Assert.Equal(want.PowerProduction, got.PowerProduction);
                Assert.Equal(want.PowerUsage, got.PowerUsage);
                Assert.Equal(want.Storage, got.Storage);
                Assert.Equal(want.Capturable, got.Capturable);
                AssertMasksEqual(want.BaseOccupyMask, got.BaseOccupyMask, c);
                Assert.Equal(want.OwnerHouse, got.OwnerHouse);
                Assert.Equal(want.FactoryOverlay, got.FactoryOverlay);
                Assert.Equal(want.FrameOffset, got.FrameOffset);
                Assert.Equal(want.GraphicsSource, got.GraphicsSource);
                Assert.Equal(want.Flags, got.Flags);
                Assert.Equal(want.ZOrder, got.ZOrder);
            }
        }

        [Fact]
        public void UnitsMatchTheCompiledTable()
        {
            List<UnitType> manifest = ModTypeFactory.Units(LoadManifest());
            List<UnitType> compiled = UnitTypes.GetTypes(false).Where(IsTfUnit).ToList();
            Assert.Equal(compiled.Select(u => (u.GetType().Name, u.ID)), manifest.Select(u => (u.GetType().Name, u.ID)));
            foreach ((UnitType want, UnitType got) in compiled.Zip(manifest))
            {
                Assert.Equal(want.Name, got.Name);
                Assert.Equal(NameId(want), NameId(got));
                Assert.Equal(want.OwnerHouse, got.OwnerHouse);
                Assert.Equal(want.BodyFrameUsage, got.BodyFrameUsage);
                Assert.Equal(want.TurretFrameUsage, got.TurretFrameUsage);
                Assert.Equal(want.Turret, got.Turret);
                Assert.Equal(want.SecondTurret, got.SecondTurret);
                Assert.Equal(want.TurretOffset, got.TurretOffset);
                Assert.Equal(want.TurretY, got.TurretY);
                Assert.Equal(want.Flags, got.Flags);
            }
        }

        [Fact]
        public void InfantryMatchTheCompiledTable()
        {
            List<InfantryType> manifest = ModTypeFactory.Infantry(LoadManifest());
            List<InfantryType> compiled = InfantryTypes.GetTypes().Where(i => i.ID >= 26).ToList();
            Assert.Equal(compiled.Select(i => i.ID), manifest.Select(i => i.ID));
            foreach ((InfantryType want, InfantryType got) in compiled.Zip(manifest))
            {
                Assert.Equal(want.Name, got.Name);
                Assert.Equal(NameId(want), NameId(got));
                Assert.Equal(want.OwnerHouse, got.OwnerHouse);
                Assert.Equal(want.Flags, got.Flags);
                Assert.Equal(want.NoImageRuleInRemaster, got.NoImageRuleInRemaster);
                Assert.Equal(want.ClassicGraphicsRemap, got.ClassicGraphicsRemap);
            }
        }

        [Fact]
        public void TemplatesMatchTheCompiledTable()
        {
            List<TemplateType> manifest = ModTypeFactory.Templates(LoadManifest());
            List<TemplateType> compiled = TemplateTypes.GetTypes().Where(IsTfTemplate).ToList();
            Assert.Equal(compiled.Select(t => t.ID), manifest.Select(t => t.ID));
            FieldInfo landsField = typeof(TemplateType).GetField("landsDefault", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach ((TemplateType want, TemplateType got) in compiled.Zip(manifest))
            {
                string c = want.Name;
                Assert.Equal(want.Name, got.Name);
                Assert.Equal(want.IconWidth, got.IconWidth);
                Assert.Equal(want.IconHeight, got.IconHeight);
                Assert.Equal(want.Flags, got.Flags);
                LandType[] wantLands = (LandType[])landsField.GetValue(want);
                LandType[] gotLands = (LandType[])landsField.GetValue(got);
                Assert.True((wantLands ?? Array.Empty<LandType>()).SequenceEqual(gotLands ?? Array.Empty<LandType>()), c + ": lands");
                Assert.Equal(want.MaskOverrides.Count, got.MaskOverrides.Count);
                foreach (KeyValuePair<string, bool[,]> kvp in want.MaskOverrides)
                {
                    AssertMasksEqual(kvp.Value, got.MaskOverrides[kvp.Key], c + " override '" + kvp.Key + "'");
                }
            }
        }

        [Fact]
        public void MergingAfterVanillaReproducesTodaysSequenceOrder()
        {
            ModManifest[] manifests = { LoadManifest() };
            List<string> warnings = new List<string>();
            Assert.Equal(
                BuildingTypes.GetTypes().Select(b => b.ID),
                ModTypeFactory.MergeBuildings(BuildingTypes.GetTypes().Where(b => b.ID < 87), manifests, true, warnings).Select(b => b.ID));
            Assert.Equal(
                UnitTypes.GetTypes(false).Select(u => (u.GetType().Name, u.ID)),
                ModTypeFactory.MergeUnits(UnitTypes.GetTypes(false).Where(u => !IsTfUnit(u)).ToList(), manifests, warnings).Select(u => (u.GetType().Name, u.ID)));
            Assert.Equal(
                InfantryTypes.GetTypes().Select(i => i.ID),
                ModTypeFactory.MergeInfantry(InfantryTypes.GetTypes().Where(i => i.ID < 26), manifests, warnings).Select(i => i.ID));
            Assert.Equal(
                TemplateTypes.GetTypes().Select(t => t.ID),
                ModTypeFactory.MergeTemplates(TemplateTypes.GetTypes().Where(t => !IsTfTemplate(t)), manifests, warnings).Select(t => t.ID));
            Assert.Empty(warnings);
        }

        private static bool IsTfUnit(UnitType u) =>
            (u is VehicleType && u.ID >= 22) || (u is AircraftType && u.ID >= 7) || (u is VesselType && u.ID >= 7);

        private static bool IsTfTemplate(TemplateType t) => t.ID >= TemplateType.TFTDTileIdFirst && t.ID < 0xFFF0;
    }
}
