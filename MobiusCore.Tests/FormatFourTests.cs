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
    /// A Red Alert map with NewINIFormat=4 lives on a 256-wide grid: its MapPack and OverlayPack
    /// carry 65,536 cells and its cell numbers are y*256+x. The editor loads it at its real size and
    /// saves it back as format 4; maps of format 3 and older stay on the 128 grid.
    /// </summary>
    public class FormatFourTests
    {
        private static string Crater => TestPaths.Oracle(Path.Combine("inputs", "ts-theatres", "CCDATA", "TFTS1005.INI"));
        private static readonly CellMetrics Grid256 = new CellMetrics(256, 256);

        private static EditorSession Session() =>
            new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { TestPaths.ModDir, TestPaths.Oracle(Path.Combine("inputs", "ts-theatres")) });

        private static byte[] Pack(string path, string section, int bytesPerCell)
        {
            INI ini = new INI();
            ini.Parse(File.ReadAllText(path));
            bool modified = false;
            return INITools.DecompressLCWSection(ini[section], Grid256, bytesPerCell, new List<string>(), ref modified);
        }

        [Fact]
        public void AFormatFourMapLoadsAtItsRealSize()
        {
            IGamePlugin plugin = Session().Load(Crater, out string[] errors);
            Map map = plugin.Map;
            Assert.Equal(256, map.Metrics.Width);
            Assert.Equal(256, map.Metrics.Height);
            Assert.Equal(new System.Drawing.Rectangle(43, 87, 169, 82), map.Bounds);
            Assert.DoesNotContain(errors, e => e.Contains("Illegal") || e.Contains("decompress") || e.Contains("out of range"));
            Assert.Equal(new[] { 28751, 41416, 39236, 25533 }, Enumerable.Range(0, 4).Select(i => map.Waypoints[i].Cell ?? -1).ToArray());
            Assert.NotEmpty(map.Templates);
        }

        [Fact]
        public void AFormatFourMapSavesBackAsFormatFourWithItsCellsUnchanged()
        {
            IGamePlugin plugin = Session().Load(Crater, out _);
            string outPath = TestPaths.Output("crater-saved.ini");
            plugin.Save(outPath, FileType.INI);
            string[] saved = File.ReadAllLines(outPath);
            Assert.Contains("NewINIFormat=4", saved);
            Assert.Equal(Pack(Crater, "MapPack", 3), Pack(outPath, "MapPack", 3));
            Assert.Equal(Pack(Crater, "OverlayPack", 1), Pack(outPath, "OverlayPack", 1));
            IGamePlugin reloaded = Session().Load(outPath, out _);
            Assert.Equal(plugin.Map.Bounds, reloaded.Map.Bounds);
            Assert.Equal(new[] { 28751, 41416, 39236, 25533 }, Enumerable.Range(0, 4).Select(i => reloaded.Map.Waypoints[i].Cell ?? -1).ToArray());
        }

        public static string GridMod()
        {
            string mod = TestPaths.Output(Path.Combine("fake-mods-grid", "GridMod"));
            Directory.CreateDirectory(Path.Combine(mod, "Data"));
            File.WriteAllText(Path.Combine(mod, "ccmod.json"), "{ \"name\": \"GridMod\", \"game_type\": \"RA\", \"version_high\": 1, \"version_low\": 0 }");
            File.WriteAllText(Path.Combine(mod, "mapeditor.json"), "{ \"format\": 1, \"game_type\": \"RA\", \"map\": { \"grid\": 256, \"ini_format\": 4 } }");
            return mod;
        }

        [Fact]
        public void ANewBigMapNeedsAModThatDeclaresTheGrid()
        {
            Assert.Throws<System.InvalidOperationException>(() => EditorHost.Shared.New(null, out _, bigGrid: true));

            EditorSession session = new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { GridMod() });
            Assert.Equal("GridMod", session.BigGridMod);
            IGamePlugin plugin = session.New(null, out _, bigGrid: true);
            Assert.Equal(256, plugin.Map.Metrics.Width);
            Map map = plugin.Map;
            Waypoint[] starts = map.Waypoints.Where(w => w.Flags.HasFlag(WaypointFlag.PlayerStart)).Take(2).ToArray();
            map.Metrics.GetCell(new System.Drawing.Point(200, 200), out int far);
            starts[0].Cell = far;
            map.Metrics.GetCell(new System.Drawing.Point(10, 10), out int near);
            starts[1].Cell = near;
            string outPath = TestPaths.Output("new-big.ini");
            plugin.Save(outPath, FileType.INI);
            Assert.Contains("NewINIFormat=4", File.ReadAllLines(outPath));
            IGamePlugin reloaded = session.Load(outPath, out _);
            Assert.Equal(256, reloaded.Map.Metrics.Width);
            Assert.Equal(far, reloaded.Map.Waypoints[0].Cell);
        }

        [Fact]
        public void AFormatThreeMapStaysOnTheOneTwentyEightGrid()
        {
            IGamePlugin plugin = EditorHost.Shared.Load(Path.Combine(TestPaths.OfficialMapsDir, "scm01ea.ini"), out _);
            Assert.Equal(128, plugin.Map.Metrics.Width);
        }
    }
}
