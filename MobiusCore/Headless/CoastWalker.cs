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
            string startPiece, Point startOrigin, Point goal, int maxPieces)
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

        private static int Chebyshev(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

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
