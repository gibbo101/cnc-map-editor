using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;
using Xunit.Abstractions;

namespace MobiusCore.Tests
{
    /// <summary>
    /// The coast walker plans piece chains along corpus-mined transitions: deterministic
    /// A* that reaches its goal through idioms real mappers used, never flipping the
    /// water side by more than a quarter turn between neighbouring pieces.
    /// </summary>
    public class CoastWalkerTests
    {
        private readonly ITestOutputHelper output;
        public CoastWalkerTests(ITestOutputHelper output) { this.output = output; }

        private static (IGamePlugin plugin, IReadOnlyDictionary<string, ShorePiece> catalog) Fresh()
        {
            IGamePlugin plugin = EditorHost.Shared.New("Temperate", out _);
            var catalog = ShoreCatalog.Build(plugin.Map.TemplateTypes)
                .Concat(ShoreCatalog.Build(plugin.Map.TemplateTypes, "wc"))
                .ToDictionary(p => p.Template.Name, System.StringComparer.OrdinalIgnoreCase);
            return (plugin, catalog);
        }

        [Fact]
        public void BakedGraphLoads()
        {
            Assert.True(TransitionGraph.Baked.Count > 10000, "baked graph too small: " + TransitionGraph.Baked.Count);
            Assert.NotEmpty(TransitionGraph.Baked.From("wc02"));
        }

        [Fact]
        public void PlansADeterministicPathBetweenAnchors()
        {
            (IGamePlugin plugin, var catalog) = Fresh();
            List<CoastWalker.Step> first = CoastWalker.PlanPath(plugin.Map, TransitionGraph.Baked, catalog,
                "wc02", new Point(40, 40), new Point(80, 60), 60);
            Assert.NotNull(first);
            Assert.True(first.Count >= 5, "path too short: " + first.Count);
            output.WriteLine(string.Join(" ", first.Select(s => $"{s.Piece.Name}@{s.Origin.X},{s.Origin.Y}")));
            // Ends near the goal, starts at the anchor.
            Assert.Equal(new Point(40, 40), first[0].Origin);
            Point last = first[^1].Origin;
            Assert.True(System.Math.Max(System.Math.Abs(last.X - 80), System.Math.Abs(last.Y - 60)) <= 4,
                "ended far from goal: " + last);

            List<CoastWalker.Step> second = CoastWalker.PlanPath(plugin.Map, TransitionGraph.Baked, catalog,
                "wc02", new Point(40, 40), new Point(80, 60), 60);
            Assert.Equal(first.Select(s => (s.Piece.Name, s.Origin)), second.Select(s => (s.Piece.Name, s.Origin)));
        }

        [Fact]
        public void PlansAClosedLoopOfMinedTransitions()
        {
            (IGamePlugin plugin, var catalog) = Fresh();
            List<CoastWalker.Step> loop = CoastWalker.PlanLoop(plugin.Map, TransitionGraph.Baked, catalog,
                new Point(60, 60), new Size(12, 10));
            Assert.NotNull(loop);
            Assert.True(loop.Count >= 10, "loop too short: " + loop.Count);
            output.WriteLine(string.Join(" ", loop.Select(s => $"{s.Piece.Name}@{s.Origin.X},{s.Origin.Y}")));
            for (int i = 1; i < loop.Count; i++)
            {
                Point offset = new Point(loop[i].Origin.X - loop[i - 1].Origin.X, loop[i].Origin.Y - loop[i - 1].Origin.Y);
                Assert.Contains(TransitionGraph.Baked.From(loop[i - 1].Piece.Name),
                    t => t.To.Equals(loop[i].Piece.Name, System.StringComparison.OrdinalIgnoreCase) && t.Offset == offset);
            }
            // Strict closure: the final piece transitions back onto the first by a mined
            // idiom, not a lucky abutment.
            Point close = new Point(loop[0].Origin.X - loop[^1].Origin.X, loop[0].Origin.Y - loop[^1].Origin.Y);
            Assert.Contains(TransitionGraph.Baked.From(loop[^1].Piece.Name),
                t => t.To.Equals(loop[0].Piece.Name, System.StringComparison.OrdinalIgnoreCase) && t.Offset == close);

            List<CoastWalker.Step> again = CoastWalker.PlanLoop(plugin.Map, TransitionGraph.Baked, catalog,
                new Point(60, 60), new Size(12, 10));
            Assert.Equal(loop.Select(s => (s.Piece.Name, s.Origin)), again.Select(s => (s.Piece.Name, s.Origin)));
        }

        [Fact]
        public void WalkedLakeLoopHoldsItsWater()
        {
            (IGamePlugin plugin, var catalog) = Fresh();
            Map map = plugin.Map;
            var random = new MobiusEditor.Utility.DeterministicRandom(9);
            List<CoastWalker.Step> loop = CoastWalker.PlanLoop(map, TransitionGraph.Baked, catalog,
                new Point(60, 60), new Size(12, 10), random);
            Assert.NotNull(loop);
            CoastWalker.Place(map, loop, catalog, random);
            Assert.True(CoastWalker.FloodWater(map, new Point(60, 60), 40 * 40, random),
                "flood escaped the ring");
            CoastWalker.PatchBareContacts(map, random);
            Assert.Equal(LandType.Water, LakeBuilder.LandAt(map, new Point(60, 60)));
            // The seam invariant: no flood-fill water cell may touch bare clear land.
            int bare = 0;
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    Template fill = map.Templates[y, x];
                    if (fill?.Type == null || (fill.Type.Name != "w1" && fill.Type.Name != "w2")) continue;
                    foreach (Point n in new[] { new Point(x + 1, y), new Point(x - 1, y), new Point(x, y + 1), new Point(x, y - 1) })
                    {
                        if (map.Bounds.Contains(n) && LakeBuilder.LandAt(map, n) == LandType.Clear) bare++;
                    }
                }
            }
            Assert.Equal(0, bare);
        }

        [Fact]
        public void EveryStepIsAMinedTransition()
        {
            (IGamePlugin plugin, var catalog) = Fresh();
            List<CoastWalker.Step> path = CoastWalker.PlanPath(plugin.Map, TransitionGraph.Baked, catalog,
                "wc02", new Point(30, 90), new Point(90, 30), 80);
            Assert.NotNull(path);
            for (int i = 1; i < path.Count; i++)
            {
                Point offset = new Point(path[i].Origin.X - path[i - 1].Origin.X, path[i].Origin.Y - path[i - 1].Origin.Y);
                Assert.Contains(TransitionGraph.Baked.From(path[i - 1].Piece.Name),
                    t => t.To.Equals(path[i].Piece.Name, System.StringComparison.OrdinalIgnoreCase) && t.Offset == offset);
            }
        }
    }
}
