using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Headless;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>A building type the mod marks Unplaceable is never offered by the building palette.</summary>
    public class UnplaceableBuildingTests
    {
        [Fact]
        public void TheBuildingPaletteLeavesOutUnplaceableTypes()
        {
            string mod = TestPaths.Output(Path.Combine("fake-mods-building", "UnplaceableMod"));
            Directory.CreateDirectory(Path.Combine(mod, "Data"));
            File.WriteAllText(Path.Combine(mod, "ccmod.json"), "{ \"name\": \"UnplaceableMod\", \"game_type\": \"RA\", \"version_high\": 1, \"version_low\": 0 }");
            File.WriteAllText(Path.Combine(mod, "mapeditor.json"), @"{ ""format"": 1, ""game_type"": ""RA"", ""buildings"": [
                { ""id"": 201, ""name"": ""xhidden"", ""display_name"": ""Hidden"", ""owner"": ""Neutral"", ""width"": 1, ""height"": 1,
                  ""graphics_source"": ""powr"", ""flags"": [""Unplaceable""] } ] }");
            using (MapDocument doc = new MapDocument(new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { mod })))
            {
                doc.Open(Path.Combine(TestPaths.OfficialMapsDir, "scm05ea.ini"));
                Assert.Contains(doc.Map.BuildingTypes, b => b.Name == "xhidden" && b.ExistsInTheater);
                Assert.DoesNotContain(doc.AvailableBuildings(), b => b.Name == "xhidden");
            }
        }
    }
}
