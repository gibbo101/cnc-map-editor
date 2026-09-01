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
        /// </summary>
        public static List<Step> PlanPath(Map map, TransitionGraph graph,
            IReadOnlyDictionary<string, ShorePiece> catalog,
            string startPiece, Point startOrigin, Point goal, int maxPieces, int desiredSide = -1)
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
                if (Chebyshev(origin, goal) <= 2 && (piece, origin) != (startPiece, startOrigin))
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
                    Point nextOrigin = new Point(origin.X + t.Offset.X, origin.Y + t.Offset.Y);
                    Rectangle box = new Rectangle(nextOrigin, new Size(next.IconWidth, next.IconHeight));
                    if (!map.Bounds.Contains(box)) continue;
                    int toSide = SideIndex(catalog, t.To);
                    if (fromSide >= 0 && toSide >= 0 && TurnDistance(fromSide, toSide) > 2) continue;
                    // The whole segment keeps its water on the commanded side (within 45°),
                    // or the coast slowly rotates and the flood side leaks.
                    if (desiredSide >= 0 && toSide >= 0 && TurnDistance(toSide, desiredSide) > 1) continue;
                    // Consecutive pieces must share an edge, not merely touch corners —
                    // corner-only contact leaves a diagonal gap the water pours through.
                    TemplateType current = Lookup(piece);
                    Rectangle boxA = new Rectangle(origin, new Size(current.IconWidth, current.IconHeight));
                    if (!SealedAdjacency(boxA, box)) continue;
                    double stepCost = 1.0 + 4.0 / (t.Count + 1);
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
        /// Stamps a planned path onto the map, in order, then seals each piece's masked
        /// cells on its water side with water tiles — in the corpus maps those transparent
        /// cells sit over open water, and without them the coast line leaks.
        /// </summary>
        public static void Place(Map map, IEnumerable<Step> path,
            IReadOnlyDictionary<string, ShorePiece> catalog, MobiusEditor.Utility.DeterministicRandom random)
        {
            TemplateType water = map.TemplateTypes.FirstOrDefault(t =>
                (t.Flags & TemplateTypeFlag.DefaultFill) == TemplateTypeFlag.DefaultFill && t.ExistsInTheater
                && t.LandTypes != null && t.LandTypes.Length > 0 && t.LandTypes[0] == LandType.Water);
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
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
        public static bool FloodWater(Map map, Point seed, int maxCells, MobiusEditor.Utility.DeterministicRandom random)
        {
            TemplateType water = map.TemplateTypes.FirstOrDefault(t =>
                (t.Flags & TemplateTypeFlag.DefaultFill) == TemplateTypeFlag.DefaultFill && t.ExistsInTheater
                && t.LandTypes != null && t.LandTypes.Length > 0 && t.LandTypes[0] == LandType.Water);
            TemplateType patch = map.TemplateTypes.FirstOrDefault(t => t.Name == "w2" && t.ExistsInTheater);
            if (water == null) return false;
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
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
        public static void ExtendToEdge(Map map, TransitionGraph graph,
            IReadOnlyDictionary<string, ShorePiece> catalog, List<Step> path, int edge, int desiredSide)
        {
            TemplateType Lookup(string name) => map.TemplateTypes.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
            for (int guard = 0; guard < 40; guard++)
            {
                Step last = path[^1];
                Rectangle lastBox = new Rectangle(last.Origin, new Size(last.Piece.IconWidth, last.Piece.IconHeight));
                bool flush = edge switch
                {
                    0 => lastBox.Top <= map.Bounds.Top,
                    1 => lastBox.Bottom >= map.Bounds.Bottom,
                    2 => lastBox.Right >= map.Bounds.Right,
                    _ => lastBox.Left <= map.Bounds.Left,
                };
                if (flush) return;
                PieceTransition chosen = null;
                TemplateType chosenType = null;
                foreach (PieceTransition t in graph.From(last.Piece.Name))
                {
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
                    if (!map.Bounds.Contains(box)) continue;
                    int side = SideIndex(catalog, t.To);
                    if (desiredSide >= 0 && side >= 0 && TurnDistance(side, desiredSide) > 1) continue;
                    if (!SealedAdjacency(lastBox, box)) continue;
                    chosen = t;
                    chosenType = next;
                    break;
                }
                if (chosen == null) return;
                path.Add(new Step(new Point(last.Origin.X + chosen.Offset.X, last.Origin.Y + chosen.Offset.Y), chosenType));
            }
        }

        /// <summary>
        /// Softens the rare seam where flood-filled open water directly touches bare clear
        /// land (a chain side-step the corpus would have covered with another piece): the
        /// water cell becomes the 1x1 shallow tile, the same transition official maps use.
        /// </summary>
        public static void PatchBareContacts(Map map, MobiusEditor.Utility.DeterministicRandom random)
        {
            TemplateType shallow = map.TemplateTypes.FirstOrDefault(t =>
                t.Name.Equals("sh55", StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater);
            if (shallow == null) return;
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
            List<Point> patches = new List<Point>();
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    Template t = map.Templates[y, x];
                    if (t?.Type == null || (t.Type.Name != "w1" && t.Type.Name != "w2")) continue;
                    foreach (Point n in new[] { new Point(x + 1, y), new Point(x - 1, y), new Point(x, y + 1), new Point(x, y - 1) })
                    {
                        if (!map.Bounds.Contains(n)) continue;
                        if (LakeBuilder.LandAt(map, n) == LandType.Clear)
                        {
                            patches.Add(new Point(x, y));
                            break;
                        }
                    }
                }
            }
            foreach (Point p in patches)
            {
                TemplateEdit.Place(map.TemplateTypes, map.Templates, shallow, p, null, random, undo, redo);
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
            IReadOnlyDictionary<string, ShorePiece> catalog, int desiredSide)
        {
            return catalog.Values
                .Where(p => SideOf(p.WaterSide) == desiredSide && p.Template.ExistsInTheater)
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
