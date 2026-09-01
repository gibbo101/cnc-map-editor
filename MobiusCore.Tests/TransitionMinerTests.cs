using System.Collections.Generic;
using System.Drawing;
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
    /// The transition miner reads construction idioms out of existing maps: piece instances
    /// are reconstructed from the template grid, and touching pieces become transition
    /// observations. The official 230-map set is the corpus the coast walker will learn from.
    /// </summary>
    public class TransitionMinerTests
    {
        private readonly ITestOutputHelper output;
        public TransitionMinerTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void ReconstructsPlacedInstancesFromTheGrid()
        {
            IGamePlugin plugin = EditorHost.Shared.Load(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), out _);
            Map map = plugin.Map;
            // Stamp a known multicell piece and expect exactly one instance at its origin.
            TemplateType shore = map.TemplateTypes.First(t => t.Name == "sh06" && t.ExistsInTheater || t.Name == "sh06");
            var undo = new Dictionary<int, Template>();
            var redo = new Dictionary<int, Template>();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, shore, new Point(50, 50), null,
                new MobiusEditor.Utility.DeterministicRandom(1), undo, redo);
            Dictionary<Point, TemplateType> instances = TransitionMiner.Instances(map);
            Assert.Equal(shore, instances[new Point(50, 50)]);
        }

        [Fact]
        [Trait("Category", "Oracle")]
        public void MinesShoreIdiomsFromTheOfficialCorpus()
        {
            string officialDir = Path.Combine(TestPaths.RepoRoot, "artifacts", "test-output", "official");
            Assert.True(Directory.Exists(officialDir), "official extraction missing; run OfficialMapsTests first");
            var table = new Dictionary<(string, string, Point), PieceTransition>();
            int mapsWithShores = 0;
            foreach (string mapPath in Directory.EnumerateFiles(officialDir, "scm*.ini").OrderBy(f => f))
            {
                IGamePlugin plugin = EditorHost.Shared.Load(mapPath, out _);
                int before = table.Values.Sum(t => t.Count);
                // Every family the walker will ever draw with: beaches, water cliffs,
                // rivers, land cliffs (rock faces), roads — and the crossing pieces
                // (fords, bridges, falls), whose alignments only the corpus knows.
                TransitionMiner.Mine(plugin.Map, new[] { "sh", "wc", "rv", "rc", "rf", "d", "ford", "bridge", "br", "falls" }, table);
                if (table.Values.Sum(t => t.Count) > before) mapsWithShores++;
            }
            // Bake the corpus for the generator: mined statistics about piece adjacency
            // (names, offsets, counts) — analytical metadata, not map content.
            string bakedPath = TestPaths.Output("mined-transitions.txt");
            File.WriteAllText(bakedPath, TransitionMiner.Serialize(table.Values));
            output.WriteLine("baked: " + bakedPath);
            output.WriteLine($"maps contributing shore transitions: {mapsWithShores}");
            output.WriteLine($"distinct transitions: {table.Count}");
            foreach (PieceTransition t in table.Values.OrderByDescending(t => t.Count).Take(40))
            {
                output.WriteLine(t.ToString());
            }
            // The cliff-to-beach splices: how official maps interrupt a wc cliff coast with
            // sh landing beaches (the amphibious-landing idiom the walker must reproduce).
            var crossFamily = table.Values
                .Where(t => t.From.StartsWith("wc") != t.To.StartsWith("wc"))
                .OrderByDescending(t => t.Count)
                .Take(25)
                .ToList();
            output.WriteLine($"cross-family (cliff<->beach) transitions: {table.Values.Count(t => t.From.StartsWith("wc") != t.To.StartsWith("wc"))}");
            foreach (PieceTransition t in crossFamily)
            {
                output.WriteLine("splice: " + t);
            }
            // The corpus must be rich enough to walk from: hundreds of distinct idioms.
            Assert.True(table.Count > 200, "corpus too thin: " + table.Count);
        }
    }
}
