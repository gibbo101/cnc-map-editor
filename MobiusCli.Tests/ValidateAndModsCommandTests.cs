using System.IO;
using MobiusCli;
using MobiusCore.Tests;
using Xunit;

namespace MobiusCli.Tests
{
    public class ValidateAndModsCommandTests
    {
        private static (int code, string stdout, string stderr) Run(params string[] args)
        {
            StringWriter o = new StringWriter(), e = new StringWriter();
            int code = Cli.Run(args, o, e);
            return (code, o.ToString(), e.ToString());
        }

        [Fact]
        public void CleanOfficialMapValidates()
        {
            (int code, string stdout, string stderr) = Run("validate", Path.Combine(TestPaths.MapEdits, "scm111ea.ini"), "--game", TestPaths.GameDir);
            Assert.True(code == 0, stderr + stdout);
            Assert.Contains("ok", stdout);
        }

        [Fact]
        public void LegacyConversionsAreNotesNotProblems()
        {
            string official = TestPaths.Output("official");
            if (!File.Exists(Path.Combine(official, "scm01ea.ini"))) MobiusEditor.Headless.OfficialMaps.Extract(TestPaths.GameDir, official);
            (int code, string stdout, string stderr) = Run("validate", Path.Combine(official, "scm01ea.ini"), "--game", TestPaths.GameDir);
            Assert.True(code == 0, stderr + stdout);
            Assert.Contains("note: ", stdout);
            Assert.Contains("ok (2 note(s))", stdout);
        }

        [Fact]
        public void MapWithUnknownEntryFailsValidation()
        {
            string[] lines = File.ReadAllLines(Path.Combine(TestPaths.MapEdits, "scm111ea.ini"));
            string path = TestPaths.Output("cli-validate-unknown.ini");
            File.WriteAllLines(path, System.Linq.Enumerable.Concat(lines, new[] { "", "[STRUCTURES]", "0=Spain,ZZUNKNOWN,256,4500,0,None,1,1" }));
            (int code, string stdout, string _) = Run("validate", path, "--game", TestPaths.GameDir);
            Assert.Equal(1, code);
            Assert.Contains("unknown: [STRUCTURES]", stdout);
        }

        [Fact]
        public void ModsListsAModsRootInLoadOrder()
        {
            string root = TestPaths.Output("cli-fake-mods");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(Path.Combine(root, "Red_Alert", "Second"));
            Directory.CreateDirectory(Path.Combine(root, "Red_Alert", "First"));
            File.WriteAllText(Path.Combine(root, "Red_Alert", "Second", "ccmod.json"), "{ \"name\": \"Second Mod\", \"load_order\": 5, \"version_high\": 1, \"version_low\": 0, \"game_type\": \"RA\" }");
            File.WriteAllText(Path.Combine(root, "Red_Alert", "First", "ccmod.json"), "{ \"name\": \"First Mod\", \"load_order\": 1, \"version_high\": 2, \"version_low\": 1, \"game_type\": \"RA\" }");
            (int code, string stdout, string stderr) = Run("mods", "--mods-root", root, "--no-workshop");
            Assert.True(code == 0, stderr);
            Assert.True(stdout.IndexOf("First Mod (2.1)") < stdout.IndexOf("Second Mod (1.0)"), stdout);
            Assert.Contains("2 mod(s)", stdout);
        }
    }
}
