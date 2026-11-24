using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Render;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// A mod's building may ship no art of its own (a converted map's picture or per-map art draws it):
    /// it loads as its type, draws nothing, and a save keeps it where it was.
    /// </summary>
    public class ModBuildingTests
    {
        [Fact]
        public void AnUnplaceableModBuildingWithNoArtIsKeptAndDrawsNothing()
        {
            string mod = TestPaths.Output(Path.Combine("fake-mods-building", "CivMod"));
            Directory.CreateDirectory(Path.Combine(mod, "Data"));
            File.WriteAllText(Path.Combine(mod, "ccmod.json"), "{ \"name\": \"CivMod\", \"game_type\": \"RA\", \"version_high\": 1, \"version_low\": 0 }");
            File.WriteAllText(Path.Combine(mod, "mapeditor.json"), @"{ ""format"": 1, ""game_type"": ""RA"", ""buildings"": [
                { ""id"": 200, ""name"": ""xciv"", ""display_name"": ""Converted Building"", ""owner"": ""Neutral"", ""width"": 1, ""height"": 1, ""flags"": [""Unplaceable""] } ] }");
            string[] lines = File.ReadAllLines(Path.Combine(TestPaths.OfficialMapsDir, "scm111ea.ini"));
            string map = TestPaths.Output("mod-building.ini");
            File.WriteAllLines(map, lines.Concat(new[] { "", "[STRUCTURES]", "0=Neutral,XCIV,256,7740,0,None" }));

            EditorSession session = new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { mod });
            IGamePlugin plugin = session.Load(map, out string[] errors);
            Assert.Empty(session.ManifestLoadWarnings);
            Assert.DoesNotContain(plugin.Map.UnknownEntries, u => u.Value.Contains("XCIV"));
            Building building = plugin.Map.Buildings.Select(b => b.Occupier).OfType<Building>().Single(b => b.Type.Name == "xciv");
            Assert.True(building.Type.Flags.HasFlag(BuildingTypeFlag.Unplaceable));
            Assert.Null(MapRenderer.RenderBuilding(plugin.GameInfo, plugin.Map, new System.Drawing.Point(60, 60), new System.Drawing.Size(32, 32), 0.25, building, false).RenderAction);

            string outPath = TestPaths.Output("mod-building-saved.ini");
            plugin.Save(outPath, FileType.INI);
            string[] saved = File.ReadAllLines(outPath);
            int idx = System.Array.IndexOf(saved, "[STRUCTURES]");
            // Saved in the standard Red Alert form: renumbered key, sellable and rebuild fields appended.
            Assert.Contains(saved.Skip(idx + 1).TakeWhile(l => !l.StartsWith("[")), l => l.Contains("=Neutral,XCIV,256,7740,0,None"));
        }
    }
}
