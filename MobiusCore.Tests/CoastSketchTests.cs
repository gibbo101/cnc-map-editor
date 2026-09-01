using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;
using Xunit;
using Xunit.Abstractions;

namespace MobiusCore.Tests
{
    /// <summary>Exploratory sketch: a walked cliff coastline across a fresh map, water flooded south.</summary>
    public class CoastSketchTests
    {
        private readonly ITestOutputHelper output;
        public CoastSketchTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void SketchAWalkedCoast()
        {
            IGamePlugin plugin = EditorHost.Shared.New("Temperate", out _);
            Map map = plugin.Map;
            var catalog = ShoreCatalog.Build(map.TemplateTypes)
                .Concat(ShoreCatalog.Build(map.TemplateTypes, "wc"))
                .ToDictionary(p => p.Template.Name, System.StringComparer.OrdinalIgnoreCase);
            DeterministicRandom random = new DeterministicRandom(7);
            // Anchors wiggle across the map west to east; each segment starts from the last placed piece.
            Point[] anchors = { new Point(30, 66), new Point(55, 58), new Point(80, 70), new Point(105, 62), new Point(map.Bounds.Right - 3, 64) };
            string piece = "wc02";
            Point origin = new Point(map.Bounds.Left, 64);
            List<CoastWalker.Step> all = new List<CoastWalker.Step>();
            foreach (Point goal in anchors)
            {
                List<CoastWalker.Step> segment = CoastWalker.PlanPath(map, TransitionGraph.Baked, catalog, piece, origin, goal, 60, desiredSide: 4);
                Assert.NotNull(segment);
                if (all.Count > 0) segment.RemoveAt(0);
                all.AddRange(segment);
                piece = all[^1].Piece.Name;
                origin = all[^1].Origin;
            }
            CoastWalker.ExtendToEdge(map, TransitionGraph.Baked, catalog, all, edge: 2, desiredSide: 4);
            output.WriteLine("pieces: " + all.Count);
            CoastWalker.Place(map, all, catalog, random);
            bool flooded = CoastWalker.FloodWater(map, new Point(60, 100), 8000, random);
            output.WriteLine("flooded: " + flooded);
            if (!flooded)
            {
                // Leak hunt: BFS the same region and report the first cell escaping north
                // of the chain, with the template contents around it.
                var seen = new HashSet<Point> { new Point(60, 100) };
                var frontier = new Queue<Point>();
                frontier.Enqueue(new Point(60, 100));
                int minChainY = all.Min(st => st.Origin.Y) - 2;
                while (frontier.Count > 0)
                {
                    Point c = frontier.Dequeue();
                    if (!map.Bounds.Contains(c)) continue;
                    if (map.Templates[c.Y, c.X]?.Type != null) continue;
                    if (c.Y < minChainY)
                    {
                        output.WriteLine($"LEAK reaches {c.X},{c.Y}");
                        break;
                    }
                    foreach (Point n in new[] { new Point(c.X + 1, c.Y), new Point(c.X - 1, c.Y), new Point(c.X, c.Y + 1), new Point(c.X, c.Y - 1) })
                    {
                        if (seen.Add(n)) frontier.Enqueue(n);
                    }
                }
                // And walk the chain listing consecutive pairs plus their gap columns.
                for (int i = 1; i < all.Count; i++)
                {
                    var pa = all[i - 1];
                    var pb = all[i];
                    output.WriteLine($"{pa.Piece.Name}@{pa.Origin.X},{pa.Origin.Y} ({pa.Piece.IconWidth}x{pa.Piece.IconHeight}) -> {pb.Piece.Name}@{pb.Origin.X},{pb.Origin.Y} ({pb.Piece.IconWidth}x{pb.Piece.IconHeight})");
                }
            }
            Waypoint[] starts = map.Waypoints.Where(w => w.Flags.HasFlag(WaypointFlag.PlayerStart)).Take(2).ToArray();
            map.Metrics.GetCell(new Point(10, 10), out int a);
            map.Metrics.GetCell(new Point(100, 20), out int b);
            starts[0].Cell = a;
            starts[1].Cell = b;
            string outPath = TestPaths.Output("coast-sketch.mpr");
            plugin.Save(outPath, FileType.INI);
            output.WriteLine("saved: " + outPath);
        }
    }
}
