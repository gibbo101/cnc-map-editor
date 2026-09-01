//
// Deterministic skirmish-map synthesis onto a fresh map: spaced player starts, value-noise
// tree cover thinned around the starts and the resource fields, an ore field beside every
// start and contested gem patches between neighbours. Vanilla types only, placed through
// the same guarded operations the brushes use, so every result loads in an unmodded game.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.Headless
{
    public sealed class MapGeneratorOptions
    {
        /// <summary>Same seed and dials on the same theater produce a byte-identical map.</summary>
        public int Seed { get; set; } = 1;
        /// <summary>Start positions to assign; clamped to the game's player-start slots.</summary>
        public int Players { get; set; } = 4;
        /// <summary>Tree cover, 0 (none) to 1 (heavy forest).</summary>
        public double Trees { get; set; } = 0.5;
        /// <summary>Resource richness, 0 (no fields) to 1 (large fields).</summary>
        public double Ore { get; set; } = 0.5;
    }

    public static class MapGenerator
    {
        private const int StartMargin = 4;
        private const int StartClearRadius = 7;
        private const int NoiseStep = 5;

        public static IEnumerable<string> Generate(IGamePlugin plugin, MapGeneratorOptions options)
        {
            List<string> warnings = new List<string>();
            Map map = plugin.Map;
            DeterministicRandom random = new DeterministicRandom(options.Seed);
            List<Point> starts = PlaceStarts(map, options, random);
            List<Point> fieldCenters = PlaceResources(map, options, random, starts, warnings);
            PlaceTrees(plugin, options, random, starts, fieldCenters, warnings);
            plugin.Dirty = true;
            return warnings;
        }

        /// <summary>
        /// Fills the game's player-start waypoints with best-candidate-sampled positions:
        /// each start goes to the candidate farthest from everything placed before it.
        /// </summary>
        private static List<Point> PlaceStarts(Map map, MapGeneratorOptions options, DeterministicRandom random)
        {
            Waypoint[] slots = map.Waypoints.Where(w => w.Flags.HasFlag(WaypointFlag.PlayerStart)).ToArray();
            int count = Math.Clamp(options.Players, 2, slots.Length);
            Rectangle area = map.Bounds;
            area.Inflate(-StartMargin, -StartMargin);
            List<Point> starts = new List<Point>();
            for (int i = 0; i < count; i++)
            {
                Point best = default;
                double bestScore = -1;
                for (int candidate = 0; candidate < 24; candidate++)
                {
                    Point p = new Point(area.Left + random.Next(area.Width), area.Top + random.Next(area.Height));
                    double score = starts.Count == 0
                        ? 1
                        : starts.Min(s => Math.Pow(s.X - p.X, 2) + Math.Pow(s.Y - p.Y, 2));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = p;
                    }
                }
                starts.Add(best);
                if (map.Metrics.GetCell(best, out int cell)) slots[i].Cell = cell;
            }
            for (int i = count; i < slots.Length; i++) slots[i].Cell = null;
            return starts;
        }

        /// <summary>An ore field beside every start; a contested gem patch between neighbouring starts.</summary>
        private static List<Point> PlaceResources(Map map, MapGeneratorOptions options, DeterministicRandom random,
            List<Point> starts, List<string> warnings)
        {
            List<Point> centers = new List<Point>();
            if (options.Ore <= 0) return centers;
            OverlayType ore = map.OverlayTypes.FirstOrDefault(o => o.IsResource && o.Name.StartsWith("gold", StringComparison.OrdinalIgnoreCase))
                ?? map.OverlayTypes.FirstOrDefault(o => o.IsResource);
            OverlayType gems = map.OverlayTypes.FirstOrDefault(o => o.IsResource && o.Name.StartsWith("gem", StringComparison.OrdinalIgnoreCase)) ?? ore;
            if (ore == null)
            {
                warnings.Add("No vanilla resource overlay in this game; no fields generated.");
                return centers;
            }
            int radius = 2 + (int)Math.Round(2 * options.Ore);
            foreach (Point start in starts)
            {
                double angle = random.Next(360) * Math.PI / 180;
                Point center = Clamp(map.Bounds, new Point(
                    start.X + (int)Math.Round(Math.Cos(angle) * (StartClearRadius - 1)),
                    start.Y + (int)Math.Round(Math.Sin(angle) * (StartClearRadius - 1))));
                PlacePatch(map, ore, center, radius);
                centers.Add(center);
            }
            for (int i = 0; i < starts.Count; i++)
            {
                Point a = starts[i], b = starts[(i + 1) % starts.Count];
                Point mid = Clamp(map.Bounds, new Point(
                    (a.X + b.X) / 2 + random.Next(5) - 2,
                    (a.Y + b.Y) / 2 + random.Next(5) - 2));
                PlacePatch(map, gems, mid, Math.Max(2, radius - 1));
                centers.Add(mid);
            }
            return centers;
        }

        private static void PlacePatch(Map map, OverlayType resource, Point center, int radius)
        {
            Dictionary<int, Overlay> undo = new Dictionary<int, Overlay>(), redo = new Dictionary<int, Overlay>();
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    Point p = new Point(center.X + dx, center.Y + dy);
                    if (!map.Bounds.Contains(p) || map.Technos[p] != null) continue;
                    OverlayEdit.Place(map, resource, p, undo, redo);
                }
            }
        }

        /// <summary>
        /// Value-noise tree cover: a random lattice sampled bilinearly gives smooth clumps;
        /// the density dial sets both the clump threshold and the tree budget, which stays
        /// under the game's terrain-object cap. Placement sites are drawn from the clump
        /// cells in shuffled order so a capped budget still spreads over the whole map.
        /// Starts and resource fields keep a clear ring.
        /// </summary>
        private static void PlaceTrees(IGamePlugin plugin, MapGeneratorOptions options, DeterministicRandom random,
            List<Point> starts, List<Point> fieldCenters, List<string> warnings)
        {
            if (options.Trees <= 0) return;
            Map map = plugin.Map;
            List<TerrainType> pool = map.TerrainTypes
                .Where(t => t.ExistsInTheater
                    && t.Name.StartsWith("t", StringComparison.OrdinalIgnoreCase)
                    && !t.Name.Equals("mine", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.ID)
                .ToList();
            if (pool.Count == 0)
            {
                warnings.Add("No tree terrain in this theater; no forest generated.");
                return;
            }
            Rectangle bounds = map.Bounds;
            int latW = bounds.Width / NoiseStep + 2, latH = bounds.Height / NoiseStep + 2;
            double[,] lattice = new double[latH, latW];
            for (int y = 0; y < latH; y++)
                for (int x = 0; x < latW; x++)
                    lattice[y, x] = random.Next(1000) / 1000.0;
            double threshold = 1.0 - 0.45 * options.Trees;
            List<Point> candidates = new List<Point>();
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    Point p = new Point(x, y);
                    if (Noise(lattice, bounds, p) < threshold) continue;
                    if (starts.Any(s => Math.Abs(s.X - x) <= StartClearRadius && Math.Abs(s.Y - y) <= StartClearRadius)) continue;
                    if (fieldCenters.Any(c => Math.Abs(c.X - x) <= 5 && Math.Abs(c.Y - y) <= 5)) continue;
                    candidates.Add(p);
                }
            }
            // Fill the densest clump cores first, so a capped budget yields solid forests
            // instead of an even speckle over every clump.
            candidates = candidates.OrderByDescending(p => Noise(lattice, bounds, p)).ToList();
            int budget = Math.Max(1, (int)(plugin.GameInfo.MaxTerrain * 0.85 * options.Trees));
            int placed = 0;
            foreach (Point p in candidates)
            {
                if (placed >= budget) break;
                TerrainType type = pool[random.Next(pool.Count)];
                if (map.Technos.Add(p, new Terrain { Type = type })) placed++;
            }
        }

        private static double Noise(double[,] lattice, Rectangle bounds, Point p)
        {
            double fx = (p.X - bounds.Left) / (double)NoiseStep, fy = (p.Y - bounds.Top) / (double)NoiseStep;
            int x0 = (int)fx, y0 = (int)fy;
            double tx = fx - x0, ty = fy - y0;
            double top = lattice[y0, x0] * (1 - tx) + lattice[y0, x0 + 1] * tx;
            double bottom = lattice[y0 + 1, x0] * (1 - tx) + lattice[y0 + 1, x0 + 1] * tx;
            return top * (1 - ty) + bottom * ty;
        }

        private static Point Clamp(Rectangle bounds, Point p) => new Point(
            Math.Clamp(p.X, bounds.Left + 1, bounds.Right - 2),
            Math.Clamp(p.Y, bounds.Top + 1, bounds.Bottom - 2));
    }
}
