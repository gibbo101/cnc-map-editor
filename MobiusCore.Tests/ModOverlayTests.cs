using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// A mod's overlays come from its mapeditor.json: a map placing one loads it as a real
    /// overlay, and a save writes its own id back, never folded into the stock ore.
    /// </summary>
    public class ModOverlayTests
    {
        private const int Cell = 60 * 128 + 60;

        private static string MapWithOverlayId(byte id)
        {
            INI ini = new INI();
            ini.Parse(File.ReadAllText(Path.Combine(TestPaths.OfficialMapsDir, "scm111ea.ini")));
            bool modified = false;
            byte[] overlay = INITools.DecompressLCWSection(ini["OverlayPack"], new CellMetrics(128, 128), 1, new List<string>(), ref modified);
            overlay[Cell] = id;
            ini.Sections.Remove("OverlayPack");
            INITools.CompressLCWSection(ini.Sections.Add("OverlayPack"), overlay);
            string path = TestPaths.Output("mod-overlay.ini");
            File.WriteAllText(path, ini.ToString());
            return path;
        }

        private static string ModWithOreLikeOverlay()
        {
            string mod = TestPaths.Output(Path.Combine("fake-mods-overlay", "OreMod"));
            Directory.CreateDirectory(Path.Combine(mod, "Data"));
            File.WriteAllText(Path.Combine(mod, "ccmod.json"), "{ \"name\": \"OreMod\", \"game_type\": \"RA\", \"version_high\": 1, \"version_low\": 0 }");
            File.WriteAllText(Path.Combine(mod, "mapeditor.json"), @"{ ""format"": 1, ""game_type"": ""RA"", ""overlays"": [
                { ""id"": 100, ""name"": ""xore"", ""display_name"": ""Blue Ore"", ""graphics_source"": ""gold01"", ""flags"": [""TiberiumOrGold""] } ] }");
            return mod;
        }

        [Fact]
        public void AModOverlayLoadsAsKnownAndKeepsItsIdThroughASave()
        {
            EditorSession session = new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { ModWithOreLikeOverlay() });
            IGamePlugin plugin = session.Load(MapWithOverlayId(100), out _);
            Assert.Equal("xore", plugin.Map.Overlay[Cell]?.Type.Name);
            Assert.Equal("OreMod", plugin.Map.Overlay[Cell].Type.ModSource);
            Assert.DoesNotContain(plugin.Map.UnknownEntries, u => u.Section == "OverlayPack");

            string outPath = TestPaths.Output("mod-overlay-saved.ini");
            plugin.Save(outPath, FileType.INI);
            INI saved = new INI();
            saved.Parse(File.ReadAllText(outPath));
            bool modified = false;
            byte[] savedOverlay = INITools.DecompressLCWSection(saved["OverlayPack"], new CellMetrics(128, 128), 1, new List<string>(), ref modified);
            Assert.Equal(100, savedOverlay[Cell]);
        }
    }
}
