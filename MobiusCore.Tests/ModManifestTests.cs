using System.Collections.Generic;
using System.Linq;
using MobiusEditor;
using MobiusEditor.Headless;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// A mod ships its editor type tables as mapeditor.json beside ccmod.json. Parsing never
    /// throws: a file with an unsupported format is skipped whole, a malformed entry or an
    /// unknown flag name skips just that entry — always with a warning, so a v2 flag can
    /// never silently half-apply.
    /// </summary>
    public class ModManifestTests
    {
        private const string FullManifest = @"{
            ""format"": 1, ""game_type"": ""RA"",
            ""buildings"": [
                { ""id"": 91, ""name"": ""tdsilo"", ""text_id"": ""TEXT_STRUCTURE_TITLE_GDI_SILO"",
                  ""power_production"": 0, ""power_usage"": 10, ""storage"": 1500, ""capturable"": true,
                  ""width"": 2, ""height"": 1, ""occupy_mask"": ""10"", ""owner"": ""GoodGuy"",
                  ""factory_overlay"": null, ""frame_offset"": 0, ""graphics_source"": null,
                  ""z_order"": ""paved"", ""flags"": [""Bib""] }
            ],
            ""units"": [
                { ""id"": 24, ""kind"": ""vehicle"", ""name"": ""tdmtnk"", ""text_id"": ""TEXT_UNIT_TITLE_GDI_MED_TANK"",
                  ""owner"": ""GoodGuy"", ""body_frames"": [""Frames32Full""],
                  ""turret_frames"": [""Frames32Full"", ""OnFlatBed""], ""turret"": null, ""turret2"": null,
                  ""turret_offset"": 0, ""turret_y"": -4, ""flags"": [""Armed"", ""Turret""] }
            ],
            ""infantry"": [
                { ""id"": 26, ""name"": ""tde1"", ""text_id"": ""TEXT_UNIT_TITLE_GDI_MINIGUNNER"",
                  ""owner"": ""GoodGuy"", ""flags"": [""Armed""] }
            ],
            ""templates"": [
                { ""id"": 401, ""name"": ""tdsh1"", ""width"": 3, ""height"": 3, ""lands"": ""BBB BBB WWW"", ""mask"": null },
                { ""id"": 405, ""name"": ""tdsh3"", ""width"": 1, ""height"": 1, ""lands"": ""I"", ""mask"": ""1"" }
            ] }";

        [Fact]
        public void ParsesEveryCategory()
        {
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Parse(FullManifest, "TF", warnings);
            Assert.NotNull(m);
            Assert.Empty(warnings);
            Assert.Equal("TF", m.ModName);
            Assert.Equal("RA", m.GameType);

            ManifestBuilding b = Assert.Single(m.Buildings);
            Assert.Equal(91, b.Id);
            Assert.Equal("tdsilo", b.Name);
            Assert.Equal("TEXT_STRUCTURE_TITLE_GDI_SILO", b.TextId);
            Assert.Equal(0, b.PowerProduction);
            Assert.Equal(10, b.PowerUsage);
            Assert.Equal(1500, b.Storage);
            Assert.True(b.Capturable);
            Assert.Equal(2, b.Width);
            Assert.Equal(1, b.Height);
            Assert.Equal("10", b.OccupyMask);
            Assert.Equal("GoodGuy", b.Owner);
            Assert.Null(b.FactoryOverlay);
            Assert.Equal(0, b.FrameOffset);
            Assert.Null(b.GraphicsSource);
            Assert.Equal(Globals.ZOrderPaved, b.ZOrder);
            Assert.Equal(BuildingTypeFlag.Bib, b.Flags);

            ManifestUnit u = Assert.Single(m.Units);
            Assert.Equal(24, u.Id);
            Assert.Equal(ManifestUnitKind.Vehicle, u.Kind);
            Assert.Equal(FrameUsage.Frames32Full, u.BodyFrames);
            Assert.Equal(FrameUsage.Frames32Full | FrameUsage.OnFlatBed, u.TurretFrames);
            Assert.Equal(-4, u.TurretY);
            Assert.Equal(UnitTypeFlag.Armed | UnitTypeFlag.Turret, u.Flags);

            ManifestInfantry i = Assert.Single(m.Infantry);
            Assert.Equal(26, i.Id);
            Assert.Equal(UnitTypeFlag.Armed, i.Flags);

            Assert.Equal(2, m.Templates.Count);
            Assert.Equal("BBB BBB WWW", m.Templates[0].Lands);
            Assert.Null(m.Templates[0].Mask);
            Assert.Equal("1", m.Templates[1].Mask);
        }

        [Fact]
        public void OmittedFieldsGetTheirDefaults()
        {
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Parse(@"{ ""format"": 1, ""game_type"": ""RA"",
                ""buildings"": [ { ""id"": 87, ""name"": ""x"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""Neutral"" } ],
                ""units"": [ { ""id"": 7, ""kind"": ""aircraft"", ""name"": ""y"", ""text_id"": ""T"", ""owner"": ""BadGuy"", ""body_frames"": [""Frames32Full""] } ] }", "TF", warnings);
            Assert.Empty(warnings);
            ManifestBuilding b = Assert.Single(m.Buildings);
            Assert.Equal(0, b.Storage);
            Assert.Null(b.OccupyMask);
            Assert.Equal(Globals.ZOrderDefault, b.ZOrder);
            Assert.Equal(BuildingTypeFlag.None, b.Flags);
            Assert.False(b.Capturable);
            ManifestUnit u = Assert.Single(m.Units);
            Assert.Equal(FrameUsage.None, u.TurretFrames);
            Assert.Null(u.Turret);
            Assert.Equal(0, u.TurretOffset);
            Assert.Equal(UnitTypeFlag.None, u.Flags);
            Assert.Empty(m.Infantry);
            Assert.Empty(m.Templates);
        }

        [Fact]
        public void DisplayNameSubstitutesForTextIdAndBecomesTheNameOverride()
        {
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Parse(@"{ ""format"": 1, ""game_type"": ""RA"",
                ""buildings"": [
                    { ""id"": 112, ""name"": ""tspowr"", ""text_id"": null, ""display_name"": ""Tiberian Power Plant"",
                      ""width"": 2, ""height"": 2, ""occupy_mask"": ""00 11"", ""owner"": ""GoodGuy"" } ],
                ""units"": [
                    { ""id"": 37, ""kind"": ""vehicle"", ""name"": ""tshvr"", ""display_name"": ""Hover MLRS"",
                      ""owner"": ""GoodGuy"", ""body_frames"": [""Frames32Full""] } ],
                ""infantry"": [
                    { ""id"": 30, ""name"": ""tsjump"", ""display_name"": ""Jumpjet Infantry"", ""owner"": ""GoodGuy"" } ] }",
                "TF", warnings);
            Assert.Empty(warnings);
            ManifestBuilding b = Assert.Single(m.Buildings);
            Assert.Null(b.TextId);
            Assert.Equal("Tiberian Power Plant", b.DisplayName);
            Assert.Equal("Tiberian Power Plant", ModTypeFactory.Buildings(m).Single().NameOverride);
            Assert.Equal("Hover MLRS", ModTypeFactory.Units(m).Single().NameOverride);
            Assert.Equal("Jumpjet Infantry", ModTypeFactory.Infantry(m).Single().NameOverride);
        }

        [Fact]
        public void EntryWithNeitherTextIdNorDisplayNameIsSkippedWithAWarning()
        {
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Parse(@"{ ""format"": 1, ""game_type"": ""RA"",
                ""buildings"": [
                    { ""id"": 87, ""name"": ""unnamed"", ""width"": 1, ""height"": 1, ""owner"": ""Neutral"" } ] }", "TF", warnings);
            Assert.Empty(m.Buildings);
            string warning = Assert.Single(warnings);
            Assert.Contains("unnamed", warning);
            Assert.Contains("text_id", warning);
        }

        [Fact]
        public void UnknownFlagRejectsTheEntryWithAWarning()
        {
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Parse(@"{ ""format"": 1, ""game_type"": ""RA"",
                ""buildings"": [
                    { ""id"": 87, ""name"": ""bad"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""Neutral"", ""flags"": [""Hologram""] },
                    { ""id"": 88, ""name"": ""good"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""Neutral"" } ] }", "TF", warnings);
            ManifestBuilding survivor = Assert.Single(m.Buildings);
            Assert.Equal("good", survivor.Name);
            string warning = Assert.Single(warnings);
            Assert.Contains("bad", warning);
            Assert.Contains("Hologram", warning);
        }

        [Fact]
        public void MalformedEntryIsSkippedWithAWarning()
        {
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Parse(@"{ ""format"": 1, ""game_type"": ""RA"",
                ""units"": [
                    { ""kind"": ""vehicle"", ""name"": ""noid"", ""text_id"": ""T"", ""owner"": ""GoodGuy"", ""body_frames"": [""Frames32Full""] },
                    { ""id"": 22, ""kind"": ""hovercraft"", ""name"": ""badkind"", ""text_id"": ""T"", ""owner"": ""GoodGuy"", ""body_frames"": [""Frames32Full""] },
                    { ""id"": 23, ""kind"": ""vessel"", ""name"": ""ok"", ""text_id"": ""T"", ""owner"": ""GoodGuy"", ""body_frames"": [""Frames16Simple""] } ] }", "TF", warnings);
            ManifestUnit survivor = Assert.Single(m.Units);
            Assert.Equal("ok", survivor.Name);
            Assert.Equal(2, warnings.Count);
        }

        [Fact]
        public void UnsupportedFormatSkipsTheWholeFileWithAWarning()
        {
            List<string> warnings = new List<string>();
            ModManifest m = ModManifest.Parse(@"{ ""format"": 2, ""game_type"": ""RA"",
                ""buildings"": [ { ""id"": 87, ""name"": ""x"", ""text_id"": ""T"", ""width"": 1, ""height"": 1, ""owner"": ""Neutral"" } ] }", "TF", warnings);
            Assert.Null(m);
            Assert.Contains(warnings, w => w.Contains("format"));
        }

        [Fact]
        public void UnparseableJsonReturnsNullWithAWarning()
        {
            List<string> warnings = new List<string>();
            Assert.Null(ModManifest.Parse("{ not json", "TF", warnings));
            Assert.Single(warnings);
        }
    }
}
