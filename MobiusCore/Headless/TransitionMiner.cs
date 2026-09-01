//
// Mines construction idioms from existing maps: every placement of the terrain-family
// templates (shores, water cliffs, rivers, roads) is recorded together with the pieces
// it touches and their relative offsets. The resulting transition table is an empirical
// grammar of how the original mappers chained pieces — corners, bends and junctions
// included — which generated coastlines can replay instead of deriving geometry rules.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Model;

namespace MobiusEditor.Headless
{
    /// <summary>One observed piece-to-piece adjacency: B's origin relative to A's, with how often it occurs.</summary>
    public sealed class PieceTransition
    {
        public string From { get; }
        public string To { get; }
        public Point Offset { get; }
        public int Count { get; set; }

        public PieceTransition(string from, string to, Point offset)
        {
            From = from;
            To = to;
            Offset = offset;
        }

        public override string ToString() => $"{From} -> {To} @{Offset.X},{Offset.Y} x{Count}";
    }

    public static class TransitionMiner
    {
        /// <summary>
        /// Reconstructs the placed template instances of a map: each contiguous run of cells
        /// holding the same template type with coherent icon indices becomes one instance
        /// anchored at the cell carrying icon 0 (or the run's top-left for masked pieces).
        /// </summary>
        public static Dictionary<Point, TemplateType> Instances(Map map)
        {
            Dictionary<Point, TemplateType> instances = new Dictionary<Point, TemplateType>();
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    Template t = map.Templates[y, x];
                    if (t?.Type == null || t.Type.IconWidth * t.Type.IconHeight <= 1) continue;
                    // The instance origin is this cell minus the icon's position in the stamp.
                    int iconX = t.Icon % t.Type.IconWidth, iconY = t.Icon / t.Type.IconWidth;
                    Point origin = new Point(x - iconX, y - iconY);
                    if (!instances.ContainsKey(origin)) instances[origin] = t.Type;
                }
            }
            return instances;
        }

        /// <summary>
        /// All piece-to-piece transitions in a map among templates whose names match the
        /// given family prefixes, keyed From/To/Offset with occurrence counts. Only pieces
        /// whose bounding boxes touch or overlap are related.
        /// </summary>
        public static Dictionary<(string From, string To, Point Offset), PieceTransition> Mine(
            Map map, IReadOnlyList<string> familyPrefixes,
            Dictionary<(string, string, Point), PieceTransition> accumulate = null)
        {
            bool InFamily(TemplateType t) => familyPrefixes.Any(p =>
                t.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                && t.Name.Length > p.Length && char.IsDigit(t.Name[p.Length]));
            var table = accumulate ?? new Dictionary<(string, string, Point), PieceTransition>();
            var instances = Instances(map).Where(kv => InFamily(kv.Value)).ToList();
            foreach ((Point originA, TemplateType a) in instances)
            {
                Rectangle boxA = new Rectangle(originA, new Size(a.IconWidth, a.IconHeight));
                Rectangle reach = boxA;
                reach.Inflate(1, 1);
                foreach ((Point originB, TemplateType b) in instances)
                {
                    if (originA == originB) continue;
                    Rectangle boxB = new Rectangle(originB, new Size(b.IconWidth, b.IconHeight));
                    if (!reach.IntersectsWith(boxB)) continue;
                    Point offset = new Point(originB.X - originA.X, originB.Y - originA.Y);
                    // Record each ordered pair once, from the lexicographically first origin,
                    // so a neighbouring pair is one observation rather than two mirrored ones.
                    if (originB.Y < originA.Y || (originB.Y == originA.Y && originB.X < originA.X)) continue;
                    var key = (a.Name, b.Name, offset);
                    if (!table.TryGetValue(key, out PieceTransition transition))
                    {
                        table[key] = transition = new PieceTransition(a.Name, b.Name, offset);
                    }
                    transition.Count++;
                }
            }
            return table;
        }
    }
}
