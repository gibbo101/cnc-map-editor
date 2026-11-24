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

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 7)]
        [InlineData(2, 6)]
        [InlineData(129, 4)]
        [InlineData(1000, 3)]
        [InlineData(65535, 1)]
        public void LayoutsFollowTheCellHash(int cell, int layout)
        {
            OverlayType tib = new OverlayType(28, "tibts", "TS Tiberium", OverlayTypeFlag.Tiberium) { Layouts = 8 };
            Assert.Equal(5 + 12 * layout, tib.FrameFor(5, cell));
            Assert.Equal(5, new OverlayType(25, "tib01", "Tiberium", OverlayTypeFlag.Tiberium).FrameFor(5, cell));
        }

        [Fact]
        public void ACanvasOverlayDrawsPastItsCellCentredOnIt()
        {
            string mod = TestPaths.Output(Path.Combine("fake-mods-overlay", "CanvasMod"));
            Directory.CreateDirectory(Path.Combine(mod, "Data"));
            File.WriteAllText(Path.Combine(mod, "ccmod.json"), "{ \"name\": \"CanvasMod\", \"game_type\": \"RA\", \"version_high\": 1, \"version_low\": 0 }");
            File.WriteAllText(Path.Combine(mod, "mapeditor.json"), @"{ ""format"": 1, ""game_type"": ""RA"", ""overlays"": [
                { ""id"": 101, ""name"": ""xbig"", ""display_name"": ""Big"", ""graphics_source"": ""gold04"", ""flags"": [""Tiberium""], ""canvas"": 384 },
                { ""id"": 102, ""name"": ""xsmall"", ""display_name"": ""Small"", ""graphics_source"": ""gold04"", ""flags"": [""Tiberium""] } ] }");
            EditorSession session = new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { mod });
            IGamePlugin plugin = session.Load(Path.Combine(TestPaths.OfficialMapsDir, "scm111ea.ini"), out _);
            OverlayType big = plugin.Map.OverlayTypes.Single(t => t.Name == "xbig");
            OverlayType small = plugin.Map.OverlayTypes.Single(t => t.Name == "xsmall");
            // A 384 px canvas covers three cells, the middle one its own; one cell (128 px) stays within it.
            Assert.Equal(new System.Drawing.Rectangle(-128, -128, 384, 384), MobiusEditor.Render.MapRenderer.OverlayCanvas(big, new System.Drawing.Point(0, 0), 1.0));
            Assert.Equal(new System.Drawing.Rectangle(128, 64, 192, 192), MobiusEditor.Render.MapRenderer.OverlayCanvas(big, new System.Drawing.Point(3, 2), 0.5));
            Assert.Null(MobiusEditor.Render.MapRenderer.OverlayCanvas(small, new System.Drawing.Point(3, 2), 0.5));
        }

        [Fact]
        public void AModOverlayWithNoArtIsKeptAndDrawsNothing()
        {
            // A converted map's walls are drawn by the map's picture, so the overlay ships no art.
            string mod = TestPaths.Output(Path.Combine("fake-mods-overlay", "NoArtMod"));
            Directory.CreateDirectory(Path.Combine(mod, "Data"));
            File.WriteAllText(Path.Combine(mod, "ccmod.json"), "{ \"name\": \"NoArtMod\", \"game_type\": \"RA\", \"version_high\": 1, \"version_low\": 0 }");
            File.WriteAllText(Path.Combine(mod, "mapeditor.json"), @"{ ""format"": 1, ""game_type"": ""RA"", ""overlays"": [
                { ""id"": 103, ""name"": ""xpicwall"", ""display_name"": ""Picture Wall"" } ] }");
            EditorSession session = new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { mod });
            IGamePlugin plugin = session.Load(MapWithOverlayId(103), out string[] errors);
            Assert.Equal("xpicwall", plugin.Map.Overlay[Cell]?.Type.Name);
            Assert.DoesNotContain(errors, e => e.Contains("xpicwall"));
            Assert.DoesNotContain(session.New(null, out _).Map.OverlayTypes.Where(t => t.ExistsInTheater), t => t.Name == "xpicwall");

            string outPath = TestPaths.Output("no-art-overlay-saved.ini");
            plugin.Save(outPath, FileType.INI);
            INI saved = new INI();
            saved.Parse(File.ReadAllText(outPath));
            bool modified = false;
            Assert.Equal(103, INITools.DecompressLCWSection(saved["OverlayPack"], new CellMetrics(128, 128), 1, new List<string>(), ref modified)[Cell]);

            IGamePlugin reloaded = session.Load(outPath, out _);
            Assert.False(MobiusEditor.Render.MapRenderer.RenderOverlay(reloaded.GameInfo, new System.Drawing.Point(60, 60), null,
                new System.Drawing.Size(32, 32), 0.25, reloaded.Map.Overlay[Cell], false).RenderAction != null);
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
