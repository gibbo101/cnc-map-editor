using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// The official RA skirmish maps live in MAIN.MIX → general.mix: 230 files (130 numeric,
    /// 100 letter-coded Counterstrike/Aftermath), of which the game's [Missions] list shows 124.
    /// The core must read them out unchanged.
    /// </summary>
    [Trait("Category", "Oracle")]
    public class OfficialMapsTests
    {
        [Fact]
        public void ExtractsEveryOfficialSkirmishMap()
        {
            string outDir = TestPaths.Output("official");
            string[] names = OfficialMaps.Extract(TestPaths.GameDir, outDir);
            Assert.Equal(230, names.Length);
            string[] listed = File.ReadAllLines(Path.Combine(TestPaths.MapEdits, "_official_map_list.ini"))
                .Where(l => l.StartsWith("SCM", System.StringComparison.OrdinalIgnoreCase) && l.Contains("="))
                .Select(l => l.Substring(0, l.IndexOf('=')).ToLowerInvariant()).ToArray();
            Assert.Equal(124, listed.Length);
            Assert.Empty(listed.Except(names));
            foreach (string known in new[] { "scm05ea.ini", "scm111ea.ini" })
            {
                byte[] expected = File.ReadAllBytes(Path.Combine(TestPaths.MapEdits, known));
                byte[] actual = File.ReadAllBytes(Path.Combine(outDir, known));
                Assert.True(expected.SequenceEqual(actual), known + " differs from the map-edits copy");
            }
        }
    }
}
