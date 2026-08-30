using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>Unknown ≠ delete holds for Tiberian Dawn maps too.</summary>
    public class LosslessLoadTdTests
    {
        private static string Fixture()
        {
            string src = TiberianDawnSessionTests.CommunityMap;
            string dir = TestPaths.Output("td-unknown");
            Directory.CreateDirectory(dir);
            string ini = Path.Combine(dir, "SCMC0EA.INI");
            File.Copy(Path.ChangeExtension(src, ".BIN"), Path.Combine(dir, "SCMC0EA.BIN"), true);
            System.Collections.Generic.List<string> lines = File.ReadAllLines(src).ToList();
            int structures = lines.FindIndex(l => l.Trim().Equals("[STRUCTURES]", System.StringComparison.OrdinalIgnoreCase));
            if (structures < 0) { lines.AddRange(new[] { "", "[STRUCTURES]" }); structures = lines.Count - 1; }
            lines.Insert(structures + 1, "0=GoodGuy,ZZUNKNOWN,256,1500,0,None");
            File.WriteAllLines(ini, lines);
            return ini;
        }

        [Fact]
        public void UnknownStructureSurvivesTdSave()
        {
            EditorSession session = EditorHost.SharedFor(GameType.TiberianDawn);
            IGamePlugin plugin = session.Load(Fixture(), out string[] errors);
            Assert.Contains(errors, e => e.Contains("ZZUNKNOWN"));
            Assert.Contains(plugin.Map.UnknownEntries, u => u.Section == "STRUCTURES" && u.Value.Contains("ZZUNKNOWN"));
            string outPath = TestPaths.Output(Path.Combine("td-unknown", "saved.ini"));
            plugin.Save(outPath, FileType.INI);
            string[] saved = File.ReadAllLines(outPath);
            int idx = System.Array.FindIndex(saved, l => l.Trim().Equals("[STRUCTURES]", System.StringComparison.OrdinalIgnoreCase));
            Assert.True(idx >= 0, "no [STRUCTURES] in saved map");
            Assert.Contains(saved.Skip(idx + 1).TakeWhile(l => !l.StartsWith("[")), l => l.EndsWith("=GoodGuy,ZZUNKNOWN,256,1500,0,None"));
        }
    }
}
