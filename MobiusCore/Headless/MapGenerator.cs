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
        /// <summary>Water amount, 0 (none) to 1 (maximal for the chosen style), drawn with real shore templates.</summary>
        public double Water { get; set; } = 0.5;
        /// <summary>The shape the water takes: scattered lakes, one big river, an ocean along an edge, or an island world.</summary>
        public WaterStyle Style { get; set; } = WaterStyle.Lakes;
        /// <summary>Lakes style: how many lakes; unset = derived from the water dial.</summary>
        public int? Lakes { get; set; }
        /// <summary>Islands style: how many islands; unset = players plus a few from the water dial.</summary>
        public int? Islands { get; set; }
        /// <summary>River style: corridor width in 3-cell blocks; unset = derived from the water dial.</summary>
        public int? RiverWidth { get; set; }
        /// <summary>River style: how many ford crossings; unset = more fords the lower the water dial.</summary>
        public int? Fords { get; set; }
        /// <summary>Ocean style: band depth in 3-cell blocks; unset = derived from the water dial.</summary>
        public int? OceanDepth { get; set; }
        /// <summary>Ocean style: which map edge holds the sea (0 north, 1 south, 2 east, 3 west); unset = seeded choice.</summary>
        public int? OceanEdge { get; set; }
        /// <summary>Islands style: join neighbouring islands with land causeways; unset = yes.</summary>
        public bool? Causeways { get; set; }
        /// <summary>Neutral civilian villages to scatter; unset = a few on land styles, none on islands.</summary>
        public int? Villages { get; set; }
        /// <summary>Dirt roads linking the villages; unset = yes when there are two or more villages.</summary>
        public bool? Roads { get; set; }
        /// <summary>
        /// Share of resource fields that are tiberium instead of ore, 0..1. Needs a mod whose
        /// manifest declares a tiberium resource flavor (Tiberian Factions does); without one
        /// the dial warns and does nothing.
        /// </summary>
        public double Tiberium { get; set; }
        /// <summary>Coastline character for walked coasts: rocky cliffs, sandy beaches, or a seeded mix per water feature.</summary>
        public CoastFlavor Coast { get; set; } = CoastFlavor.Mixed;
    }

    public enum CoastFlavor { Mixed, Beach, Cliff }

    public enum WaterStyle { None, Lakes, River, Ocean, Islands }

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
            PlaceLakes(map, options, random, warnings);
            List<Point> starts = PlaceStarts(map, options, random);
            List<Point> fieldCenters = PlaceResources(plugin, options, random, starts, warnings);
            PlaceVillagesAndRoads(map, options, random, starts);
            PlaceTrees(plugin, options, random, starts, fieldCenters, warnings);
            plugin.Dirty = true;
            return warnings;
        }

        /// <summary>Buildable ground: the template grid says clear (water, beach, rock and rough are all out).</summary>
        private static bool IsClearGround(Map map, Point p) => LakeBuilder.LandAt(map, p) == LandType.Clear;

        /// <summary>The closest buildable cell inside the bounds, scanning outward ring by ring.</summary>
        private static Point NearestClearGround(Map map, Point from)
        {
            int reach = Math.Max(map.Bounds.Width, map.Bounds.Height);
            for (int radius = 1; radius < reach; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                        Point p = new Point(from.X + dx, from.Y + dy);
                        if (map.Bounds.Contains(p) && IsClearGround(map, p)) return p;
                    }
                }
            }
            return from;
        }

        /// <summary>Water in the chosen style — lakes, one big river, an edge ocean, or an island world.</summary>
        private static void PlaceLakes(Map map, MapGeneratorOptions options, DeterministicRandom random, List<string> warnings)
        {
            if (options.Water <= 0 || options.Style == WaterStyle.None) return;
            IReadOnlyList<ShorePiece> catalog = ShoreCatalog.Build(map.TemplateTypes);
            if (!LakeBuilder.HaveVocabulary(map, catalog))
            {
                warnings.Add("No shore templates in this theater; water skipped.");
                return;
            }
            switch (options.Style)
            {
                case WaterStyle.Ocean:
                    int edge = options.OceanEdge is int chosen && chosen >= 0 && chosen <= 3 ? chosen : random.Next(4);
                    int depth = options.OceanDepth ?? 1 + (int)Math.Round(options.Water * 2);
                    // The corpus-walked coast is the real thing; the straight block
                    // shoreline stays as the fallback for games the corpus does not cover.
                    if (!PlaceWalkedOcean(map, edge, depth, random, catalog, FlavorPrefix(options.Coast, random)))
                    {
                        LakeBuilder.PlaceOcean(map, edge, depth, random, catalog);
                    }
                    break;
                case WaterStyle.River:
                    // A width-1 river is an rv-family stream — the corpus's own river
                    // idiom, banks drawn inside the art. Wider rivers get the walked
                    // sh banks; the straight block corridor stays as the last fallback.
                    int riverWidth = options.RiverWidth ?? 1 + (int)Math.Round(options.Water);
                    if (riverWidth <= 1 && PlaceStreamRiver(map, options, random)) break;
                    if (!PlaceWalkedRiver(map, options, random))
                    {
                        LakeBuilder.PlaceRiver(map, random.Next(2) == 0,
                            riverWidth,
                            options.Fords ?? 1 + (int)Math.Round((1 - options.Water) * 2), random, catalog);
                    }
                    break;
                case WaterStyle.Islands:
                    PlaceIslands(map, options, random, catalog);
                    break;
                default:
                    PlaceScatteredLakes(map, options, random, catalog);
                    break;
            }
            // Every style honours the shore rule: between water and land there is always
            // a drawn cliff or beach — junction water with no transition recedes, and
            // stranded land scraps drown.
            CoastWalker.EnforceShoreRule(map);
            CoastWalker.DrownOrphanIslets(map, random);
        }

        /// <summary>
        /// An ocean with a corpus-walked coastline: anchors jittered along the coast band,
        /// A* segments chained through mined idioms, both ends flush with the map edges,
        /// the sea flooded behind it. Plans everything before placing anything; false when
        /// planning fails and the caller should fall back.
        /// </summary>
        private static bool PlaceWalkedOcean(Map map, int edge, int depthBlocks,
            DeterministicRandom random, IReadOnlyList<ShorePiece> shoreCatalog, string flavor = null)
        {
            TransitionGraph graph = TransitionGraph.Baked;
            Dictionary<string, ShorePiece> catalog = ShoreCatalog.Build(map.TemplateTypes)
                .Concat(ShoreCatalog.Build(map.TemplateTypes, "wc"))
                .GroupBy(p => p.Template.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            int desiredSide = edge switch { 0 => 0, 1 => 4, 2 => 2, _ => 6 };
            string start = CoastWalker.BestStartPiece(map, graph, catalog, desiredSide, flavor)
                ?? CoastWalker.BestStartPiece(map, graph, catalog, desiredSide);
            if (start == null) return false;
            Rectangle b = map.Bounds;
            int depth = Math.Max(2, depthBlocks) * LakeBuilder.Block;
            bool horizontal = edge <= 1;
            int coastLine = edge switch { 0 => b.Top + depth, 1 => b.Bottom - depth - 3, 2 => b.Right - depth - 3, _ => b.Left + depth };
            int runStart = horizontal ? b.Left : b.Top;
            int runEnd = horizontal ? b.Right : b.Bottom;

            Point At(int along, int across) => horizontal ? new Point(along, across) : new Point(across, along);
            Point origin = At(runStart, coastLine);
            string piece = start;
            List<CoastWalker.Step> all = new List<CoastWalker.Step>();
            int anchorCount = 4;
            // Jitter pulls the coast landward only: a landward bulge is a fully shored
            // peninsula, a seaward one strands naked water between coast and map edge.
            int landward = edge switch { 0 => 1, 1 => -1, 2 => -1, _ => 1 };
            for (int i = 1; i <= anchorCount; i++)
            {
                int along = runStart + (runEnd - runStart) * i / anchorCount - (i == anchorCount ? 3 : 0);
                int across = coastLine + landward * random.Next(9);
                List<CoastWalker.Step> segment = CoastWalker.PlanPath(map, graph, catalog,
                    piece, origin, At(along, across), 70, desiredSide, preferPrefix: flavor);
                if (segment == null) return false;
                if (all.Count > 0) segment.RemoveAt(0);
                all.AddRange(segment);
                piece = all[^1].Piece.Name;
                origin = all[^1].Origin;
            }
            CoastWalker.ExtendToEdge(map, graph, catalog, all, horizontal ? 2 : 1, desiredSide);
            CoastWalker.Place(map, all, catalog, random);
            // Seeds at the sea edge's midpoint and both sea corners — a headland near an
            // end can pinch a corner pocket off from a single mid-edge seed.
            Point[] seeds = edge switch
            {
                0 => new[] { new Point(b.Left + b.Width / 2, b.Top + 1), new Point(b.Left + 1, b.Top + 1), new Point(b.Right - 2, b.Top + 1) },
                1 => new[] { new Point(b.Left + b.Width / 2, b.Bottom - 2), new Point(b.Left + 1, b.Bottom - 2), new Point(b.Right - 2, b.Bottom - 2) },
                2 => new[] { new Point(b.Right - 2, b.Top + b.Height / 2), new Point(b.Right - 2, b.Top + 1), new Point(b.Right - 2, b.Bottom - 2) },
                _ => new[] { new Point(b.Left + 1, b.Top + b.Height / 2), new Point(b.Left + 1, b.Top + 1), new Point(b.Left + 1, b.Bottom - 2) },
            };
            int maxCells = (horizontal ? b.Width : b.Height) * (depth + 12);
            // A burst flood leaves partial sea rather than double-placing a fallback coast
            // over this one; the sketch-proven flush ends make it rare.
            foreach (Point seed in seeds)
            {
                CoastWalker.FloodWater(map, seed, maxCells, random);
            }
            CoastWalker.ExtendWaterIntoBorder(map, random);
            return true;
        }

        /// <summary>
        /// A river with corpus-walked meandering banks, ford-first: ford sites are pinned
        /// as block assemblies (a straight bank piece each side of stamped ford blocks),
        /// and each bank walks mined idioms between them, required to enter every
        /// assembly's bank piece at its exact origin via the walker's exact-goal mode —
        /// so the crossing always lines up. Water floods each inter-ford basin. Any
        /// failed segment plans nothing; a flood that escapes reverts every stamped cell.
        /// False falls back to the straight block corridor.
        /// </summary>
        /// <summary>The piece-name family for a coast: cliff and beach as asked, mixed rolls per feature.</summary>
        private static string FlavorPrefix(CoastFlavor coast, DeterministicRandom random) => coast switch
        {
            CoastFlavor.Cliff => "wc",
            CoastFlavor.Beach => "sh",
            _ => random.Next(2) == 0 ? "wc" : "sh",
        };

        /// <summary>The side-classified sh + wc pieces the walker orients by.</summary>
        private static Dictionary<string, ShorePiece> WalkerCatalog(Map map) => ShoreCatalog.Build(map.TemplateTypes)
            .Concat(ShoreCatalog.Build(map.TemplateTypes, "wc"))
            .GroupBy(p => p.Template.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Straight 3x3 pieces with the given water side, most idiom-rich first — pinned
        /// pieces walks must enter exactly, so the graph must know ways into and out.
        /// </summary>
        private static IEnumerable<string> StraightPieces(TransitionGraph graph, Dictionary<string, ShorePiece> catalog, int side)
        {
            return catalog.Values
                .Where(p => p.Kind == ShoreKind.Straight && p.Template.ExistsInTheater
                    && p.Template.IconWidth == LakeBuilder.Block && p.Template.IconHeight == LakeBuilder.Block
                    && SideIndexOf(p.WaterSide) == side)
                .OrderByDescending(p => graph.From(p.Template.Name).Sum(t => t.Count))
                .ThenBy(p => p.Template.Name, StringComparer.Ordinal)
                .Select(p => p.Template.Name);
        }

        private static string BestStraightPiece(TransitionGraph graph, Dictionary<string, ShorePiece> catalog, int side)
            => StraightPieces(graph, catalog, side).FirstOrDefault();

        /// <summary>
        /// A stream: one meandering rv river-course chain from map edge to map edge —
        /// the corpus's own river idiom, with both banks drawn inside the art, so it
        /// needs no flood, no bank walks and no seam patching. Ground forces cross on
        /// mined road-over-stream idioms stamped along it. False falls back to the
        /// wide walked river.
        /// </summary>
        private static bool PlaceStreamRiver(Map map, MapGeneratorOptions options, DeterministicRandom random)
        {
            TransitionGraph graph = TransitionGraph.Baked;
            Dictionary<string, ShorePiece> catalog = WalkerCatalog(map);
            TemplateType Lookup(string name) => map.TemplateTypes.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
            // Streams chain only through-pieces: a course with exactly two exits.
            // Springs and fork blobs dangle arms that abruptly begin or end mid-map.
            bool IsThroughPiece(string name) => name.StartsWith("rv", StringComparison.OrdinalIgnoreCase)
                && Lookup(name) is TemplateType t && CoastWalker.CourseExits(t) == 2;
            string start = graph.Pieces
                .Where(IsThroughPiece)
                .OrderByDescending(p => graph.From(p).Sum(t => t.Count))
                .ThenBy(p => p, StringComparer.Ordinal)
                .FirstOrDefault();
            if (start == null) return false;
            // The straight piece every anchor pins the chain back onto: segments may
            // bend between anchors, but the stream re-straightens at each one instead
            // of sawtoothing through corner idioms.
            string straight = new[] { "rv06", "rv07" }.FirstOrDefault(n => Lookup(n) != null && graph.From(n).Count > 0);
            if (straight == null) return false;
            // The rv set has NS straights (rv06/rv07/rv14) but no EW straight — the 2x2
            // corner pieces carry all EW motion — so streams run north-south; the
            // meander provides the east-west character.
            bool horizontal = false;
            Rectangle b = map.Bounds;
            Point At(int along, int across) => horizontal ? new Point(along, across) : new Point(across, along);
            int runStart = horizontal ? b.Left : b.Top, runEnd = horizontal ? b.Right : b.Bottom;
            int acrossMin = (horizontal ? b.Top : b.Left) + 8, acrossMax = (horizontal ? b.Bottom : b.Right) - 8;
            if (acrossMax - acrossMin < 12) return false;

            // The meander lives in a bounded corridor around one base line: without the
            // clamp the seam-tight diagonal idioms switchback across half the map.
            int baseAcross = acrossMin + random.Next(acrossMax - acrossMin);
            int corridorMin = Math.Max(acrossMin, baseAcross - 12), corridorMax = Math.Min(acrossMax, baseAcross + 12);
            int cursor = baseAcross;
            var chain = new List<CoastWalker.Step>();
            var composite = new Dictionary<Point, LandType>();
            int recorded = 0;
            void RecordUpTo(int end)
            {
                for (; recorded < end; recorded++)
                {
                    CoastWalker.Step st = chain[recorded];
                    LandType[,] grid = ShoreCatalog.LandGrid(st.Piece);
                    for (int y = 0; y < st.Piece.IconHeight; y++)
                        for (int x = 0; x < st.Piece.IconWidth; x++)
                            if (grid[y, x] != LandType.None)
                                composite[new Point(st.Origin.X + x, st.Origin.Y + y)] = grid[y, x];
                }
            }
            string piece = start;
            Point origin = At(runStart, cursor);
            int anchorIndex = 0;
            for (int along = runStart + 8; along < runEnd - 6; along += 9)
            {
                anchorIndex++;
                // Rivers run straight and jog occasionally — a jog at every anchor
                // renders as a mechanical sawtooth.
                if (random.Next(3) == 0)
                {
                    cursor = Math.Clamp(cursor + (random.Next(2) == 0 ? -3 : 3), corridorMin, corridorMax);
                }
                List<Rectangle> forbidden = new List<Rectangle>();
                for (int i = 0; i < chain.Count - 4; i++)
                {
                    forbidden.Add(new Rectangle(chain[i].Origin, new Size(chain[i].Piece.IconWidth, chain[i].Piece.IconHeight)));
                }
                RecordUpTo(Math.Max(0, chain.Count - 4));
                // Every third anchor pins the chain back onto a straight; the two in
                // between are free — organic bends without the sawtooth or the canal.
                bool pin = anchorIndex % 3 == 0;
                List<CoastWalker.Step> segment = CoastWalker.PlanPath(map, graph, catalog,
                    piece, origin, At(along, cursor - (pin ? 1 : 0)), 40, -1, pin ? straight : null,
                    forbidden.Count > 0 ? forbidden : null, "rv",
                    composite.Count > 0 ? composite : null, IsThroughPiece);
                if (segment == null) return false;
                if (chain.Count > 0) segment.RemoveAt(0);
                chain.AddRange(segment);
                if (chain.Count == 0) return false;
                piece = chain[^1].Piece.Name;
                origin = chain[^1].Origin;
            }
            CoastWalker.ExtendToEdge(map, graph, catalog, chain, horizontal ? 2 : 1, -1, null, IsThroughPiece);
            // The river does not stop at the playable bounds: both ends continue through
            // the border frame to the true map edge, so it reads as entering and leaving
            // the world instead of being cut off.
            Rectangle world = new Rectangle(0, 0, map.Metrics.Width, map.Metrics.Height);
            CoastWalker.ExtendToEdge(map, graph, catalog, chain, horizontal ? 2 : 1, -1, world, IsThroughPiece);
            chain.Reverse();
            CoastWalker.ExtendToEdge(map, graph, catalog, chain, horizontal ? 3 : 0, -1, world, IsThroughPiece);
            chain.Reverse();
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
            CoastWalker.Place(map, chain, catalog, random, undo);

            // Crossings come straight from the corpus now: the official maps chain
            // fords onto rv pieces (rv06->ford1 @0,2 and kin) and hang their road
            // approaches off the ford with mined ford->d edges — every offset is an
            // observed idiom, nothing is hand-aligned.
            int crossings = Math.Max(1, options.Fords ?? 1 + (int)Math.Round((1 - options.Water) * 2));
            int spacing = (runEnd - runStart) / (crossings + 1);
            int placedCrossings = 0, lastAlong = runStart - spacing;
            foreach (CoastWalker.Step step in chain)
            {
                if (placedCrossings >= crossings) break;
                int along = horizontal ? step.Origin.X : step.Origin.Y;
                if (along - lastAlong < spacing) continue;
                PieceTransition fordEdge = graph.From(step.Piece.Name).FirstOrDefault(t =>
                    t.To.StartsWith("ford", StringComparison.OrdinalIgnoreCase) && Lookup(t.To) != null);
                if (fordEdge == null) continue;
                TemplateType fordPiece = Lookup(fordEdge.To);
                Point at = new Point(step.Origin.X + fordEdge.Offset.X, step.Origin.Y + fordEdge.Offset.Y);
                if (!map.Bounds.Contains(new Rectangle(at, new Size(fordPiece.IconWidth, fordPiece.IconHeight)))) continue;
                TemplateEdit.Place(map.TemplateTypes, map.Templates, fordPiece, at, null, random, undo, redo);
                // Mined road hangs: one d edge to each side of the stream, then the
                // track continues away from the banks.
                foreach (bool east in new[] { true, false })
                {
                    PieceTransition roadEdge = graph.From(fordPiece.Name).FirstOrDefault(t =>
                        t.To.Length == 3 && t.To[0] == 'd' && char.IsDigit(t.To[1]) && char.IsDigit(t.To[2])
                        && Lookup(t.To) != null && (east ? t.Offset.X > 0 : t.Offset.X < 0));
                    if (roadEdge == null) continue;
                    TemplateType roadPiece = Lookup(roadEdge.To);
                    Point roadAt = new Point(at.X + roadEdge.Offset.X, at.Y + roadEdge.Offset.Y);
                    if (!map.Bounds.Contains(new Rectangle(roadAt, new Size(roadPiece.IconWidth, roadPiece.IconHeight)))) continue;
                    TemplateEdit.Place(map.TemplateTypes, map.Templates, roadPiece, roadAt, null, random, undo, redo);
                    int reach = east ? roadAt.X + roadPiece.IconWidth + 7 : roadAt.X - 7;
                    SettlementBuilder.PlaceRoad(map,
                        new Point(east ? roadAt.X + roadPiece.IconWidth : reach, roadAt.Y),
                        new Point(east ? reach : roadAt.X - 1, roadAt.Y), random);
                }
                placedCrossings++;
                lastAlong = along;
            }
            if (placedCrossings == 0)
            {
                // A stream nobody can cross is a wall, not a river: take it all back
                // so the wide walked river gets a clean map.
                foreach (KeyValuePair<int, Template> cell in undo) map.Templates[cell.Key] = cell.Value;
                return false;
            }
            return true;
        }

        private static bool PlaceWalkedRiver(Map map, MapGeneratorOptions options, DeterministicRandom random)
        {
            TransitionGraph graph = TransitionGraph.Baked;
            Dictionary<string, ShorePiece> catalog = WalkerCatalog(map);
            int block = LakeBuilder.Block;
            bool horizontal = random.Next(2) == 0;
            // The fords are orientation-specific: ford1 carries an east-west road over a
            // north-south stream, ford2 the transpose. The wrong one runs the road along
            // the river instead of across it.
            TemplateType ford = map.TemplateTypes.FirstOrDefault(t =>
                t.Name.Equals(horizontal ? "ford2" : "ford1", StringComparison.OrdinalIgnoreCase)
                && t.ExistsInTheater && t.IconWidth == block && t.IconHeight == block);
            if (ford == null) return false;
            Rectangle b = map.Bounds;
            Point At(int along, int across) => horizontal ? new Point(along, across) : new Point(across, along);
            int runStart = horizontal ? b.Left : b.Top, runEnd = horizontal ? b.Right : b.Bottom;
            int acrossMin = horizontal ? b.Top : b.Left, acrossMax = horizontal ? b.Bottom : b.Right;
            // Two blocks minimum: each bank wanders a few cells around its anchors, and a
            // one-block corridor would pinch shut.
            int width = Math.Clamp(options.RiverWidth ?? 1 + (int)Math.Round(options.Water), 2, 3);
            int widthCells = width * block;
            if (acrossMax - acrossMin < widthCells + 26) return false;
            int fords = Math.Max(1, options.Fords ?? 1 + (int)Math.Round((1 - options.Water) * 2));

            // One shared meander curve keeps the banks parallel: a bounded random walk
            // sampled every dozen cells, with each ford site pinned onto it. Both banks
            // anchor to this curve, so they cannot drift into each other.
            int curveMin = acrossMin + 10, curveMax = acrossMax - widthCells - 10;
            if (curveMax <= curveMin) return false;
            var anchors = new List<(int Along, int Across, bool Ford)>();
            int cursor = curveMin + random.Next(curveMax - curveMin);
            for (int along = runStart + 6; along < runEnd - 6; along += 12)
            {
                cursor = Math.Clamp(cursor + (random.Next(3) - 1) * block, curveMin, curveMax);
                anchors.Add((along, cursor, false));
            }
            if (anchors.Count == 0) return false;
            // Each ford snaps onto its nearest anchor, so no spacing arithmetic can drop
            // one; two fords landing on the same anchor collapse into one crossing.
            for (int i = 0; i < fords; i++)
            {
                int along = runStart + (runEnd - runStart) * (i + 1) / (fords + 1) / block * block;
                int nearest = 0;
                for (int a = 1; a < anchors.Count; a++)
                {
                    if (Math.Abs(anchors[a].Along - along) < Math.Abs(anchors[nearest].Along - along)) nearest = a;
                }
                anchors[nearest] = (along, anchors[nearest].Across, true);
            }
            if (anchors.Count(a => a.Ford) == 0) return false;
            var sites = anchors.Where(a => a.Ford).Select(a => (a.Along, a.Across)).ToList();

            int nearSide = horizontal ? 4 : 2, farSide = horizontal ? 0 : 6;
            string nearName = BestStraightPiece(graph, catalog, nearSide), farName = BestStraightPiece(graph, catalog, farSide);
            if (nearName == null || farName == null) return false;
            if (graph.From(nearName).Count == 0 || graph.From(farName).Count == 0) return false;

            // Walk one bank along the shared curve: vicinity goals at plain anchors keep
            // the meander, exact-goal entries at ford anchors pin the crossings.
            List<CoastWalker.Step> WalkBank(string pieceName, int acrossOffset, int side, int edge)
            {
                var bank = new List<CoastWalker.Step>();
                string piece = pieceName;
                Point origin = At(runStart, anchors[0].Across + acrossOffset);
                foreach ((int along, int across, bool ford) in anchors)
                {
                    List<CoastWalker.Step> segment = CoastWalker.PlanPath(map, graph, catalog,
                        piece, origin, At(along, across + acrossOffset), 40, side, ford ? pieceName : null);
                    if (segment == null) return null;
                    if (bank.Count > 0) segment.RemoveAt(0);
                    bank.AddRange(segment);
                    piece = bank[^1].Piece.Name;
                    origin = bank[^1].Origin;
                }
                List<CoastWalker.Step> tail = CoastWalker.PlanPath(map, graph, catalog,
                    piece, origin, At(runEnd - 3, anchors[^1].Across + acrossOffset), 40, side);
                if (tail == null) return null;
                tail.RemoveAt(0);
                bank.AddRange(tail);
                CoastWalker.ExtendToEdge(map, graph, catalog, bank, edge, side);
                return bank;
            }
            int farEdge = horizontal ? 2 : 1;
            List<CoastWalker.Step> near = WalkBank(nearName, -block, nearSide, farEdge);
            if (near == null) return false;
            List<CoastWalker.Step> far = WalkBank(farName, widthCells, farSide, farEdge);
            if (far == null) return false;

            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
            CoastWalker.Place(map, near, catalog, random, undo);
            CoastWalker.Place(map, far, catalog, random, undo);
            // The strip covers the corridor AND both pinned bank blocks: the straight
            // bank pieces put their water column exactly where the road needs to land,
            // so the crossing must own the banks to reach grass on both sides. (The ford
            // art is really drawn for a one-cell rv stream — the braid across a wide
            // corridor is the standing compromise until rivers grow an rv-stream kind.)
            foreach ((int along, int across) in sites)
            {
                for (int w = -1; w <= width; w++)
                {
                    TemplateEdit.Place(map.TemplateTypes, map.Templates,
                        ford, At(along, across + w * block), null, random, undo, redo);
                }
            }

            // Flood each basin between consecutive crossings (and the two end stretches),
            // seeded midway between the banks' actual walked positions there.
            var boundaries = new List<int> { runStart };
            boundaries.AddRange(sites.Select(s => s.Along));
            boundaries.Add(runEnd);
            int AlongOf(Point p) => horizontal ? p.X : p.Y;
            int AcrossOf(Point p) => horizontal ? p.Y : p.X;
            int AcrossExtent(TemplateType t) => horizontal ? t.IconHeight : t.IconWidth;
            int maxCells = (runEnd - runStart) * (widthCells + 16);
            // Bank bulges split a basin into sub-pockets, so one seed per basin is not
            // enough: seed every few cells along it, first empty cell between the banks.
            // Re-flooding an already-wet pocket is a cheap no-op.
            bool FloodRegion(int from, int to)
            {
                for (int along = from + 2; along < to - 1; along += 4)
                {
                    CoastWalker.Step nearStep = near.OrderBy(s => Math.Abs(AlongOf(s.Origin) - along)).First();
                    CoastWalker.Step farStep = far.OrderBy(s => Math.Abs(AlongOf(s.Origin) - along)).First();
                    int top = AcrossOf(nearStep.Origin) + AcrossExtent(nearStep.Piece);
                    int bottom = AcrossOf(farStep.Origin);
                    for (int across = top; across < bottom; across++)
                    {
                        Point seed = At(along, across);
                        if (!map.Bounds.Contains(seed) || map.Templates[seed.Y, seed.X]?.Type != null) continue;
                        if (!CoastWalker.FloodWater(map, seed, maxCells, random, undo)) return false;
                        break;
                    }
                }
                return true;
            }
            for (int i = 0; i + 1 < boundaries.Count; i++)
            {
                if (!FloodRegion(boundaries[i], boundaries[i + 1]))
                {
                    foreach (KeyValuePair<int, Template> cell in undo) map.Templates[cell.Key] = cell.Value;
                    return false;
                }
            }
            CoastWalker.ExtendWaterIntoBorder(map, random);
            return true;
        }

        private static int SideIndexOf(ShoreSide side) => side switch
        {
            ShoreSide.North => 0,
            ShoreSide.East => 2,
            ShoreSide.South => 4,
            ShoreSide.West => 6,
            _ => -1,
        };

        /// <summary>Lakes scaled by the water dial, spaced apart inside the playable bounds.</summary>
        private static void PlaceScatteredLakes(Map map, MapGeneratorOptions options, DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
        {
            List<Rectangle> placed = new List<Rectangle>();
            int lakeCount = options.Lakes ?? 1 + (int)Math.Round(options.Water * 2);
            // The first lake is the centerpiece: a corpus-walked closed shoreline at a
            // size the idioms deserve. The rest scatter as block-built ponds.
            if (lakeCount > 0) TryPlaceWalkedLake(map, options, random, placed);
            int maxBlocks = 2 + (int)Math.Round(options.Water * 4);
            for (int attempt = 0; attempt < 200 && placed.Count < lakeCount; attempt++)
            {
                int blocksWide = 2 + random.Next(maxBlocks - 1);
                int blocksHigh = 2 + random.Next(maxBlocks - 1);
                int spanX = map.Bounds.Width - blocksWide * LakeBuilder.Block - 2;
                int spanY = map.Bounds.Height - blocksHigh * LakeBuilder.Block - 2;
                if (spanX < 1 || spanY < 1) continue;
                Point origin = new Point(map.Bounds.Left + 1 + random.Next(spanX), map.Bounds.Top + 1 + random.Next(spanY));
                Rectangle candidate = new Rectangle(origin, new Size(blocksWide * LakeBuilder.Block, blocksHigh * LakeBuilder.Block));
                Rectangle spaced = candidate;
                spaced.Inflate(4, 4);
                if (placed.Any(r => r.IntersectsWith(spaced))) continue;
                Rectangle? area = LakeBuilder.Place(map, origin, blocksWide, blocksHigh, random, catalog);
                if (area.HasValue) placed.Add(area.Value);
            }
        }

        /// <summary>
        /// Tries a few candidate centers for the walked centerpiece lake and records the
        /// area it claims (with margin for jitter and piece overhang) so the block ponds
        /// keep their distance. False when no candidate plans and holds its water.
        /// </summary>
        private static bool TryPlaceWalkedLake(Map map, MapGeneratorOptions options, DeterministicRandom random, List<Rectangle> placed)
        {
            Size radius = new Size(8 + (int)Math.Round(options.Water * 5), 7 + (int)Math.Round(options.Water * 4));
            int margin = Math.Max(radius.Width, radius.Height) + 7;
            Rectangle b = map.Bounds;
            if (b.Width <= margin * 2 || b.Height <= margin * 2) return false;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Point center = new Point(
                    b.Left + margin + random.Next(b.Width - margin * 2),
                    b.Top + margin + random.Next(b.Height - margin * 2));
                if (TryWalkedLake(map, center, radius, random, FlavorPrefix(options.Coast, random)))
                {
                    placed.Add(new Rectangle(center.X - radius.Width - 6, center.Y - radius.Height - 6,
                        radius.Width * 2 + 12, radius.Height * 2 + 12));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A lake with a corpus-walked closed shoreline: ring planned around the center,
        /// placed, the basin flooded from the middle. A failed plan or a flood that
        /// escapes the ring reverts every stamped cell and reports false, so the caller
        /// falls back to the block-built lake with the map unmarked.
        /// </summary>
        private static bool TryWalkedLake(Map map, Point center, Size radius, DeterministicRandom random, string flavor = null)
        {
            TransitionGraph graph = TransitionGraph.Baked;
            Dictionary<string, ShorePiece> catalog = WalkerCatalog(map);
            List<CoastWalker.Step> loop = CoastWalker.PlanLoop(map, graph, catalog, center, radius, random,
                flavorPrefix: flavor);
            if (loop == null) return false;
            Dictionary<int, Template> undo = new Dictionary<int, Template>();
            CoastWalker.Place(map, loop, catalog, random, undo);
            // The bound comfortably exceeds the basin's area; a flood that leaks past the
            // ring hits it long before filling the map.
            int maxCells = (radius.Width * 2 + 8) * (radius.Height * 2 + 8);
            if (!CoastWalker.FloodWater(map, center, maxCells, random))
            {
                foreach (KeyValuePair<int, Template> cell in undo) map.Templates[cell.Key] = cell.Value;
                return false;
            }
            return true;
        }

        /// <summary>
        /// An island world: the bounds flooded with water, then same-sized islands carved in
        /// a ring — a top band and a bottom band — and, when enabled, neighbouring islands
        /// joined by straight land causeways so ground forces can advance without transports.
        /// Starts land on the islands by the buildable-ground scoring.
        /// </summary>
        private static void PlaceIslands(Map map, MapGeneratorOptions options, DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
        {
            // Corpus-walked island coastlines are the real thing; the carved block grid
            // stays as the fallback.
            if (PlaceWalkedIslandWorld(map, options, random)) return;
            PlaceBlockIslandWorld(map, options, random, catalog);
        }

        /// <summary>
        /// An island world drawn as ONE landmass: the archipelago (islands plus the
        /// serpentine causeway rectangles) is rasterized into a mask, its boundary is
        /// traced clockwise, and a single continuous coastline is walked around the
        /// whole outline — so a causeway junction is just more coast turning through
        /// mined corner idioms, and no doorway surgery exists to break the shore rule.
        /// The sea floods from the map frame afterwards; a wet island interior reverts
        /// everything so the caller falls back to the block grid.
        /// </summary>
        private static bool PlaceWalkedIslandWorld(Map map, MapGeneratorOptions options, DeterministicRandom random)
        {
            TransitionGraph graph = TransitionGraph.Baked;
            Dictionary<string, ShorePiece> catalog = WalkerCatalog(map);
            int block = LakeBuilder.Block;
            int players = Math.Clamp(options.Players, 2, 8);
            int islands = Math.Clamp(options.Islands ?? players + 1, 2, 12);
            int rows = Math.Max(1, (int)Math.Round(Math.Sqrt(islands)));
            int cols = (islands + rows - 1) / rows;
            // A tight archipelago in the middle of open sea: big islands, short straits.
            // An island must host a full base (~30 cells across); causeways are four
            // blocks wide so the two coasts walking their flanks keep a clear middle
            // and stay out of each other's self-separation moats.
            // Wide straits keep the lobes reading as islands once the walked coast has
            // fattened each shoreline by a piece's land band.
            int strait = 5 * block;
            int causeway = 4 * block;
            int blocksWide = Math.Clamp(12 - (int)Math.Round(options.Water * 4), 8, 11);
            while (blocksWide > 6
                && (cols * blocksWide * block + (cols - 1) * strait > map.Bounds.Width - 8
                    || rows * blocksWide * block + (rows - 1) * strait > map.Bounds.Height - 8))
            {
                blocksWide--;
            }
            int islandW = blocksWide * block, islandH = blocksWide * block;
            int totalW = cols * islandW + (cols - 1) * strait, totalH = rows * islandH + (rows - 1) * strait;
            if (totalW > map.Bounds.Width - 8 || totalH > map.Bounds.Height - 8) return false;
            int offsetX = map.Bounds.Left + (map.Bounds.Width - totalW) / 2 / block * block;
            int offsetY = map.Bounds.Top + (map.Bounds.Height - totalH) / 2 / block * block;
            var areas = new Rectangle[islands];
            for (int i = 0; i < islands; i++)
            {
                int row = i / cols, col = i % cols;
                areas[i] = new Rectangle(
                    offsetX + col * (islandW + strait), offsetY + row * (islandH + strait), islandW, islandH);
            }

            // Rasterize the landmass: islands, then the serpentine chain's causeways.
            bool[,] mask = new bool[map.Metrics.Height, map.Metrics.Width];
            void Fill(Rectangle r)
            {
                for (int y = r.Top; y < r.Bottom; y++)
                    for (int x = r.Left; x < r.Right; x++)
                        if (map.Bounds.Contains(new Point(x, y))) mask[y, x] = true;
            }
            foreach (Rectangle area in areas) Fill(area);
            var order = new List<int>();
            for (int row = 0; row < rows; row++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int col = row % 2 == 0 ? c : cols - 1 - c;
                    int idx = row * cols + col;
                    if (idx < islands) order.Add(idx);
                }
            }
            bool causeways = options.Causeways ?? true;
            for (int i = 0; causeways && i + 1 < order.Count; i++)
            {
                Rectangle a = areas[order[i]], b = areas[order[i + 1]];
                int cy = Math.Max(a.Top, b.Top) + islandH / 2 - causeway / 2;
                if (a.Y == b.Y)
                {
                    Rectangle west = a.X < b.X ? a : b, east = a.X < b.X ? b : a;
                    Fill(new Rectangle(west.Right, a.Top + islandH / 2 - causeway / 2, east.Left - west.Right, causeway));
                }
                else if (a.X == b.X)
                {
                    Fill(new Rectangle(a.Left + islandW / 2 - causeway / 2, a.Bottom, causeway, b.Top - a.Bottom));
                }
                else
                {
                    // Row transition onto a different column: an L through open sea.
                    int elbowX = b.Left + islandW / 2;
                    int legY = a.Top + islandH / 2 - causeway / 2;
                    Rectangle west = elbowX < a.Left ? new Rectangle(elbowX - causeway / 2, legY, a.Left - elbowX + causeway / 2, causeway)
                        : new Rectangle(a.Right, legY, elbowX - a.Right + causeway / 2, causeway);
                    Fill(west);
                    Fill(new Rectangle(elbowX - causeway / 2, legY, causeway, b.Top - legY));
                }
            }

            // Each connected landmass (one with causeways, several without) gets its
            // own traced outline and walked ring; later rings keep clear of earlier ones.
            var components = LandmassComponents(map, mask);
            if (components.Count == 0) return false;
            Dictionary<int, Template> undo = new Dictionary<int, Template>();
            var claimed = new List<Rectangle>();
            Point[] outward =
            {
                new Point(0, -1), new Point(1, -1), new Point(1, 0), new Point(1, 1),
                new Point(0, 1), new Point(-1, 1), new Point(-1, 0), new Point(-1, -1),
            };
            foreach (bool[,] component in components)
            {
                List<(Point Anchor, int Side)> ring = TraceOutlineRing(map, component);
                if (ring == null) return false;
                // Seaward-only anchor jitter: headlands and coves, and no two islands
                // walk the same clone outline.
                for (int i = 0; i < ring.Count; i++)
                {
                    int j = random.Next(2);
                    ring[i] = (new Point(
                        ring[i].Anchor.X + outward[ring[i].Side].X * j,
                        ring[i].Anchor.Y + outward[ring[i].Side].Y * j), ring[i].Side);
                }
                // A cliff coast still carries sandy landing beaches for amphibious
                // assaults: every fourth segment of a wc ring prefers the sh family,
                // and the mined wc-sh splices draw the changeovers.
                string flavor = FlavorPrefix(options.Coast, random);
                List<string> segmentFlavors = null;
                if (flavor == "wc")
                {
                    segmentFlavors = new List<string>();
                    for (int i = 0; i < ring.Count; i++) segmentFlavors.Add(i % 4 == 3 ? "sh" : "wc");
                }
                List<CoastWalker.Step> coast =
                    CoastWalker.PlanRing(map, graph, catalog, ring,
                        avoid: claimed.Count > 0 ? claimed : null,
                        flavorPrefix: flavor, segmentFlavors: segmentFlavors)
                    ?? CoastWalker.PlanRing(map, graph, catalog, ring,
                        avoid: claimed.Count > 0 ? claimed : null);
                if (coast == null) return false;
                CoastWalker.Place(map, coast, catalog, random, undo);
                claimed.AddRange(coast.Select(st => new Rectangle(st.Origin, new Size(st.Piece.IconWidth, st.Piece.IconHeight))));
            }
            Rectangle bounds = map.Bounds;
            Point[] seeds =
            {
                new Point(bounds.Left + 1, bounds.Top + 1), new Point(bounds.Right - 2, bounds.Top + 1),
                new Point(bounds.Left + 1, bounds.Bottom - 2), new Point(bounds.Right - 2, bounds.Bottom - 2),
                new Point(bounds.Left + bounds.Width / 2, bounds.Top + 1), new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - 2),
                new Point(bounds.Left + 1, bounds.Top + bounds.Height / 2), new Point(bounds.Right - 2, bounds.Top + bounds.Height / 2),
            };
            bool Revert()
            {
                foreach (KeyValuePair<int, Template> cell in undo) map.Templates[cell.Key] = cell.Value;
                return false;
            }
            foreach (Point seed in seeds)
            {
                if (!CoastWalker.FloodWater(map, seed, bounds.Width * bounds.Height, random, undo)) return Revert();
            }
            // A wet interior means the outline leaked and an island drowned.
            foreach (Rectangle area in areas)
            {
                LandType land = LakeBuilder.LandAt(map, new Point(area.X + islandW / 2, area.Y + islandH / 2));
                if (land == LandType.Water || land == LandType.River) return Revert();
            }
            CoastWalker.ExtendWaterIntoBorder(map, random);
            return true;
        }

        /// <summary>The 4-connected components of a landmass mask, each as its own mask.</summary>
        private static List<bool[,]> LandmassComponents(Map map, bool[,] mask)
        {
            var components = new List<bool[,]>();
            bool[,] seen = new bool[map.Metrics.Height, map.Metrics.Width];
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    if (!mask[y, x] || seen[y, x]) continue;
                    bool[,] component = new bool[map.Metrics.Height, map.Metrics.Width];
                    var frontier = new Queue<Point>();
                    frontier.Enqueue(new Point(x, y));
                    seen[y, x] = true;
                    while (frontier.Count > 0)
                    {
                        Point p = frontier.Dequeue();
                        component[p.Y, p.X] = true;
                        foreach (Point n in new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) })
                        {
                            if (!map.Bounds.Contains(n) || !mask[n.Y, n.X] || seen[n.Y, n.X]) continue;
                            seen[n.Y, n.X] = true;
                            frontier.Enqueue(n);
                        }
                    }
                    components.Add(component);
                }
            }
            return components;
        }

        /// <summary>
        /// Traces the boundary of a landmass mask clockwise and samples it into a ring
        /// of anchors, each with the coast's outward water side there (the normal away
        /// from land, snapped to a compass step). Null when the mask has no coherent
        /// single boundary.
        /// </summary>
        private static List<(Point Anchor, int Side)> TraceOutlineRing(Map map, bool[,] mask)
        {
            bool Land(int x, int y) => map.Bounds.Contains(new Point(x, y)) && mask[y, x];
            // Start at the topmost-leftmost land cell; walk the boundary with the sea on
            // the left (clockwise around the mass in screen coordinates).
            Point start = default;
            bool found = false;
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom && !found; y++)
                for (int x = map.Bounds.Left; x < map.Bounds.Right && !found; x++)
                    if (Land(x, y)) { start = new Point(x, y); found = true; }
            if (!found) return null;
            // Moore-neighbour tracing, 8-connected, clockwise neighbour order.
            Point[] dirs =
            {
                new Point(1, 0), new Point(1, 1), new Point(0, 1), new Point(-1, 1),
                new Point(-1, 0), new Point(-1, -1), new Point(0, -1), new Point(1, -1),
            };
            var boundary = new List<Point>();
            Point current = start;
            int backtrack = 6; // came from the north (nothing above the topmost cell)
            for (int guard = 0; guard < map.Metrics.Width * map.Metrics.Height; guard++)
            {
                boundary.Add(current);
                bool stepped = false;
                for (int i = 0; i < 8; i++)
                {
                    int d = (backtrack + 1 + i) % 8;
                    Point n = new Point(current.X + dirs[d].X, current.Y + dirs[d].Y);
                    if (!Land(n.X, n.Y)) continue;
                    backtrack = (d + 4) % 8;
                    current = n;
                    stepped = true;
                    break;
                }
                if (!stepped) return null; // an isolated cell is no coastline
                if (current == start && boundary.Count > 2) break;
            }
            if (boundary.Count < 24) return null;
            // Outward normal per boundary cell: the mean direction toward its sea
            // neighbours over a small window, snapped to a compass step.
            int SideOfCell(int index)
            {
                double nx = 0, ny = 0;
                for (int w = -2; w <= 2; w++)
                {
                    Point p = boundary[(index + w + boundary.Count) % boundary.Count];
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (!Land(p.X + dx, p.Y + dy)) { nx += dx; ny += dy; }
                        }
                }
                double angle = Math.Atan2(ny, nx); // y down; 0 = east
                int step = (int)Math.Round(angle / (Math.PI / 4));
                // atan2 east=0 → compass: east is side 2, and steps go clockwise.
                return ((step + 8) % 8 + 2) % 8;
            }
            // Tight sampling: the causeway notches are only a dozen cells long, and a
            // sparse ring lets the walk cut their corners and fill the straits.
            var ring = new List<(Point Anchor, int Side)>();
            int spacing = 9;
            for (int i = 0; i < boundary.Count; i += spacing)
            {
                ring.Add((boundary[i], SideOfCell(i)));
            }
            return ring.Count >= 3 ? ring : null;
        }

        private static void PlaceBlockIslandWorld(Map map, MapGeneratorOptions options, DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
        {
            if (!LakeBuilder.FloodBounds(map, random, catalog)) return;
            int players = Math.Clamp(options.Players, 2, 8);
            int islands = Math.Clamp(options.Islands ?? players + 1, 2, 12);
            int block = LakeBuilder.Block;
            // A near-square grid of same-sized islands: neighbours align by construction,
            // so every causeway is a short straight bridge instead of a ring wall.
            int rows = Math.Max(1, (int)Math.Round(Math.Sqrt(islands)));
            int cols = (islands + rows - 1) / rows;
            int blocksWide = Math.Clamp(7 - (int)Math.Round(options.Water * 3), 4, 6);
            int blocksHigh = blocksWide;
            int islandW = blocksWide * block, islandH = blocksHigh * block;
            int spanX = map.Bounds.Width - islandW, spanY = map.Bounds.Height - islandH;
            if (spanX < 4 || spanY < 4) return;
            Rectangle?[,] grid = new Rectangle?[rows, cols];
            for (int i = 0; i < islands; i++)
            {
                int row = i / cols, col = i % cols;
                double tx = cols == 1 ? 0.5 : 0.12 + col / (double)(cols - 1) * 0.76;
                double ty = rows == 1 ? 0.5 : 0.2 + row / (double)(rows - 1) * 0.6;
                int x = map.Bounds.Left + 2 + (int)(tx * (spanX - 4)) / block * block;
                int y = map.Bounds.Top + 2 + (int)(ty * (spanY - 4)) / block * block;
                Rectangle area = new Rectangle(x, y, islandW, islandH);
                if (LakeBuilder.Place(map, area.Location, blocksWide, blocksHigh, random, catalog, island: true).HasValue)
                {
                    grid[row, col] = area;
                }
            }
            if (!(options.Causeways ?? true)) return;
            // A serpentine chain, not a full mesh: every island is reachable on foot but
            // most of the sea between islands stays open — that is what keeps it reading
            // as an island map instead of a lattice of canals.
            Rectangle? previous = null;
            for (int row = 0; row < rows; row++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int col = row % 2 == 0 ? c : cols - 1 - c;
                    if (!(grid[row, col] is Rectangle current)) continue;
                    if (previous is Rectangle a)
                    {
                        if (a.Y == current.Y && Math.Max(current.Left - a.Right, a.Left - current.Right) >= block)
                        {
                            Rectangle west = a.X < current.X ? a : current, east = a.X < current.X ? current : a;
                            int y = CenterBlock(a).Y;
                            LakeBuilder.PunchBlock(map, new Point(west.Right - block, y));
                            LakeBuilder.PunchBlock(map, new Point(east.Left, y));
                            LakeBuilder.PlaceCauseway(map, new Point(west.Right, y), new Point(east.Left - block, y), random, catalog);
                        }
                        else if (a.X == current.X && current.Top - a.Bottom >= block)
                        {
                            int x = CenterBlock(a).X;
                            LakeBuilder.PunchBlock(map, new Point(x, a.Bottom - block));
                            LakeBuilder.PunchBlock(map, new Point(x, current.Top));
                            LakeBuilder.PlaceCauseway(map, new Point(x, a.Bottom), new Point(x, current.Top - block), random, catalog);
                        }
                        else
                        {
                            // Row transition onto a different column: an L through open sea —
                            // a horizontal leg from the upper island to the lower one's
                            // column, then a vertical leg down to its top edge.
                            int elbowX = CenterBlock(current).X;
                            int y = CenterBlock(a).Y;
                            bool eastward = elbowX > a.Right;
                            Point exitA = eastward ? new Point(a.Right, y) : new Point(a.Left - block, y);
                            LakeBuilder.PunchBlock(map, eastward ? new Point(a.Right - block, y) : new Point(a.Left, y));
                            LakeBuilder.PlaceCauseway(map, exitA, new Point(elbowX, y), random, catalog);
                            LakeBuilder.PlaceCauseway(map, new Point(elbowX, y + block), new Point(elbowX, current.Top - block), random, catalog);
                            LakeBuilder.PunchBlock(map, new Point(elbowX, current.Top));
                        }
                    }
                    previous = current;
                }
            }

            Point CenterBlock(Rectangle area) => new Point(
                area.X + (blocksWide / 2) * block,
                area.Y + (blocksHigh / 2) * block);
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
                double bestScore = double.NegativeInfinity;
                for (int candidate = 0; candidate < 40; candidate++)
                {
                    Point p = new Point(area.Left + random.Next(area.Width), area.Top + random.Next(area.Height));
                    // A base needs buildable ground; a candidate on water/beach only counts
                    // when nothing better turned up at all.
                    double score = starts.Count == 0
                        ? 1
                        : starts.Min(s => Math.Pow(s.X - p.X, 2) + Math.Pow(s.Y - p.Y, 2));
                    if (!IsClearGround(map, p)) score -= 1e9;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = p;
                    }
                }
                // On water-heavy maps every sample can miss land; walk to the nearest
                // buildable cell rather than start a player in the sea.
                if (!IsClearGround(map, best)) best = NearestClearGround(map, best);
                starts.Add(best);
                if (map.Metrics.GetCell(best, out int cell)) slots[i].Cell = cell;
            }
            for (int i = count; i < slots.Length; i++) slots[i].Cell = null;
            return starts;
        }

        /// <summary>
        /// Villages of neutral civilian buildings on open ground away from the starts, and
        /// the dirt roads that chain them together.
        /// </summary>
        private static void PlaceVillagesAndRoads(Map map, MapGeneratorOptions options, DeterministicRandom random, List<Point> starts)
        {
            int villages = options.Villages ?? (options.Style == WaterStyle.Islands ? 0 : Math.Clamp(options.Players / 2 + 1, 2, 4));
            if (villages <= 0) return;
            List<Point> centers = new List<Point>();
            Rectangle area = map.Bounds;
            area.Inflate(-StartMargin, -StartMargin);
            for (int i = 0; i < villages; i++)
            {
                Point best = default;
                double bestScore = double.NegativeInfinity;
                for (int candidate = 0; candidate < 30; candidate++)
                {
                    Point p = new Point(area.Left + random.Next(area.Width), area.Top + random.Next(area.Height));
                    double score = starts.Concat(centers).Select(s => Math.Pow(s.X - p.X, 2) + Math.Pow(s.Y - p.Y, 2))
                        .DefaultIfEmpty(1).Min();
                    if (!IsClearGround(map, p)) score -= 1e9;
                    if (score > bestScore) { bestScore = score; best = p; }
                }
                if (!IsClearGround(map, best)) continue;
                if (SettlementBuilder.PlaceVillage(map, best, 3 + random.Next(4), random).Count > 0) centers.Add(best);
            }
            if (!(options.Roads ?? centers.Count >= 2)) return;
            for (int i = 0; i + 1 < centers.Count; i++)
            {
                SettlementBuilder.PlaceRoad(map, centers[i], centers[i + 1], random);
            }
        }

        /// <summary>
        /// A resource field beside every start — ore seeded with an ore mine, or, when the
        /// active mod declares a tiberium flavor and the tiberium dial asks for it, tiberium
        /// seeded with the mod's spawner (Tiberian Factions: the blossom tree) — plus a
        /// contested gem patch between neighbouring starts.
        /// </summary>
        private static List<Point> PlaceResources(IGamePlugin plugin, MapGeneratorOptions options, DeterministicRandom random,
            List<Point> starts, List<string> warnings)
        {
            Map map = plugin.Map;
            List<Point> centers = new List<Point>();
            if (options.Ore <= 0) return centers;
            ManifestResource tiberium = ResolveFlavor(plugin, "tiberium");
            OverlayType tiberiumOverlay = tiberium?.Overlays
                .Select(n => map.OverlayTypes.FirstOrDefault(o => o.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && o.IsResource && o.ExistsInTheater))
                .FirstOrDefault(o => o != null);
            if (options.Tiberium > 0 && tiberiumOverlay == null)
            {
                warnings.Add("Tiberium requested but no active mod declares a tiberium resource; using ore.");
            }
            // Resource overlays come in value stages (gold01..gold04, gem01..gem04);
            // a field is graded like a hand-placed one — densest at the heart, thinning
            // to the rim — never a puddle of the cheapest stage.
            List<OverlayType> Stages(string prefix) => map.OverlayTypes
                .Where(o => o.IsResource && o.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && o.ExistsInTheater)
                .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<OverlayType> oreStages = Stages("gold");
            if (oreStages.Count == 0)
            {
                OverlayType any = map.OverlayTypes.FirstOrDefault(o => o.IsResource);
                if (any == null)
                {
                    warnings.Add("No vanilla resource overlay in this game; no fields generated.");
                    return centers;
                }
                oreStages = new List<OverlayType> { any };
            }
            List<OverlayType> gemStages = Stages("gem");
            if (gemStages.Count == 0) gemStages = oreStages;
            List<OverlayType> tiberiumStages = tiberiumOverlay == null ? null : new List<OverlayType> { tiberiumOverlay };
            int radius = 3 + (int)Math.Round(4 * options.Ore);
            foreach (Point start in starts)
            {
                double angle = random.Next(360) * Math.PI / 180;
                Point center = Clamp(map.Bounds, new Point(
                    start.X + (int)Math.Round(Math.Cos(angle) * (StartClearRadius + 1)),
                    start.Y + (int)Math.Round(Math.Sin(angle) * (StartClearRadius + 1))));
                bool tiberiumField = tiberiumOverlay != null && random.Next(1000) < options.Tiberium * 1000;
                PlaceSpawner(map, tiberiumField ? tiberium : null, center, random);
                PlacePatch(map, tiberiumField ? tiberiumStages : oreStages, center, radius);
                centers.Add(center);
            }
            for (int i = 0; i < starts.Count; i++)
            {
                Point a = starts[i], b = starts[(i + 1) % starts.Count];
                Point mid = Clamp(map.Bounds, new Point(
                    (a.X + b.X) / 2 + random.Next(5) - 2,
                    (a.Y + b.Y) / 2 + random.Next(5) - 2));
                PlacePatch(map, gemStages, mid, Math.Max(3, radius - 2));
                centers.Add(mid);
            }
            return centers;
        }

        /// <summary>The mod-declared resource flavor for the plugin's game, or null.</summary>
        private static ManifestResource ResolveFlavor(IGamePlugin plugin, string flavor)
        {
            string key = plugin.GameInfo.GameType == GameType.TiberianDawn ? "TD" : "RA";
            if (!MobiusEditor.Globals.TheModManifests.TryGetValue(key, out IReadOnlyList<ModManifest> manifests)) return null;
            return manifests
                .SelectMany(m => m.Resources)
                .FirstOrDefault(r => r.Flavor.Equals(flavor, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The living seed of a resource field: the ore mine for ore, or the flavor's
        /// declared spawner (building or terrain) for a modded resource.
        /// </summary>
        private static void PlaceSpawner(Map map, ManifestResource flavor, Point center, DeterministicRandom random)
        {
            if (!map.Bounds.Contains(center) || !IsClearGround(map, center)) return;
            if (flavor == null)
            {
                TerrainType mine = map.TerrainTypes.FirstOrDefault(t =>
                    t.Name.Equals("mine", StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
                if (mine != null) map.Technos.Add(center, new Terrain { Type = mine });
                return;
            }
            if (!string.IsNullOrEmpty(flavor.SpawnerBuilding))
            {
                BuildingType building = map.BuildingTypes.FirstOrDefault(t =>
                    t.Name.Equals(flavor.SpawnerBuilding, StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
                HouseType neutral = map.HouseTypes.FirstOrDefault(h => h.Name.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
                    ?? map.HouseTypes.First();
                if (building != null)
                {
                    map.Buildings.Add(center, new Building
                    {
                        Type = building,
                        House = neutral,
                        Strength = 256,
                        IsPrebuilt = true,
                        Direction = map.BuildingDirectionTypes.First(d => d.Facing == FacingType.North),
                    });
                }
            }
            else if (!string.IsNullOrEmpty(flavor.SpawnerTerrain))
            {
                TerrainType terrain = map.TerrainTypes.FirstOrDefault(t =>
                    t.Name.Equals(flavor.SpawnerTerrain, StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
                if (terrain != null) map.Technos.Add(center, new Terrain { Type = terrain });
            }
        }

        private static void PlacePatch(Map map, IReadOnlyList<OverlayType> stages, Point center, int radius)
        {
            if (stages == null || stages.Count == 0) return;
            Dictionary<int, Overlay> undo = new Dictionary<int, Overlay>(), redo = new Dictionary<int, Overlay>();
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    Point p = new Point(center.X + dx, center.Y + dy);
                    if (!map.Bounds.Contains(p) || map.Technos[p] != null || map.Buildings[p] != null || !IsClearGround(map, p)) continue;
                    // Dense heart, thinning rim: the last stage is the richest.
                    double frac = Math.Sqrt(dx * dx + dy * dy) / Math.Max(1, radius);
                    int fromDensest = frac < 0.55 ? 0 : frac < 0.75 ? 1 : frac < 0.9 ? 2 : 3;
                    OverlayType stage = stages[Math.Max(0, stages.Count - 1 - fromDensest)];
                    OverlayEdit.Place(map, stage, p, undo, redo);
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
                    if (!IsClearGround(map, p)) continue;
                    candidates.Add(p);
                }
            }
            // Fill the densest clump cores first, so a capped budget yields solid forests
            // instead of an even speckle over every clump.
            candidates = candidates.OrderByDescending(p => Noise(lattice, bounds, p)).ToList();
            int budget = Math.Max(1, (int)(plugin.GameInfo.MaxTerrain * 0.85 * options.Trees));
            int placed = 0;
            // A tree's whole footprint must stand on clear ground — the origin check
            // alone plants trees whose occupied cells hang into the water.
            bool Fits(TerrainType type, Point p)
            {
                for (int y = 0; y < type.OccupyMask.GetLength(0); y++)
                {
                    for (int x = 0; x < type.OccupyMask.GetLength(1); x++)
                    {
                        if (!type.OccupyMask[y, x]) continue;
                        Point cell = new Point(p.X + x, p.Y + y);
                        if (!bounds.Contains(cell) || !IsClearGround(map, cell)) return false;
                    }
                }
                return true;
            }
            foreach (Point p in candidates)
            {
                if (placed >= budget) break;
                TerrainType type = pool[random.Next(pool.Count)];
                if (!Fits(type, p)) continue;
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
