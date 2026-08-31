using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// Sessions for both games coexist in one process (the test host holds one of each), and
    /// each load must run against its own session's state: a later session — or a load through
    /// the other game's session — must never starve a plugin of its mod types or leave the
    /// shared type tables initialised against the wrong game's archives.
    /// </summary>
    public class ModManifestSessionTests
    {
        [Fact]
        public void RaManifestsSurviveALaterTiberianDawnSession()
        {
            EditorSession ra = EditorHost.Shared;
            // Constructed after the RA session; ships no manifests of its own.
            EditorHost.SharedFor(GameType.TiberianDawn);
            string map = Directory.EnumerateFiles(Path.Combine(TestPaths.ModDir, "CustomMaps"), "*.MPR").OrderBy(f => f).First();
            IGamePlugin plugin = ra.Load(map, out _);
            Assert.Empty(plugin.Map.UnknownEntries);
        }

        [Fact]
        public void RaTheaterContentSurvivesATiberianDawnLoadInBetween()
        {
            // scm06ea places five crater smudges; smudge availability is probed against the
            // active archive manager, so a TD load leaving its own archives behind starves
            // the RA load's theater check and the craters silently vanish from the save.
            EditorSession ra = EditorHost.Shared;
            string extracted = TestPaths.Output("official");
            if (!File.Exists(Path.Combine(extracted, "scm06ea.ini"))) MobiusEditor.Headless.OfficialMaps.Extract(TestPaths.GameDir, extracted);
            EditorSession td = EditorHost.SharedFor(GameType.TiberianDawn);
            td.Load(TiberianDawnSessionTests.CommunityMap, out _);
            IGamePlugin plugin = ra.Load(Path.Combine(extracted, "scm06ea.ini"), out string[] errors);
            Assert.Empty(errors);
            Assert.Equal(5, plugin.Map.Smudge.Count());
        }
    }
}
