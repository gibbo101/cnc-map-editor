using System.IO;
using System.Linq;
using MobiusCli;
using MobiusCore.Tests;
using MobiusEditor.Interface;
using Xunit;

namespace MobiusCli.Tests
{
    /// <summary>
    /// `expand-mission` applies a JSON mission spec (patterns + raw rows) to a map and writes
    /// the result to --out, never the input. One-time scaffolding: the expansion is refused
    /// entirely — nothing written — when any pattern fails to build or the expanded triggers
    /// fail the game's trigger check with fatals.
    /// </summary>
    public class ExpandMissionCommandTests
    {
        private static (int code, string stdout, string stderr) Run(params string[] args)
        {
            StringWriter o = new StringWriter(), e = new StringWriter();
            int code = Cli.Run(args, o, e);
            return (code, o.ToString(), e.ToString());
        }

        private static readonly string Source = Path.Combine(TestPaths.MapEdits, "scm05ea.ini");

        private static string WriteSpec(string name, string json)
        {
            string dir = TestPaths.Output("cli-expand");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void ExpandsPatternsAndWritesTheNewMap()
        {
            string spec = WriteSpec("spec.json", @"{
                // one win condition and one reinforcement
                ""patterns"": [
                    { ""pattern"": ""win"", ""name"": ""win1"", ""house"": ""USSR"", ""enemy"": ""Greece"" },
                    { ""pattern"": ""reinforce"", ""name"": ""rnf1"", ""house"": ""USSR"",
                      ""units"": [""E1:3""], ""at"": 4, ""after"": 30 }
                ] }");
            string outPath = Path.Combine(TestPaths.Output("cli-expand"), "expanded.ini");
            (int code, string stdout, string stderr) = Run("expand-mission", Source, spec, "--out", outPath,
                "--game", TestPaths.GameDir);
            Assert.True(code == 0, stderr);
            Assert.Contains("2 pattern(s)", stdout);
            IGamePlugin plugin = EditorHost.Shared.Load(outPath, out _);
            Assert.NotNull(plugin.Map.Triggers.SingleOrDefault(t => t.Name == "win1"));
            Assert.NotNull(plugin.Map.Triggers.SingleOrDefault(t => t.Name == "rnf1"));
            Assert.NotNull(plugin.Map.TeamTypes.SingleOrDefault(t => t.Name == "rnf1"));
        }

        [Fact]
        public void RefusesToOverwriteTheInput()
        {
            string spec = WriteSpec("spec-same.json", @"{ ""patterns"": [] }");
            (int code, string _, string stderr) = Run("expand-mission", Source, spec, "--out", Source,
                "--game", TestPaths.GameDir);
            Assert.NotEqual(0, code);
            Assert.Contains("input", stderr);
        }

        [Fact]
        public void BadSpecWritesNothingAndReportsTheErrors()
        {
            string spec = WriteSpec("spec-bad.json", @"{ ""patterns"": [
                { ""pattern"": ""lose"", ""name"": ""lose"", ""house"": ""Klingons"" } ] }");
            string outPath = Path.Combine(TestPaths.Output("cli-expand"), "never-written.ini");
            File.Delete(outPath);
            (int code, string _, string stderr) = Run("expand-mission", Source, spec, "--out", outPath,
                "--game", TestPaths.GameDir);
            Assert.NotEqual(0, code);
            Assert.Contains("Klingons", stderr);
            Assert.False(File.Exists(outPath));
        }
    }
}
