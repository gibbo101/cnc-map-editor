using System.Collections.Generic;
using System.Linq;
using MobiusEditor;
using MobiusEditor.Headless;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// ModTypeFactory turns manifest entries into engine types — fresh instances on every call,
    /// so per-plugin theater init never mutates shared state — and merges them after the
    /// vanilla tables: vanilla declaration order first, then manifest entries by
    /// (mod load order, id), units grouped vehicle/aircraft/vessel. On an id or name
    /// collision the first-loaded type wins and the dropped entry leaves a warning.
    /// </summary>
    public class ModTypeFactoryTests
    {
        private static ModManifest Manifest(string body, string modName = "TF")
        {
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Parse(@"{ ""format"": 1, ""game_type"": ""RA"", " + body + " }", modName, warnings);
            Assert.Empty(warnings);
            return m;
        }

        private static readonly string Silo = @"""buildings"": [
            { ""id"": 91, ""name"": ""tdsilo"", ""text_id"": ""TEXT_STRUCTURE_TITLE_GDI_SILO"",
              ""power_usage"": 10, ""storage"": 1500, ""capturable"": true,
              ""width"": 2, ""height"": 1, ""occupy_mask"": ""10"", ""owner"": ""GoodGuy"",
              ""z_order"": ""paved"", ""flags"": [""Bib""] } ]";

        [Fact]
        public void BuildsABuildingWithMaskBibAndZOrder()
        {
            BuildingType b = Assert.Single(ModTypeFactory.Buildings(Manifest(Silo)));
            Assert.Equal(91, b.ID);
            Assert.Equal("tdsilo", b.Name);
            Assert.Equal("tdsilo", b.GraphicsSource);
            Assert.Equal(10, b.PowerUsage);
            Assert.Equal(1500, b.Storage);
            Assert.True(b.Capturable);
            Assert.Equal("GoodGuy", b.OwnerHouse);
            Assert.Equal(Globals.ZOrderPaved, b.ZOrder);
            Assert.True(b.HasBib);
            // "10": one row; '0' is the free cell, so the first cell is occupied, the second free.
            Assert.True(b.BaseOccupyMask[0, 0]);
            Assert.False(b.BaseOccupyMask[0, 1]);
        }

        [Fact]
        public void UnitKindPicksTheConcreteType()
        {
            ModManifest m = Manifest(@"""units"": [
                { ""id"": 24, ""kind"": ""vehicle"", ""name"": ""v"", ""text_id"": ""T"", ""owner"": ""GoodGuy"",
                  ""body_frames"": [""Frames32Full""], ""turret_frames"": [""Frames32Full"", ""OnFlatBed""],
                  ""turret_y"": -4, ""flags"": [""Armed"", ""Turret""] },
                { ""id"": 8, ""kind"": ""aircraft"", ""name"": ""a"", ""text_id"": ""T"", ""owner"": ""BadGuy"",
                  ""body_frames"": [""Frames32Full""], ""flags"": [""FixedWing""] },
                { ""id"": 7, ""kind"": ""vessel"", ""name"": ""s"", ""text_id"": ""T"", ""owner"": ""GoodGuy"",
                  ""body_frames"": [""Frames16Simple""], ""turret_frames"": [""Frames32Full""],
                  ""turret"": ""stur"", ""turret_offset"": 14, ""turret_y"": 1, ""flags"": [""Armed"", ""Turret""] } ]");
            List<UnitType> units = ModTypeFactory.Units(m);
            UnitType v = units.Single(u => u.Name == "v");
            Assert.IsType<VehicleType>(v);
            Assert.Equal(FrameUsage.Frames32Full | FrameUsage.OnFlatBed, v.TurretFrameUsage);
            Assert.Equal(-4, v.TurretY);
            UnitType a = units.Single(u => u.Name == "a");
            Assert.IsType<AircraftType>(a);
            Assert.True(a.IsFixedWing);
            UnitType s = units.Single(u => u.Name == "s");
            Assert.IsType<VesselType>(s);
            Assert.Equal("stur", s.Turret);
            Assert.Equal(14, s.TurretOffset);
        }

        [Fact]
        public void BuildsInfantryAndTemplates()
        {
            ModManifest m = Manifest(@"""infantry"": [
                { ""id"": 26, ""name"": ""tde1"", ""text_id"": ""T"", ""owner"": ""GoodGuy"", ""flags"": [""Armed""] } ],
                ""templates"": [
                { ""id"": 401, ""name"": ""tdsh1"", ""width"": 3, ""height"": 3, ""lands"": ""BBB BBB WWW"" },
                { ""id"": 405, ""name"": ""tdsh3"", ""width"": 2, ""height"": 1, ""lands"": ""II"", ""mask"": ""10"" } ]");
            InfantryType i = Assert.Single(ModTypeFactory.Infantry(m));
            Assert.Equal(26, i.ID);
            Assert.Equal(UnitTypeFlag.Armed, i.Flags);
            List<TemplateType> templates = ModTypeFactory.Templates(m);
            Assert.Equal(401, (int)templates[0].ID);
            Assert.Equal(3, templates[0].IconWidth);
            Assert.Equal(405, (int)templates[1].ID);
        }

        [Fact]
        public void EveryCallBuildsFreshInstances()
        {
            ModManifest m = Manifest(Silo);
            Assert.NotSame(ModTypeFactory.Buildings(m)[0], ModTypeFactory.Buildings(m)[0]);
        }

        [Fact]
        public void MergeAppendsManifestTypesAfterVanillaSortedByLoadOrderThenId()
        {
            ModManifest first = Manifest(@"""buildings"": [
                { ""id"": 95, ""name"": ""m1b"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""GoodGuy"" },
                { ""id"": 90, ""name"": ""m1a"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""GoodGuy"" } ]", "First");
            ModManifest second = Manifest(@"""buildings"": [
                { ""id"": 87, ""name"": ""m2"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""BadGuy"" } ]", "Second");
            BuildingType vanilla = new BuildingType(1, "van", "T", 0, 0, false, 1, 1, null, "Neutral");
            List<string> warnings = new List<string>();
            List<BuildingType> merged = ModTypeFactory.MergeBuildings(new[] { vanilla }, new[] { first, second }, true, warnings);
            Assert.Empty(warnings);
            Assert.Equal(new[] { "van", "m1a", "m1b", "m2" }, merged.Select(b => b.Name));
        }

        [Fact]
        public void MergeDropsIdAndNameCollisionsWithAWarning()
        {
            BuildingType vanilla = new BuildingType(87, "van", "T", 0, 0, false, 1, 1, null, "Neutral");
            ModManifest m = Manifest(@"""buildings"": [
                { ""id"": 87, ""name"": ""idclash"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""GoodGuy"" },
                { ""id"": 88, ""name"": ""VAN"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""GoodGuy"" },
                { ""id"": 89, ""name"": ""ok"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""GoodGuy"" } ]");
            List<string> warnings = new List<string>();
            List<BuildingType> merged = ModTypeFactory.MergeBuildings(new[] { vanilla }, new[] { m }, true, warnings);
            Assert.Equal(new[] { "van", "ok" }, merged.Select(b => b.Name));
            Assert.Equal(2, warnings.Count);
            Assert.Contains(warnings, w => w.Contains("idclash"));
            Assert.Contains(warnings, w => w.Contains("VAN"));
        }

        [Fact]
        public void MergedUnitsGroupByKindAndUnitIdsArePerKindNamespaces()
        {
            // Aircraft id 7 and vessel id 7 coexist; output groups vehicle, aircraft, vessel.
            ModManifest m = Manifest(@"""units"": [
                { ""id"": 7, ""kind"": ""vessel"", ""name"": ""boat"", ""text_id"": ""T"", ""owner"": ""GoodGuy"", ""body_frames"": [""Frames16Simple""] },
                { ""id"": 7, ""kind"": ""aircraft"", ""name"": ""plane"", ""text_id"": ""T"", ""owner"": ""BadGuy"", ""body_frames"": [""Frames32Full""] },
                { ""id"": 22, ""kind"": ""vehicle"", ""name"": ""tank"", ""text_id"": ""T"", ""owner"": ""GoodGuy"", ""body_frames"": [""Frames32Full""] } ]");
            List<string> warnings = new List<string>();
            List<UnitType> merged = ModTypeFactory.MergeUnits(new UnitType[0], new[] { m }, warnings);
            Assert.Empty(warnings);
            Assert.Equal(new[] { "tank", "plane", "boat" }, merged.Select(u => u.Name));
        }

        [Fact]
        public void MergeBuildingsCanFilterWallsLikeTheVanillaTable()
        {
            ModManifest m = Manifest(@"""buildings"": [
                { ""id"": 87, ""name"": ""wall"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""Neutral"", ""flags"": [""Wall""] },
                { ""id"": 88, ""name"": ""keep"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""Neutral"" } ]");
            List<string> warnings = new List<string>();
            List<BuildingType> merged = ModTypeFactory.MergeBuildings(new BuildingType[0], new[] { m }, false, warnings);
            BuildingType keep = Assert.Single(merged);
            Assert.Equal("keep", keep.Name);
            Assert.Empty(warnings);
        }
    }
}
