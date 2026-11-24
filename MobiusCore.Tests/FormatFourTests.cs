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

        [Fact]
        public void AFormatThreeMapStaysOnTheOneTwentyEightGrid()
        {
            IGamePlugin plugin = EditorHost.Shared.Load(Path.Combine(TestPaths.OfficialMapsDir, "scm01ea.ini"), out _);
            Assert.Equal(128, plugin.Map.Metrics.Width);
        }
    }
}
