using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MobiusEditor.Headless;
using MobiusEditor.Utility;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// The official RA skirmish maps live in MAIN.MIX → general.mix: 230 files (130 numeric,
    /// 100 letter-coded Counterstrike/Aftermath), named by missions.pkt (124) and cstrike.pkt
    /// (106). The core must find every one and read it out unchanged.
    /// </summary>
    [Trait("Category", "Oracle")]
    public class OfficialMapsTests
    {
        [Fact]
        public void ExtractsEveryOfficialSkirmishMap()
        {
            string outDir = TestPaths.Output("official-extract-check");
            string[] names = OfficialMaps.Extract(TestPaths.GameDir, outDir);
            Assert.Equal(230, names.Length);
            string[] missions = PacketMissions("missions.pkt");
            string[] cstrike = PacketMissions("cstrike.pkt");
            Assert.Equal(124, missions.Length);
            Assert.Equal(106, cstrike.Length);
            Assert.Equal(names.OrderBy(n => n), missions.Union(cstrike).OrderBy(n => n));
            // SHA-256 of the file as the game ships it.
            byte[] scm111 = File.ReadAllBytes(Path.Combine(outDir, "scm111ea.ini"));
            Assert.Equal("2b4af7a44ee75c52ae814b8ad39e18830531716f273c7b7020cc57095ee73c39",
                System.Convert.ToHexString(SHA256.HashData(scm111)).ToLowerInvariant());
        }

        private static string[] PacketMissions(string packet)
        {
            using (MixFile main = new MixFile(Path.Combine(TestPaths.GameDir, OfficialMaps.MainMixRelative)))
            using (MixFile general = new MixFile(main, "general.mix"))
            {
                string[] lines = Encoding.ASCII.GetString(general.ReadFile(packet)).Split('\n');
                return lines.Select(l => l.Trim())
                    .Where(l => l.StartsWith("SCM", System.StringComparison.OrdinalIgnoreCase) && l.Contains('='))
                    .Select(l => l.Substring(0, l.IndexOf('=')).ToLowerInvariant()).ToArray();
            }
        }
    }
}
