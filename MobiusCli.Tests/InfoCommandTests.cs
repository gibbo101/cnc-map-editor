using System.IO;
using MobiusCli;
using MobiusCore.Tests;
using Xunit;

namespace MobiusCli.Tests
{
    /// <summary>`info` describes a map without a display: game, theater, size, bounds and object counts.</summary>
    public class InfoCommandTests
    {
        private static (int code, string stdout, string stderr) Run(params string[] args)
        {
            StringWriter o = new StringWriter(), e = new StringWriter();
            int code = Cli.Run(args, o, e);
            return (code, o.ToString(), e.ToString());
        }

        [Fact]
        public void DescribesAnOfficialMap()
        {
            (int code, string stdout, string stderr) = Run("info", Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), "--game", TestPaths.GameDir);
            Assert.True(code == 0, stderr);
            Assert.Contains("game: RedAlert", stdout);
            Assert.Contains("theater: Temperate", stdout);
            Assert.Contains("size: 128x128", stdout);
            Assert.Contains("bounds: ", stdout);
            Assert.Contains("name: ", stdout);
            Assert.Contains("overlay: ", stdout);
        }

        [Fact]
        public void ListsUnknownEntries()
        {
            string[] lines = File.ReadAllLines(Path.Combine(TestPaths.MapEdits, "scm111ea.ini"));
            string path = TestPaths.Output("cli-unknown.ini");
            File.WriteAllLines(path, System.Linq.Enumerable.Concat(lines, new[] { "", "[STRUCTURES]", "0=Spain,ZZUNKNOWN,256,4500,0,None,1,1" }));
            (int code, string stdout, string stderr) = Run("info", path, "--game", TestPaths.GameDir);
            Assert.True(code == 0, stderr);
            Assert.Contains("unknown entries: 1", stdout);
            Assert.Contains("[STRUCTURES] 0=Spain,ZZUNKNOWN,256,4500,0,None,1,1 (unknown type ZZUNKNOWN)", stdout);
        }

        [Fact]
        public void DescribesATiberianDawnCommunityMap()
        {
            (int code, string stdout, string stderr) = Run("info", MobiusCore.Tests.TiberianDawnSessionTests.CommunityMap, "--game", TestPaths.GameDir, "--game-type", "TD");
            Assert.True(code == 0, stderr);
            Assert.Contains("game: TiberianDawn", stdout);
            Assert.Contains("theater: Desert", stdout);
            Assert.Contains("size: 64x64", stdout);
            Assert.Contains("bounds: 1,1 52x54", stdout);
            Assert.Contains("load errors: 0", stdout);
        }

        [Fact]
        public void ReportsWhichModsAMapNeeds()
        {
            string tfMap = System.Linq.Enumerable.First(System.Linq.Enumerable.OrderBy(
                Directory.EnumerateFiles(Path.Combine(TestPaths.ModDir, "CustomMaps"), "*.MPR"), f => f));
            (int code, string stdout, string stderr) = Run("info", tfMap, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir);
            Assert.True(code == 0, stderr);
            Assert.Contains("requires mods: ", stdout);
            Assert.Contains("Tiberian Factions", stdout);
        }

        [Fact]
        public void ReportsAnAllVanillaMapAsVanillaSafe()
        {
            // The mod is active, but the map places none of its types.
            (int code, string stdout, string stderr) = Run("info", Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir);
            Assert.True(code == 0, stderr);
            Assert.Contains("requires mods: none (vanilla-safe)", stdout);
        }

        [Fact]
        public void ReportsMissingMapAsAnError()
        {
            (int code, string _, string stderr) = Run("info", "/nonexistent/map.ini", "--game", TestPaths.GameDir);
            Assert.NotEqual(0, code);
            Assert.Contains("/nonexistent/map.ini", stderr);
        }

        [Fact]
        public void UnknownCommandIsAnError()
        {
            (int code, string _, string stderr) = Run("frobnicate");
            Assert.NotEqual(0, code);
            Assert.Contains("frobnicate", stderr);
        }
    }
}
