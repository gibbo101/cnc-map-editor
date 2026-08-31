using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// EditorSession.New creates a fresh empty map — chosen theater, full playable bounds,
    /// first house as the player — that saves and reloads cleanly once it satisfies the
    /// game's save rules (a skirmish map needs two player-start waypoints; without them the
    /// plugin's Validate blocks the save, which the test pins too).
    /// </summary>
    public class NewMapTests
    {
        private static void PlaceStartWaypoints(IGamePlugin plugin)
        {
            Waypoint[] starts = plugin.Map.Waypoints.Where(w => w.Flags.HasFlag(WaypointFlag.PlayerStart)).Take(2).ToArray();
            plugin.Map.Metrics.GetCell(new System.Drawing.Point(plugin.Map.Bounds.Left + 2, plugin.Map.Bounds.Top + 2), out int a);
            plugin.Map.Metrics.GetCell(new System.Drawing.Point(plugin.Map.Bounds.Right - 2, plugin.Map.Bounds.Bottom - 2), out int b);
            starts[0].Cell = a;
            starts[1].Cell = b;
        }

        [Fact]
        public void NewRedAlertMapSavesAndReloadsOnceItHasStartPoints()
        {
            EditorSession session = EditorHost.Shared;
            IGamePlugin plugin = session.New("Snow", out string[] errors);
            Assert.Equal("Snow", plugin.Map.Theater.Name);
            Assert.Equal(new System.Drawing.Point(1, 1), plugin.Map.TopLeft);
            Assert.Equal(plugin.Map.HouseTypes.First().Name, plugin.Map.BasicSection.Player);
            // The game refuses a skirmish map without two start waypoints.
            Assert.Contains("starting locations", plugin.Validate(FileType.INI, false, false) ?? "");
            PlaceStartWaypoints(plugin);
            Assert.Null(plugin.Validate(FileType.INI, false, false));
            string outDir = TestPaths.Output("new-map");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "fresh.mpr");
            plugin.Save(outPath, FileType.INI);
            IGamePlugin reloaded = session.Load(outPath, out string[] loadErrors);
            Assert.Empty(loadErrors);
            Assert.Equal("Snow", reloaded.Map.Theater.Name);
            Assert.Equal(plugin.Map.Bounds, reloaded.Map.Bounds);
            Assert.Empty(reloaded.Map.Triggers);
        }

        [Fact]
        public void UnknownTheaterFallsBackToTheGamesFirst()
        {
            EditorSession session = EditorHost.Shared;
            IGamePlugin plugin = session.New("Moonscape", out _);
            Assert.Equal(session.GameInfo.AllTheaters.First().Name, plugin.Map.Theater.Name);
        }

        [Fact]
        public void NewTiberianDawnMapSavesWithItsBin()
        {
            EditorSession session = EditorHost.SharedFor(GameType.TiberianDawn);
            IGamePlugin plugin = session.New(null, out _);
            PlaceStartWaypoints(plugin);
            string outDir = TestPaths.Output("new-map");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "freshtd.ini");
            plugin.Save(outPath, FileType.INI);
            Assert.True(File.Exists(outPath));
            Assert.True(File.Exists(Path.ChangeExtension(outPath, ".bin")), "TD save should write the terrain .bin");
            IGamePlugin reloaded = session.Load(outPath, out _);
            Assert.Equal(plugin.Map.Theater.Name, reloaded.Map.Theater.Name);
        }
    }
}
