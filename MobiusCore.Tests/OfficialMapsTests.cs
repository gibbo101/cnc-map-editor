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
            // map-edits/scm111ea.ini is a pristine extract (scm05ea.ini there is an editor-saved copy).
            byte[] expected = File.ReadAllBytes(Path.Combine(TestPaths.MapEdits, "scm111ea.ini"));
            byte[] actual = File.ReadAllBytes(Path.Combine(outDir, "scm111ea.ini"));
            Assert.True(expected.SequenceEqual(actual), "scm111ea.ini differs from the pristine map-edits copy");
        }
    }
}
