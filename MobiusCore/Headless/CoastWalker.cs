//
// The transition-mined coast walker: draws coastlines by chaining terrain pieces along
// adjacencies observed in the official map corpus, so every corner and bend it produces
// is an idiom a real mapper used. Pathing is deterministic A* over (piece, origin)
// states — common idioms cost less — with orientation pruning from the shore catalog so
// the water side cannot flip mid-walk.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Model;

namespace MobiusEditor.Headless
{
    /// <summary>The mined adjacency table indexed for walking, reverse edges included.</summary>
    public sealed class TransitionGraph
    {
        private readonly Dictionary<string, List<PieceTransition>> successors =
            new Dictionary<string, List<PieceTransition>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Lazy<TransitionGraph> baked = new Lazy<TransitionGraph>(() =>
        {
            using System.IO.Stream stream = typeof(TransitionGraph).Assembly
                .GetManifestResourceStream("MobiusCore.Resources.mined-transitions.txt");
            if (stream == null) return new TransitionGraph();
            using System.IO.StreamReader reader = new System.IO.StreamReader(stream);
            return Load(reader.ReadToEnd());
        });

        /// <summary>The corpus baked into the editor — mined once from the official maps.</summary>
        public static TransitionGraph Baked => baked.Value;

        public static TransitionGraph Load(string bakedText)
        {
            TransitionGraph graph = new TransitionGraph();
            foreach (PieceTransition t in TransitionMiner.Parse(bakedText))
            {
                graph.Add(t);
                // The corpus records each touching pair once; a walk can traverse the
                // idiom in either order.
                graph.Add(new PieceTransition(t.To, t.From, new Point(-t.Offset.X, -t.Offset.Y)) { Count = t.Count });
            }
            foreach (List<PieceTransition> list in graph.successors.Values)
            {
                // Stable order: common idioms first, then deterministic tie-breaks.
                list.Sort((a, b) =>
                {
                    int byCount = b.Count.CompareTo(a.Count);
                    if (byCount != 0) return byCount;
                    int byName = string.CompareOrdinal(a.To, b.To);
                    if (byName != 0) return byName;
                    return (a.Offset.Y, a.Offset.X).CompareTo((b.Offset.Y, b.Offset.X));
                });
            }
            return graph;
        }

        private void Add(PieceTransition t)
        {
            if (!successors.TryGetValue(t.From, out List<PieceTransition> list))
            {
                successors[t.From] = list = new List<PieceTransition>();
            }
            PieceTransition existing = list.FirstOrDefault(x =>
                x.To.Equals(t.To, StringComparison.OrdinalIgnoreCase) && x.Offset == t.Offset);
            if (existing != null) existing.Count += t.Count;
            else list.Add(t);
        }

        public IReadOnlyList<PieceTransition> From(string piece) =>
            successors.TryGetValue(piece, out List<PieceTransition> list) ? list : Array.Empty<PieceTransition>();

        public IEnumerable<string> Pieces => successors.Keys;
        public int Count => successors.Values.Sum(l => l.Count);
    }

    public static class CoastWalker
    {
        /// <summary>A planned placement: which piece goes where.</summary>
        public readonly struct Step
        {
            public readonly Point Origin;
            public readonly TemplateType Piece;
            public Step(Point origin, TemplateType piece) { Origin = origin; Piece = piece; }
        }

        /// <summary>
        /// Plans a coast segment from a start piece toward a goal cell: A* over the mined
        /// transition graph, cheaper along common idioms, pruned when a successor's water
        /// side turns more than a quarter turn from its predecessor's. Returns the piece
        /// chain including the start, or null when no path reaches the goal's vicinity.
        /// With goalPiece set the search only succeeds on that exact piece at the exact
        /// goal cell — the edge into it is then itself a mined, sealed transition, which
        /// is what lets a loop close strictly.
        /// </summary>
        public static List<Step> PlanPath(Map map, TransitionGraph graph,
            IReadOnlyDictionary<string, ShorePiece> catalog,
            string startPiece, Point startOrigin, Point goal, int maxPieces, int desiredSide = -1,
            string goalPiece = null, IReadOnlyList<Rectangle> forbidden = null, string preferPrefix = null,
            IReadOnlyDictionary<Point, LandType> placed = null, Func<string, bool> pieceFilter = null)
        {
            TemplateType Lookup(string name) => map.TemplateTypes.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
            if (Lookup(startPiece) == null) return null;

            var open = new PriorityQueue<(string Piece, Point Origin), (double F, long Order)>();
            var cameFrom = new Dictionary<(string, Point), (string, Point)>();
            var bestCost = new Dictionary<(string, Point), double> { [(startPiece, startOrigin)] = 0 };
            long order = 0;
            open.Enqueue((startPiece, startOrigin), (Heuristic(startOrigin), order++));
            (string, Point)? found = null;
            int explored = 0;

            while (open.Count > 0 && explored < 30000)
            {
                (string piece, Point origin) = open.Dequeue();
                explored++;
                double cost = bestCost[(piece, origin)];
                bool atGoal = goalPiece == null
                    ? Chebyshev(origin, goal) <= 2 && (piece, origin) != (startPiece, startOrigin)
                    : origin == goal && piece.Equals(goalPiece, StringComparison.OrdinalIgnoreCase);
                if (atGoal)
                {
                    found = (piece, origin);
                    break;
                }
                if (cost >= maxPieces) continue;
                int fromSide = SideIndex(catalog, piece);
                foreach (PieceTransition t in graph.From(piece))
                {
                    TemplateType next = Lookup(t.To);
                    if (next == null) continue;
                    if (pieceFilter != null && !pieceFilter(t.To)) continue;
                    Point nextOrigin = new Point(origin.X + t.Offset.X, origin.Y + t.Offset.Y);
                    Rectangle box = new Rectangle(nextOrigin, new Size(next.IconWidth, next.IconHeight));
                    if (!map.Bounds.Contains(box)) continue;
                    // Coast geometry planned earlier (this walk's own older segments, or
                    // another ring) is off-limits with a one-cell moat: a chain that
                    // merely comes NEAR itself leaves an unsealed water channel between
                    // two stretches of coast — the exact-goal piece is the one exception,
                    // since re-entering it is how a loop closes.
                    if (forbidden != null)
                    {
                        Rectangle moat = box;
                        moat.Inflate(1, 1);
                        bool isGoal = goalPiece != null && nextOrigin == goal
                            && t.To.Equals(goalPiece, StringComparison.OrdinalIgnoreCase);
                        if (!isGoal && forbidden.Any(f => f.IntersectsWith(moat))) continue;
                    }
                    int toSide = SideIndex(catalog, t.To);
                    if (fromSide >= 0 && toSide >= 0 && TurnDistance(fromSide, toSide) > 2) continue;
                    // The whole segment keeps its water on the commanded side (within 45°),
                    // or the coast slowly rotates and the flood side leaks. Unclassified
                    // pieces (river courses, falls) cannot prove their side, and they are
                    // porous — a bank built from them leaks the flood through itself.
                    if (desiredSide >= 0 && (toSide < 0 || TurnDistance(toSide, desiredSide) > 1)) continue;
                    // Consecutive pieces must share an edge, not merely touch corners —
                    // corner-only contact leaves a diagonal gap the water pours through.
                    TemplateType current = Lookup(piece);
                    Rectangle boxA = new Rectangle(origin, new Size(current.IconWidth, current.IconHeight));
                    if (!SealedAdjacency(boxA, box)) continue;
                    if (desiredSide >= 0 && !SeamSealed(current, origin, next, nextOrigin)) continue;
                    // A side-less walk (an rv stream) still needs its watercourse to
                    // continue across the seam — the rest of the seam rule would wrongly
                    // reject rv's self-banked water-against-clear pairs.
                    if (desiredSide < 0 && !WatersTouch(current, origin, next, nextOrigin)) continue;
                    // Placement-exact: the candidate must not create a cross-stamp
                    // water-clear junction against ANYTHING already laid — pair checks
                    // cannot see a third stamp's overwrite, this can.
                    if (placed != null && !CompositeSeals(next, nextOrigin, placed, desiredSide < 0)) continue;
                    double stepCost = 1.0 + 4.0 / (t.Count + 1);
                    // Coast flavor: the off-family pieces stay reachable (the mined
                    // wc-sh splices are how a cliff coast carries a landing beach), but
                    // the walk stays in its family unless it has a reason not to.
                    if (preferPrefix != null && !t.To.StartsWith(preferPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        stepCost += 1.5;
                    }
                    double total = cost + stepCost;
                    var key = (t.To, nextOrigin);
                    if (bestCost.TryGetValue(key, out double known) && known <= total) continue;
                    bestCost[key] = total;
                    cameFrom[key] = (piece, origin);
                    open.Enqueue((t.To, nextOrigin), (total + Heuristic(nextOrigin), order++));
                }
            }
            if (found == null) return null;

            List<Step> path = new List<Step>();
            (string, Point) cursor = found.Value;
            while (true)
            {
                path.Add(new Step(cursor.Item2, Lookup(cursor.Item1)));
                if (!cameFrom.TryGetValue(cursor, out (string, Point) prev)) break;
                cursor = prev;
            }
            path.Reverse();
            return path;

            double Heuristic(Point p) => Chebyshev(p, goal) / 3.0;
        }

        /// <summary>
        /// Plans a closed coast ring around a center: eight anchors on an ellipse, each
        /// segment walked through mined idioms with the water side rotating around the
        /// ring — inward for a lake, outward for an island — and the final segment
        /// required to re-enter the start piece at its exact origin, so the loop closes
        /// on a mined transition, never a lucky abutment. Anchor jitter grows the ring
        /// outward only: landward for a lake (an inward bulge would pinch the flood
        /// basin), seaward for an island (a fully shored headland). A pin replaces the
        /// anchor at its compass position with an exact strip of pieces, ordered along
        /// the walk: the walk must enter the strip's first piece precisely and the strip
        /// is then laid verbatim — which is how a causeway gate gets a known,
        /// block-aligned doorway with straight coast on both sides of it. The caller
        /// builds strips from mined transitions; a single-piece strip is just a pinned
        /// point. Returns the ring without the duplicated closing step, or null when any
        /// segment or the strict closure cannot be planned.
        /// </summary>
        public static List<Step> PlanLoop(Map map, TransitionGraph graph,
            IReadOnlyDictionary<string, ShorePiece> catalog, Point center, Size radius,
            MobiusEditor.Utility.DeterministicRandom random = null, bool waterInside = true,
            IReadOnlyDictionary<int, IReadOnlyList<(string Piece, Point Origin)>> pins = null,
            IReadOnlyCollection<Rectangle> avoid = null, string flavorPrefix = null)
        {
            // Anchor 0 sits at the ring's top; going clockwise the anchor at compass
            // position k wants its water side at (4 + k) mod 8 for a lake (water toward
            // the center), k mod 8 for an island (water away from it).
            var ring = new List<(Point Anchor, int Side)>();
            for (int k = 0; k < 8; k++)
            {
                double angle = (k * 45 - 90) * Math.PI / 180.0;
                int outward = random?.Next(3) ?? 0;
                ring.Add((new Point(
                    center.X + (int)Math.Round(Math.Cos(angle) * (radius.Width + outward)),
                    center.Y + (int)Math.Round(Math.Sin(angle) * (radius.Height + outward))),
                    waterInside ? (4 + k) % 8 : k % 8));
            }
            return PlanRing(map, graph, catalog, ring, pins, avoid, flavorPrefix);
        }

        /// <summary>
        /// Plans a closed coast around any ordered ring of anchors, each commanding its
        /// own water side (the outward normal for a landmass outline, the inward one for
        /// a lake). Same contract as the ellipse loop: mined idioms only, strict closure
        /// back into the start piece, self-separation with joint exemptions, optional
        /// pinned strips by anchor index.
        /// </summary>
        public static List<Step> PlanRing(Map map, TransitionGraph graph,
            IReadOnlyDictionary<string, ShorePiece> catalog,
            IReadOnlyList<(Point Anchor, int Side)> ring,
            IReadOnlyDictionary<int, IReadOnlyList<(string Piece, Point Origin)>> pins = null,
            IReadOnlyCollection<Rectangle> avoid = null, string flavorPrefix = null,
            IReadOnlyList<string> segmentFlavors = null)
        {
            if (ring == null || ring.Count < 3) return null;
            int count = ring.Count;
            string FlavorAt(int k) => segmentFlavors != null ? segmentFlavors[k % count] : flavorPrefix;
            TemplateType Lookup(string name) => map.TemplateTypes.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
            IReadOnlyList<(string Piece, Point Origin)> Strip(int k) =>
                pins != null && pins.TryGetValue(k % count, out IReadOnlyList<(string Piece, Point Origin)> s) && s.Count > 0 ? s : null;
            Point AnchorAt(int k) => Strip(k) is IReadOnlyList<(string Piece, Point Origin)> pinned
                ? pinned[0].Origin : ring[k % count].Anchor;
            int SideAt(int k) => ring[k % count].Side;
            bool AppendStrip(List<Step> steps, IReadOnlyList<(string Piece, Point Origin)> strip, int from)
            {
                for (int i = from; i < strip.Count; i++)
                {
                    TemplateType type = Lookup(strip[i].Piece);
                    if (type == null) return false;
                    steps.Add(new Step(strip[i].Origin, type));
                }
                return true;
            }
            IReadOnlyList<(string Piece, Point Origin)> startStrip = Strip(0);
            string start = startStrip?[0].Piece
                ?? BestStartPiece(map, graph, catalog, SideAt(0), FlavorAt(0))
                ?? BestStartPiece(map, graph, catalog, SideAt(0));
            if (start == null) return null;
            List<Step> all = new List<Step>();
            var composite = new Dictionary<Point, LandType>();
            int recorded = 0;
            void RecordUpTo(int end)
            {
                for (; recorded < end; recorded++)
                {
                    Step s2 = all[recorded];
                    LandType[,] grid = ShoreCatalog.LandGrid(s2.Piece);
                    for (int y = 0; y < s2.Piece.IconHeight; y++)
                        for (int x = 0; x < s2.Piece.IconWidth; x++)
                            if (grid[y, x] != LandType.None)
                                composite[new Point(s2.Origin.X + x, s2.Origin.Y + y)] = grid[y, x];
                }
            }
            if (startStrip != null && !AppendStrip(all, startStrip, 0)) return null;
            string piece = all.Count > 0 ? all[^1].Piece.Name : start;
            Point origin = all.Count > 0 ? all[^1].Origin : AnchorAt(0);
            Rectangle BoxOf(Step s) => new Rectangle(s.Origin, new Size(s.Piece.IconWidth, s.Piece.IconHeight));
            for (int k = 1; k <= count; k++)
            {
                bool closing = k == count;
                IReadOnlyList<(string Piece, Point Origin)> strip = closing ? null : Strip(k);
                // The ring so far is off-limits to this segment, apart from the joints:
                // the last few pieces (the segment grows out of them — corner idioms
                // stride one or two cells, so a narrow window rejects legal turns) and,
                // when closing, the first few (the segment must reach back into them).
                // The placement-exact composite trails by the same window: a joint piece
                // legitimately overwrites its predecessors, and the pair checks already
                // guard those seams.
                List<Rectangle> forbidden = new List<Rectangle>(avoid ?? Array.Empty<Rectangle>());
                for (int i = closing ? 4 : 0; i < all.Count - 4; i++) forbidden.Add(BoxOf(all[i]));
                RecordUpTo(Math.Max(0, all.Count - 4));
                List<Step> segment = PlanPath(map, graph, catalog, piece, origin,
                    closing ? AnchorAt(0) : AnchorAt(k), 40, SideAt(k), closing ? start : strip?[0].Piece,
                    forbidden.Count > 0 ? forbidden : null, FlavorAt(k),
                    composite.Count > 0 ? composite : null);
                if (segment == null) return null;
                if (all.Count > 0) segment.RemoveAt(0);
                if (closing) segment.RemoveAt(segment.Count - 1);
                all.AddRange(segment);
                if (strip != null && !AppendStrip(all, strip, 1)) return null;
                if (all.Count == 0) return null;
                piece = all[^1].Piece.Name;
                origin = all[^1].Origin;
            }
            return all;
        }

        /// <summary>
        /// Stamps a planned path onto the map, in order, then seals each piece's masked
        /// cells on its water side with water tiles — in the corpus maps those transparent
        /// cells sit over open water, and without them the coast line leaks.
        /// </summary>
        public static void Place(Map map, IEnumerable<Step> path,
            IReadOnlyDictionary<string, ShorePiece> catalog, MobiusEditor.Utility.DeterministicRandom random,
            IDictionary<int, Template> undo = null)
        {
            TemplateType water = map.TemplateTypes.FirstOrDefault(t =>
                (t.Flags & TemplateTypeFlag.DefaultFill) == TemplateTypeFlag.DefaultFill && t.ExistsInTheater
                && t.LandTypes != null && t.LandTypes.Length > 0 && t.LandTypes[0] == LandType.Water);
            undo ??= new Dictionary<int, Template>();
            Dictionary<int, Template> redo = new Dictionary<int, Template>();
            List<Step> placed = path.ToList();
            foreach (Step step in placed)
            {
                TemplateEdit.Place(map.TemplateTypes, map.Templates, step.Piece, step.Origin, null, random, undo, redo);
            }
            if (water == null) return;
            // Masked piece cells over open water: fill any still-empty box cell that touches
            // a placed water cell orthogonally, repeating until stable — growth from real
            // water, so land-side masked cells stay grass.
            List<Point> holes = new List<Point>();
            foreach (Step step in placed)
            {
                LandType[,] grid = ShoreCatalog.LandGrid(step.Piece);
                for (int y = 0; y < step.Piece.IconHeight; y++)
                {
                    for (int x = 0; x < step.Piece.IconWidth; x++)
                    {
                        if (grid[y, x] != LandType.None) continue;
                        Point cell = new Point(step.Origin.X + x, step.Origin.Y + y);
                        if (map.Bounds.Contains(cell)) holes.Add(cell);
                    }
                }
            }
            bool IsWaterCell(Point p) => map.Bounds.Contains(p)
                && map.Templates[p.Y, p.X]?.Type is TemplateType t
                && t.GetLandType(map.Templates[p.Y, p.X].Icon) is LandType land
                && (land == LandType.Water || land == LandType.River);
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (Point cell in holes)
                {
                    if (map.Templates[cell.Y, cell.X]?.Type != null) continue;
                    bool touchesWater = IsWaterCell(new Point(cell.X + 1, cell.Y)) || IsWaterCell(new Point(cell.X - 1, cell.Y))
                        || IsWaterCell(new Point(cell.X, cell.Y + 1)) || IsWaterCell(new Point(cell.X, cell.Y - 1));
                    if (!touchesWater) continue;
                    TemplateEdit.Place(map.TemplateTypes, map.Templates, water, cell, null, random, undo, redo);
                    grew = true;
                }
            }
        }

        /// <summary>
        /// Floods still-clear ground with water tiles from a seed, bounded by anything the
        /// coast (or map content) already claims. Aborts and undoes nothing beyond maxCells —
        /// the caller treats a burst flood as a failed coast and falls back.
        /// </summary>
        public static bool FloodWater(Map map, Point seed, int maxCells, MobiusEditor.Utility.DeterministicRandom random,
            IDictionary<int, Template> undo = null)
        {
            TemplateType water = map.TemplateTypes.FirstOrDefault(t =>
                (t.Flags & TemplateTypeFlag.DefaultFill) == TemplateTypeFlag.DefaultFill && t.ExistsInTheater
                && t.LandTypes != null && t.LandTypes.Length > 0 && t.LandTypes[0] == LandType.Water);
            TemplateType patch = map.TemplateTypes.FirstOrDefault(t => t.Name == "w2" && t.ExistsInTheater);
            if (water == null) return false;
            undo ??= new Dictionary<int, Template>();
            Dictionary<int, Template> redo = new Dictionary<int, Template>();
            Queue<Point> frontier = new Queue<Point>();
            HashSet<Point> seen = new HashSet<Point>();
            frontier.Enqueue(seed);
            seen.Add(seed);
            List<Point> flooded = new List<Point>();
            while (frontier.Count > 0)
            {
                Point p = frontier.Dequeue();
                // The map edge bounds the flood like a coast does.
                if (!map.Bounds.Contains(p)) continue;
                if (map.Templates[p.Y, p.X]?.Type != null) continue;
                if (flooded.Count >= maxCells) return false;
                flooded.Add(p);
                foreach (Point n in new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) })
                {
                    if (seen.Add(n)) frontier.Enqueue(n);
                }
            }
            foreach (Point p in flooded)
            {
                bool patchFits = patch != null && p.X + 1 < map.Bounds.Right && p.Y + 1 < map.Bounds.Bottom
                    && random.Next(4) == 0;
                TemplateEdit.Place(map.TemplateTypes, map.Templates, patchFits ? patch : water, p, null, random, undo, redo);
            }
            return true;
        }

        /// <summary>
        /// Greedily continues a path until its end piece lies flush against the east (or
        /// west) bounds edge, so water cannot wrap around the coast's tip. Prefers common
        /// idioms that advance toward the edge without turning the water side.
        /// </summary>
        /// <param name="edge">0 north, 1 south, 2 east, 3 west — the bounds edge to reach.</param>
        /// <param name="within">Placement area; defaults to the playable bounds. Pass the
        /// full metrics rectangle to run a stream off through the border frame.</param>
        public static void ExtendToEdge(Map map, TransitionGraph graph,
            IReadOnlyDictionary<string, ShorePiece> catalog, List<Step> path, int edge, int desiredSide,
            Rectangle? within = null, Func<string, bool> pieceFilter = null)
        {
            Rectangle area = within ?? map.Bounds;
            TemplateType Lookup(string name) => map.TemplateTypes.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
            for (int guard = 0; guard < 40; guard++)
            {
                Step last = path[^1];
                Rectangle lastBox = new Rectangle(last.Origin, new Size(last.Piece.IconWidth, last.Piece.IconHeight));
                bool flush = edge switch
                {
                    0 => lastBox.Top <= area.Top,
                    1 => lastBox.Bottom >= area.Bottom,
                    2 => lastBox.Right >= area.Right,
                    _ => lastBox.Left <= area.Left,
                };
                if (flush) return;
                PieceTransition chosen = null;
                TemplateType chosenType = null;
                foreach (PieceTransition t in graph.From(last.Piece.Name))
                {
                    if (pieceFilter != null && !pieceFilter(t.To)) continue;
                    bool advances = edge switch
                    {
                        0 => t.Offset.Y < 0,
                        1 => t.Offset.Y > 0,
                        2 => t.Offset.X > 0,
                        _ => t.Offset.X < 0,
                    };
                    if (!advances) continue;
                    TemplateType next = Lookup(t.To);
                    if (next == null) continue;
                    Point origin = new Point(last.Origin.X + t.Offset.X, last.Origin.Y + t.Offset.Y);
                    Rectangle box = new Rectangle(origin, new Size(next.IconWidth, next.IconHeight));
                    if (!area.Contains(box)) continue;
                    int side = SideIndex(catalog, t.To);
                    if (desiredSide >= 0 && (side < 0 || TurnDistance(side, desiredSide) > 1)) continue;
                    if (!SealedAdjacency(lastBox, box)) continue;
                    if (desiredSide >= 0 && !SeamSealed(last.Piece, last.Origin, next, origin)) continue;
                    if (desiredSide < 0 && !WatersTouch(last.Piece, last.Origin, next, origin)) continue;
                    chosen = t;
                    chosenType = next;
                    break;
                }
                if (chosen == null) return;
                path.Add(new Step(new Point(last.Origin.X + chosen.Offset.X, last.Origin.Y + chosen.Offset.Y), chosenType));
            }
        }

        /// <summary>
        /// Drains ill-formed water pockets: where a violating junction's water is fill
        /// in a small enclosed pool, the whole pool recedes to grass — a pool with no
        /// drawn shore should never have been water. Open water is left alone (receding
        /// it just moves the bare line); junctions on open water are the surgery's job
        /// to prevent, and the audit keeps the score.
        /// </summary>
        public static void EnforceShoreRule(Map map, int maxPocket = 48)
        {
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
            bool IsFill(Point p) => map.Bounds.Contains(p)
                && map.Templates[p.Y, p.X]?.Type?.Name is string n && (n == "w1" || n == "w2" || n == "sh55");
            int previous = int.MaxValue;
            var passUndo = new Dictionary<int, Template>();
            for (int pass = 0; pass < 12; pass++)
            {
                List<(Point Water, Point Land)> violations = AuditShoreRule(map);
                // A pass that did not reduce the count is structural (open water against
                // a cut only surgery can dress): eroding further just marches a bare
                // line through the sea — take the pass back and leave the original for
                // the audit to report, rather than trading water for bitten grass.
                if (violations.Count >= previous)
                {
                    foreach (KeyValuePair<int, Template> cell in passUndo) map.Templates[cell.Key] = cell.Value;
                    return;
                }
                if (violations.Count == 0) return;
                previous = violations.Count;
                passUndo = new Dictionary<int, Template>();
                var handled = new HashSet<Point>();
                foreach ((Point water, _) in violations)
                {
                    if (!handled.Add(water)) continue;
                    if (!IsFill(water))
                    {
                        // A piece's stray water cell poking out on its land side (at most
                        // one water neighbour) is an isolated poke: erasing that one cell
                        // reads as shore irregularity. Anything better-connected is real
                        // coast art and stays for the audit to report.
                        int wet = 0;
                        foreach (Point n in new[] { new Point(water.X + 1, water.Y), new Point(water.X - 1, water.Y), new Point(water.X, water.Y + 1), new Point(water.X, water.Y - 1) })
                        {
                            if (!map.Bounds.Contains(n)) continue;
                            Template t = map.Templates[n.Y, n.X];
                            LandType land = t?.Type == null ? LandType.Clear : t.Type.GetLandType(t.Icon);
                            if (land == LandType.Water || land == LandType.River) wet++;
                        }
                        if (wet <= 1) TemplateEdit.Erase(map.Templates, null, null, water, passUndo, redo);
                        continue;
                    }
                    // A small enclosed pool with no drawn shore drains whole; a junction
                    // cell on bigger water recedes alone and usually exposes drawn coast
                    // right behind it.
                    var pocket = new List<Point> { water };
                    var frontier = new Queue<Point>();
                    frontier.Enqueue(water);
                    bool small = true;
                    while (frontier.Count > 0 && small)
                    {
                        Point p = frontier.Dequeue();
                        foreach (Point n in new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) })
                        {
                            if (!IsFill(n) || !handled.Add(n)) continue;
                            pocket.Add(n);
                            frontier.Enqueue(n);
                            if (pocket.Count > maxPocket) { small = false; break; }
                        }
                    }
                    foreach (Point p in small ? pocket : new List<Point> { water })
                    {
                        TemplateEdit.Erase(map.Templates, null, null, p, passUndo, redo);
                    }
                }
            }
        }

        /// <summary>
        /// Drowns orphan islets: a tiny scrap of land (six cells or fewer, stray beach
        /// nubs and eroded grass holes included) completely surrounded by water becomes
        /// water — nothing that small is a real island, and it reads as debris.
        /// </summary>
        public static void DrownOrphanIslets(Map map, MobiusEditor.Utility.DeterministicRandom random)
        {
            TemplateType water = map.TemplateTypes.FirstOrDefault(t =>
                (t.Flags & TemplateTypeFlag.DefaultFill) == TemplateTypeFlag.DefaultFill && t.ExistsInTheater
                && t.LandTypes != null && t.LandTypes.Length > 0 && t.LandTypes[0] == LandType.Water);
            if (water == null) return;
            bool IsWaterCell(Point p) => map.Templates[p.Y, p.X]?.Type is TemplateType t
                && t.GetLandType(map.Templates[p.Y, p.X].Icon) is LandType land
                && (land == LandType.Water || land == LandType.River);
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
            var seen = new HashSet<Point>();
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    Point start = new Point(x, y);
                    if (IsWaterCell(start) || !seen.Add(start)) continue;
                    // Components join diagonally: shore art chains its land cells corner
                    // to corner, and a nub that touches the coast diagonally is part of
                    // the shoreline, not debris.
                    var component = new List<Point> { start };
                    var frontier = new Queue<Point>();
                    frontier.Enqueue(start);
                    bool small = true, surrounded = true;
                    while (frontier.Count > 0 && small)
                    {
                        Point p = frontier.Dequeue();
                        for (int dy = -1; dy <= 1 && small; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                Point n = new Point(p.X + dx, p.Y + dy);
                                if (!map.Bounds.Contains(n)) { surrounded = false; continue; }
                                if (IsWaterCell(n)) continue;
                                // Land already claimed by an earlier (larger) scan means
                                // this is a shard of real coast, not an orphan.
                                if (!seen.Add(n)) { small = false; break; }
                                component.Add(n);
                                frontier.Enqueue(n);
                                if (component.Count > 6) { small = false; break; }
                            }
                        }
                    }
                    if (!small || !surrounded) continue;
                    foreach (Point p in component)
                    {
                        TemplateEdit.Place(map.TemplateTypes, map.Templates, water, p, null, random, undo, redo);
                    }
                }
            }
        }

        /// <summary>
        /// Continues water into the non-playable border frame: every frame cell whose
        /// nearest in-bounds cell is water becomes water, so the sea does not stop at a
        /// grass picture-frame around the playable area.
        /// </summary>
        public static void ExtendWaterIntoBorder(Map map, MobiusEditor.Utility.DeterministicRandom random)
        {
            TemplateType water = map.TemplateTypes.FirstOrDefault(t =>
                (t.Flags & TemplateTypeFlag.DefaultFill) == TemplateTypeFlag.DefaultFill && t.ExistsInTheater
                && t.LandTypes != null && t.LandTypes.Length > 0 && t.LandTypes[0] == LandType.Water);
            if (water == null) return;
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
            Rectangle b = map.Bounds;
            for (int y = 0; y < map.Metrics.Height; y++)
            {
                for (int x = 0; x < map.Metrics.Width; x++)
                {
                    Point cell = new Point(x, y);
                    if (b.Contains(cell)) continue;
                    Point inside = new Point(Math.Clamp(x, b.Left, b.Right - 1), Math.Clamp(y, b.Top, b.Bottom - 1));
                    LandType land = map.Templates[inside.Y, inside.X]?.Type is TemplateType t
                        ? t.GetLandType(map.Templates[inside.Y, inside.X].Icon) : LandType.Clear;
                    if (land != LandType.Water && land != LandType.River) continue;
                    if (map.Templates[cell.Y, cell.X]?.Type != null) continue;
                    TemplateEdit.Place(map.TemplateTypes, map.Templates, water, cell, null, random, undo, redo);
                }
            }
        }

        private static int Chebyshev(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        /// <summary>
        /// Audits the map against the shore rule: between water and land there is always
        /// a cliff or a beach — no exceptions. Authored pieces paint that transition
        /// inside their own art, so a water cell may touch a clear cell only when both
        /// belong to the SAME stamp (same template, icons consistent with the offset);
        /// any cross-stamp water-against-clear junction has no drawn transition and is a
        /// violation. Returns each violating pair once, water cell first.
        /// </summary>
        public static List<(Point Water, Point Land)> AuditShoreRule(Map map)
        {
            var violations = new List<(Point, Point)>();
            LandType LandOf(Point p)
            {
                Template t = map.Templates[p.Y, p.X];
                return t?.Type == null ? LandType.Clear : t.Type.GetLandType(t.Icon);
            }
            bool SameStamp(Point a, Point b)
            {
                Template ta = map.Templates[a.Y, a.X], tb = map.Templates[b.Y, b.X];
                if (ta?.Type == null || tb?.Type == null || ta.Type != tb.Type) return false;
                return tb.Icon - ta.Icon == (b.Y - a.Y) * ta.Type.IconWidth + (b.X - a.X);
            }
            // River-typed cells are self-banked water: the rv/ford art draws its own
            // banks inside the tile (that is what LandType.River encodes — official
            // maps run rv streams straight through open meadow). Only bare Water needs
            // a drawn shore.
            static bool IsWater(LandType l) => l == LandType.Water;
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    Point a = new Point(x, y);
                    LandType la = LandOf(a);
                    foreach (Point b in new[] { new Point(x + 1, y), new Point(x, y + 1) })
                    {
                        if (!map.Bounds.Contains(b)) continue;
                        LandType lb = LandOf(b);
                        Point water, land;
                        if (IsWater(la) && lb == LandType.Clear) { water = a; land = b; }
                        else if (IsWater(lb) && la == LandType.Clear) { water = b; land = a; }
                        else continue;
                        if (!SameStamp(a, b)) violations.Add((water, land));
                    }
                }
            }
            return violations;
        }

        /// <summary>
        /// Whether laying this piece into the composite of already-placed cells creates
        /// no cross-stamp water-against-clear junction: each painted cell is checked
        /// against the surviving neighbours it does not itself overwrite.
        /// </summary>
        private static bool CompositeSeals(TemplateType piece, Point at, IReadOnlyDictionary<Point, LandType> placed,
            bool protectCourse = false)
        {
            LandType[,] grid = ShoreCatalog.LandGrid(piece);
            // River-typed cells are self-banked (see AuditShoreRule).
            static bool IsWater(LandType l) => l == LandType.Water;
            static bool IsCourse(LandType l) => l == LandType.Water || l == LandType.River;
            bool Paints(Point p) => p.X >= at.X && p.X < at.X + piece.IconWidth
                && p.Y >= at.Y && p.Y < at.Y + piece.IconHeight
                && grid[p.Y - at.Y, p.X - at.X] != LandType.None;
            for (int y = 0; y < piece.IconHeight; y++)
            {
                for (int x = 0; x < piece.IconWidth; x++)
                {
                    if (grid[y, x] == LandType.None) continue;
                    LandType mine = grid[y, x];
                    Point world = new Point(at.X + x, at.Y + y);
                    // A stream walk must not bury laid watercourse under land: that is
                    // what chops the river at overlapping bends.
                    if (protectCourse && !IsCourse(mine)
                        && placed.TryGetValue(world, out LandType beneath) && IsCourse(beneath)) return false;
                    foreach (Point n in new[]
                    {
                        new Point(world.X + 1, world.Y), new Point(world.X - 1, world.Y),
                        new Point(world.X, world.Y + 1), new Point(world.X, world.Y - 1),
                    })
                    {
                        if (Paints(n)) continue;
                        if (!placed.TryGetValue(n, out LandType other)) continue;
                        if ((IsWater(mine) && other == LandType.Clear) || (mine == LandType.Clear && IsWater(other))) return false;
                    }
                }
            }
            return true;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string, Point), bool> waterTouch =
            new System.Collections.Concurrent.ConcurrentDictionary<(string, string, Point), bool>();

        /// <summary>
        /// How many separate course arms leave this piece: clusters of River-typed cells
        /// along the box perimeter, circular. A through-piece has two; a spring has one,
        /// a fork three — chained single-strand, anything but two dangles an arm that
        /// abruptly begins or ends mid-map. An all-water perimeter counts as a through.
        /// </summary>
        public static int CourseExits(TemplateType piece)
        {
            LandType[,] grid = ShoreCatalog.LandGrid(piece);
            int w = piece.IconWidth, h = piece.IconHeight;
            var ring = new List<bool>();
            for (int x = 0; x < w; x++) ring.Add(grid[0, x] == LandType.River);
            for (int y = 1; y < h; y++) ring.Add(grid[y, w - 1] == LandType.River);
            if (h > 1) for (int x = w - 2; x >= 0; x--) ring.Add(grid[h - 1, x] == LandType.River);
            if (w > 1) for (int y = h - 2; y >= 1; y--) ring.Add(grid[y, 0] == LandType.River);
            if (ring.All(v => v)) return 2;
            int clusters = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i] && !ring[(i + ring.Count - 1) % ring.Count]) clusters++;
            }
            return clusters;
        }

        /// <summary>Whether the two pieces' painted water cells touch (8-way) at this offset — watercourse continuity; true when either piece is dry.</summary>
        private static bool WatersTouch(TemplateType a, Point ao, TemplateType b, Point bo)
        {
            (string, string, Point) key = (a.Name, b.Name, new Point(bo.X - ao.X, bo.Y - ao.Y));
            if (waterTouch.TryGetValue(key, out bool cached)) return cached;
            static bool IsWater(LandType l) => l == LandType.Water || l == LandType.River;
            List<Point> Cells(TemplateType t, Point at)
            {
                LandType[,] grid = ShoreCatalog.LandGrid(t);
                List<Point> cells = new List<Point>();
                for (int y = 0; y < t.IconHeight; y++)
                    for (int x = 0; x < t.IconWidth; x++)
                        if (grid[y, x] != LandType.None && IsWater(grid[y, x])) cells.Add(new Point(at.X + x, at.Y + y));
                return cells;
            }
            List<Point> wa = Cells(a, ao), wb = Cells(b, bo);
            bool touch = wa.Count == 0 || wb.Count == 0;
            foreach (Point p in wa)
            {
                foreach (Point q in wb)
                {
                    // Orthogonal or overlapping: a corner-only water touch renders as a
                    // broken bend — water only reads as flowing across a shared edge.
                    if (Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y) <= 1) { touch = true; break; }
                }
                if (touch) break;
            }
            if (touch)
            {
                // The successor must not bury the predecessor's course under its own
                // land cells — the other way bends get chopped where stamps overlap.
                LandType[,] gb = ShoreCatalog.LandGrid(b);
                foreach (Point p in wa)
                {
                    int lx = p.X - bo.X, ly = p.Y - bo.Y;
                    if (lx < 0 || ly < 0 || lx >= b.IconWidth || ly >= b.IconHeight) continue;
                    LandType over = gb[ly, lx];
                    if (over != LandType.None && !IsWater(over)) { touch = false; break; }
                }
            }
            waterTouch[key] = touch;
            return touch;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string, Point), bool> landTouch =
            new System.Collections.Concurrent.ConcurrentDictionary<(string, string, Point), bool>();

        /// <summary>
        /// Whether two pieces at this offset continue each other's coast: their painted
        /// land cells touch (8-way) AND their painted water cells touch. Box adjacency
        /// alone does not seal a coast — masked and water cells are no barrier, and a
        /// pair whose land bands never meet leaves a channel the flood pours through; a
        /// pair whose water bands never meet breaks the waterline's art mid-shore. In
        /// the corpus a third overlapping piece covered such pairs; a single-strand walk
        /// must reject them. An 8-connected land chain blocks 4-connected flood water.
        /// </summary>
        private static bool SeamSealed(TemplateType a, Point ao, TemplateType b, Point bo)
        {
            (string, string, Point) key = (a.Name, b.Name, new Point(bo.X - ao.X, bo.Y - ao.Y));
            if (landTouch.TryGetValue(key, out bool cached)) return cached;
            static bool IsLand(LandType l) => l == LandType.Clear || l == LandType.Beach
                || l == LandType.Rock || l == LandType.Rough || l == LandType.Road;
            static bool IsWater(LandType l) => l == LandType.Water || l == LandType.River;
            List<Point> Cells(TemplateType t, Point at, Func<LandType, bool> keep)
            {
                LandType[,] grid = ShoreCatalog.LandGrid(t);
                List<Point> cells = new List<Point>();
                for (int y = 0; y < t.IconHeight; y++)
                    for (int x = 0; x < t.IconWidth; x++)
                        if (grid[y, x] != LandType.None && keep(grid[y, x])) cells.Add(new Point(at.X + x, at.Y + y));
                return cells;
            }
            static bool Touch(List<Point> first, List<Point> second)
            {
                foreach (Point p in first)
                    foreach (Point q in second)
                        if (Chebyshev(p, q) <= 1) return true;
                return false;
            }
            bool sealed_ = Touch(Cells(a, ao, IsLand), Cells(b, bo, IsLand))
                && Touch(Cells(a, ao, IsWater), Cells(b, bo, IsWater));
            if (sealed_)
            {
                // The shore rule at the seam: laid together (the later piece overwrites
                // the overlap), no water cell of one piece may touch a clear cell of the
                // other — that junction would have no drawn beach or cliff.
                Dictionary<Point, (LandType Land, bool FromB)> union = new Dictionary<Point, (LandType, bool)>();
                foreach (Point p in Cells(a, ao, _ => true)) union[p] = (Grid(a, p, ao), false);
                foreach (Point p in Cells(b, bo, _ => true)) union[p] = (Grid(b, p, bo), true);
                static LandType Grid(TemplateType t, Point world, Point at)
                    => ShoreCatalog.LandGrid(t)[world.Y - at.Y, world.X - at.X];
                foreach (KeyValuePair<Point, (LandType Land, bool FromB)> cell in union)
                {
                    if (!IsWater(cell.Value.Land)) continue;
                    foreach (Point n in new[]
                    {
                        new Point(cell.Key.X + 1, cell.Key.Y), new Point(cell.Key.X - 1, cell.Key.Y),
                        new Point(cell.Key.X, cell.Key.Y + 1), new Point(cell.Key.X, cell.Key.Y - 1),
                    })
                    {
                        if (union.TryGetValue(n, out (LandType Land, bool FromB) other)
                            && other.Land == LandType.Clear && other.FromB != cell.Value.FromB)
                        {
                            sealed_ = false;
                            break;
                        }
                    }
                    if (!sealed_) break;
                }
            }
            landTouch[key] = sealed_;
            return sealed_;
        }

        /// <summary>Boxes overlap, or abut along an edge with at least one shared cell.</summary>
        private static bool SealedAdjacency(Rectangle a, Rectangle b)
        {
            if (a.IntersectsWith(b)) return true;
            int sharedX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            int sharedY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            bool vertical = (a.Right == b.Left || b.Right == a.Left) && sharedY >= 1;
            bool horizontal = (a.Bottom == b.Top || b.Bottom == a.Top) && sharedX >= 1;
            return vertical || horizontal;
        }

        /// <summary>
        /// The most idiom-rich starting piece whose water lies on the desired side —
        /// deterministic, so the same map regenerates identically.
        /// </summary>
        public static string BestStartPiece(Map map, TransitionGraph graph,
            IReadOnlyDictionary<string, ShorePiece> catalog, int desiredSide, string prefix = null)
        {
            return catalog.Values
                .Where(p => SideOf(p.WaterSide) == desiredSide && p.Template.ExistsInTheater
                    && (prefix == null || p.Template.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => graph.From(p.Template.Name).Sum(t => t.Count))
                .ThenBy(p => p.Template.Name, StringComparer.Ordinal)
                .Select(p => p.Template.Name)
                .FirstOrDefault();
        }

        private static int SideOf(ShoreSide side) => side switch
        {
            ShoreSide.North => 0,
            ShoreSide.NorthEast => 1,
            ShoreSide.East => 2,
            ShoreSide.SouthEast => 3,
            ShoreSide.South => 4,
            ShoreSide.SouthWest => 5,
            ShoreSide.West => 6,
            ShoreSide.NorthWest => 7,
            _ => -1,
        };

        /// <summary>The piece's water side as one of 8 compass steps, or -1 when unclassified.</summary>
        private static int SideIndex(IReadOnlyDictionary<string, ShorePiece> catalog, string piece)
        {
            if (!catalog.TryGetValue(piece, out ShorePiece p)) return -1;
            return p.WaterSide switch
            {
                ShoreSide.North => 0,
                ShoreSide.NorthEast => 1,
                ShoreSide.East => 2,
                ShoreSide.SouthEast => 3,
                ShoreSide.South => 4,
                ShoreSide.SouthWest => 5,
                ShoreSide.West => 6,
                ShoreSide.NorthWest => 7,
                _ => -1,
            };
        }

        private static int TurnDistance(int a, int b)
        {
            int d = Math.Abs(a - b) % 8;
            return Math.Min(d, 8 - d);
        }
    }
}
