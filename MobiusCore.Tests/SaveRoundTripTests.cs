using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;
using Xunit.Abstractions;

namespace MobiusCore.Tests
{
    /// <summary>
    /// Loading and saving a map must produce exactly what the mono-hosted editor produces
    /// (oracle/saves, from oracle/regen.sh). Covers the INI writer, LCW/UUBlock packing,
    /// [MapPack]/[OverlayPack] and the mod's [TFTDTiles] layer.
    /// </summary>
    [Trait("Category", "Oracle")]
    public class SaveRoundTripTests
    {
        private readonly ITestOutputHelper output;
        public SaveRoundTripTests(ITestOutputHelper output) { this.output = output; }

        public static IEnumerable<object[]> CustomMaps()
        {
            string dir = Path.Combine(TestPaths.ModDir, "CustomMaps");
            if (!Directory.Exists(dir)) yield break;
            foreach (string f in Directory.GetFiles(dir, "*.MPR").OrderBy(f => f)) yield return new object[] { Path.GetFileName(f) };
        }

        public static IEnumerable<object[]> OfficialMaps()
        {
            string dir = TestPaths.Oracle("saves-official");
            if (!Directory.Exists(dir)) yield break;
            foreach (string f in Directory.GetFiles(dir, "*.ini").OrderBy(f => f)) yield return new object[] { Path.GetFileName(f) };
        }

        [Theory]
        [MemberData(nameof(OfficialMaps))]
        public void SavedOfficialMapIsByteIdenticalToTheMonoEditorsSave(string mapName)
        {
            string extracted = TestPaths.Output("official");
            if (!File.Exists(Path.Combine(extracted, mapName))) MobiusEditor.Headless.OfficialMaps.Extract(TestPaths.GameDir, extracted);
            RoundTrip(Path.Combine(extracted, mapName), TestPaths.Oracle(Path.Combine("saves-official", mapName)), mapName);
        }

        [Theory]
        [MemberData(nameof(CustomMaps))]
        public void SavedMapIsByteIdenticalToTheMonoEditorsSave(string mapName)
        {
            RoundTrip(Path.Combine(TestPaths.ModDir, "CustomMaps", mapName), TestPaths.Oracle(Path.Combine("saves", mapName)), mapName);
        }

        private void RoundTrip(string mapPath, string oraclePath, string mapName)
        {
            Assert.True(File.Exists(oraclePath), "Oracle save missing; run oracle/regen.sh: " + oraclePath);
            EditorSession host = EditorHost.Shared;
            IGamePlugin plugin = host.Load(mapPath, out string[] errors);
            output.WriteLine("load errors: " + errors.Length);
            foreach (string error in errors) output.WriteLine("  " + error);
            string outPath = TestPaths.Output(Path.Combine("saves", mapName));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            plugin.Save(outPath, FileType.INI);
            byte[] expected = File.ReadAllBytes(oraclePath), actual = File.ReadAllBytes(outPath);
            if (!expected.SequenceEqual(actual))
            {
                string[] e = File.ReadAllLines(oraclePath), a = File.ReadAllLines(outPath);
                int line = Enumerable.Range(0, Math.Min(e.Length, a.Length)).FirstOrDefault(i => e[i] != a[i], Math.Min(e.Length, a.Length));
                string detail = $"{mapName}: {expected.Length} vs {actual.Length} bytes; first differing line {line + 1}: expected '{(line < e.Length ? e[line] : "<eof>")}' got '{(line < a.Length ? a[line] : "<eof>")}'";
                output.WriteLine(detail);
                Assert.Fail(detail);
            }
        }
    }
}
