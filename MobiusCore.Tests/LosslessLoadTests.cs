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
    /// Unknown ≠ delete. Entities the active profile does not know (a mod's objects opened without
    /// the mod, a newer game's types) must survive load and save untouched and be reported, never
    /// silently dropped.
    /// </summary>
    public class LosslessLoadTests
    {
        private static string FixtureWithUnknownStructure()
        {
            string[] lines = File.ReadAllLines(Path.Combine(TestPaths.OfficialMapsDir, "scm111ea.ini"));
            string path = TestPaths.Output("unknown-structure.ini");
            File.WriteAllLines(path, lines.Append("").Append("[STRUCTURES]").Append("0=Spain,ZZUNKNOWN,256,4500,0,None,1,1"));
            return path;
        }

        private static string FixtureWithUnknownObjects()
        {
            // The fixture map already has a [TERRAIN] section (a duplicate would be discarded), so inject into it.
            System.Collections.Generic.List<string> lines = File.ReadAllLines(Path.Combine(TestPaths.OfficialMapsDir, "scm111ea.ini")).ToList();
            int terrain = lines.IndexOf("[TERRAIN]");
            Assert.True(terrain >= 0, "fixture has no [TERRAIN] section");
            lines.Insert(terrain + 1, "4503=ZZTREE");
            lines.AddRange(new[] { "", "[UNITS]", "0=Spain,ZZUNIT,256,4501,0,Guard,None", "", "[INFANTRY]", "0=Spain,ZZINF,256,4502,0,Guard,0,None" });
            string path = TestPaths.Output("unknown-objects.ini");
            File.WriteAllLines(path, lines);
            return path;
        }

        [Theory]
        [InlineData("UNITS", "ZZUNIT", "=Spain,ZZUNIT,256,4501,0,Guard,None")]
        [InlineData("INFANTRY", "ZZINF", "=Spain,ZZINF,256,4502,0,Guard,0,None")]
        [InlineData("TERRAIN", "ZZTREE", "4503=ZZTREE")]
        public void UnknownObjectsOfEveryKindSurvive(string section, string typeName, string savedLineSuffix)
        {
            EditorSession session = EditorHost.Shared;
            IGamePlugin plugin = session.Load(FixtureWithUnknownObjects(), out string[] errors);
            Assert.Contains(errors, e => e.Contains(typeName));
            Assert.Contains(plugin.Map.UnknownEntries, u => u.Section == section && u.Value.Contains(typeName));
            string outPath = TestPaths.Output("unknown-objects-saved.ini");
            plugin.Save(outPath, FileType.INI);
            string[] saved = File.ReadAllLines(outPath);
            int idx = System.Array.IndexOf(saved, "[" + section + "]");
            Assert.True(idx >= 0, "no [" + section + "] in saved map");
            Assert.Contains(saved.Skip(idx + 1).TakeWhile(l => !l.StartsWith("[")), l => l.EndsWith(savedLineSuffix));
        }

        [Fact]
        public void UnknownOverlayIdsSurviveSaveAndAreReported()
        {
            INI ini = new INI();
            ini.Parse(File.ReadAllText(Path.Combine(TestPaths.OfficialMapsDir, "scm111ea.ini")));
            CellMetrics metrics = new CellMetrics(128, 128);
            bool modified = false;
            byte[] overlay = INITools.DecompressLCWSection(ini["OverlayPack"], metrics, 1, new System.Collections.Generic.List<string>(), ref modified);
            const int cell = 60 * 128 + 60;
            overlay[cell] = 100;
            ini.Sections.Remove("OverlayPack");
            INITools.CompressLCWSection(ini.Sections.Add("OverlayPack"), overlay);
            string path = TestPaths.Output("unknown-overlay.ini");
            File.WriteAllText(path, ini.ToString());

            IGamePlugin plugin = EditorHost.Shared.Load(path, out _);
            Assert.Contains(plugin.Map.UnknownEntries, u => u.Section == "OverlayPack" && u.Key == cell.ToString() && u.Value == "100");
            string outPath = TestPaths.Output("unknown-overlay-saved.ini");
            plugin.Save(outPath, FileType.INI);
            INI saved = new INI();
            saved.Parse(File.ReadAllText(outPath));
            byte[] savedOverlay = INITools.DecompressLCWSection(saved["OverlayPack"], metrics, 1, new System.Collections.Generic.List<string>(), ref modified);
            Assert.Equal(100, savedOverlay[cell]);
        }

        [Fact]
        public void AnUnknownSectionSurvivesASaveVerbatim()
        {
            // TF's TS maps link tunnel mouths as "n=enter cell,exit cell,steps".
            string[] lines = File.ReadAllLines(Path.Combine(TestPaths.OfficialMapsDir, "scm111ea.ini"));
            string path = TestPaths.Output("unknown-section.ini");
            File.WriteAllLines(path, lines.Concat(new[] { "", "[TFTubes]", "0=4500,4620,7", "1=4620,4500,7" }));
            IGamePlugin plugin = EditorHost.Shared.Load(path, out _);
            string outPath = TestPaths.Output("unknown-section-saved.ini");
            plugin.Save(outPath, FileType.INI);
            string[] saved = File.ReadAllLines(outPath);
            int idx = System.Array.IndexOf(saved, "[TFTubes]");
            Assert.True(idx >= 0, "no [TFTubes] in saved map");
            Assert.Equal(new[] { "0=4500,4620,7", "1=4620,4500,7" }, saved.Skip(idx + 1).Take(2).ToArray());
        }

        [Fact]
        public void ATsMapsTunnelsSurviveASave()
        {
            string map = TestPaths.Oracle(Path.Combine("inputs", "ts-theatres", "CCDATA", "TFTS1014.INI"));
            string[] Tubes(string path)
            {
                string[] lines = File.ReadAllLines(path);
                int idx = System.Array.IndexOf(lines, "[TFTubes]");
                Assert.True(idx >= 0, "no [TFTubes] in " + path);
                return lines.Skip(idx + 1).TakeWhile(l => l.Length > 0 && !l.StartsWith("[")).ToArray();
            }
            IGamePlugin plugin = EditorHost.Shared.Load(map, out _);
            string outPath = TestPaths.Output("tfts1014-saved.ini");
            plugin.Save(outPath, FileType.INI);
            Assert.NotEmpty(Tubes(map));
            Assert.Equal(Tubes(map), Tubes(outPath));
        }

        [Fact]
        public void UnknownStructureIsReportedNotDropped()
        {
            EditorSession session = EditorHost.Shared;
            IGamePlugin plugin = session.Load(FixtureWithUnknownStructure(), out string[] errors);
            Assert.Contains(errors, e => e.Contains("ZZUNKNOWN"));
            Assert.Contains(plugin.Map.UnknownEntries, u => u.Section == "STRUCTURES" && u.Value.Contains("ZZUNKNOWN"));
        }

        [Fact]
        public void UnknownStructureSurvivesSave()
        {
            EditorSession session = EditorHost.Shared;
            IGamePlugin plugin = session.Load(FixtureWithUnknownStructure(), out _);
            string outPath = TestPaths.Output("unknown-structure-saved.ini");
            plugin.Save(outPath, FileType.INI);
            string[] saved = File.ReadAllLines(outPath);
            int idx = System.Array.IndexOf(saved, "[STRUCTURES]");
            Assert.True(idx >= 0);
            Assert.Contains(saved.Skip(idx + 1).TakeWhile(l => !l.StartsWith("[")), l => l.EndsWith("=Spain,ZZUNKNOWN,256,4500,0,None,1,1"));
        }
    }
}
