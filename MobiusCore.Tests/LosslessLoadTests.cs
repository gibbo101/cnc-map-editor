using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
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
            string[] lines = File.ReadAllLines(Path.Combine(TestPaths.MapEdits, "scm111ea.ini"));
            string path = TestPaths.Output("unknown-structure.ini");
            File.WriteAllLines(path, lines.Append("").Append("[STRUCTURES]").Append("0=Spain,ZZUNKNOWN,256,4500,0,None,1,1"));
            return path;
        }

        private static string FixtureWithUnknownObjects()
        {
            string[] lines = File.ReadAllLines(Path.Combine(TestPaths.MapEdits, "scm111ea.ini"));
            string path = TestPaths.Output("unknown-objects.ini");
            File.WriteAllLines(path, lines
                .Append("").Append("[UNITS]").Append("0=Spain,ZZUNIT,256,4501,0,Guard,None")
                .Append("").Append("[INFANTRY]").Append("0=Spain,ZZINF,256,4502,0,Guard,0,None")
                .Append("").Append("[TERRAIN]").Append("4503=ZZTREE"));
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
