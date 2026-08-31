using System.IO;
using MobiusCli;
using MobiusCore.Tests;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCli.Tests
{
    /// <summary>
    /// `new` writes a fresh empty map scaffolded with two player-start waypoints (so it
    /// satisfies the game's skirmish save rules immediately), refuses to overwrite, and the
    /// result loads back cleanly with the chosen theater.
    /// </summary>
    public class NewCommandTests
    {
        private static (int code, string stdout, string stderr) Run(params string[] args)
        {
            StringWriter o = new StringWriter(), e = new StringWriter();
            int code = Cli.Run(args, o, e);
            return (code, o.ToString(), e.ToString());
        }

        [Fact]
        public void WritesALoadableMapWithTheChosenTheater()
        {
            string outPath = TestPaths.Output("cli-new.mpr");
            File.Delete(outPath);
            (int code, string stdout, string stderr) = Run("new", outPath, "--theater", "Snow", "--game", TestPaths.GameDir);
            Assert.True(code == 0, stderr);
            Assert.Contains("Snow", stdout);
            IGamePlugin plugin = EditorHost.Shared.Load(outPath, out string[] errors);
            Assert.Empty(errors);
            Assert.Equal("Snow", plugin.Map.Theater.Name);
            Assert.Equal(2, System.Linq.Enumerable.Count(plugin.Map.Waypoints, w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue));
        }

        [Fact]
        public void RefusesToOverwriteAnExistingFile()
        {
            string outPath = TestPaths.Output("cli-new-existing.mpr");
            File.WriteAllText(outPath, "already here");
            (int code, string _, string stderr) = Run("new", outPath, "--game", TestPaths.GameDir);
            Assert.NotEqual(0, code);
            Assert.Contains("overwrite", stderr);
            Assert.Equal("already here", File.ReadAllText(outPath));
        }
    }
}
