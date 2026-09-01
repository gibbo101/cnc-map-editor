//
// Classifies a game's shore templates from their per-icon land data, so the map
// generator can draw coastlines out of real shore pieces instead of hard water/land
// edges. The land grids come straight from the template tables (the same data the
// game derives passability from), so the catalog needs no hand-maintained lists.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Model;

namespace MobiusEditor.Headless
{
    public enum ShoreKind
    {
        /// <summary>A cardinal run: the coast crosses it horizontally or vertically.</summary>
        Straight,
        /// <summary>A 45-degree run: the coast crosses it diagonally; WaterSide names the diagonal the water fills.</summary>
        Diagonal,
        /// <summary>A small joint piece (mostly 1x2/2x2): used to round convex or concave turns.</summary>
        Joint,
        /// <summary>Anything the generator should not place on its own.</summary>
        Other,
    }

    /// <summary>Which side (or corner) of the piece the water lies on.</summary>
    public enum ShoreSide { North, South, East, West, NorthEast, NorthWest, SouthEast, SouthWest, None }

    public sealed class ShorePiece
    {
        public TemplateType Template { get; }
        public ShoreKind Kind { get; }
        public ShoreSide WaterSide { get; }

        public ShorePiece(TemplateType template, ShoreKind kind, ShoreSide side)
        {
            Template = template;
            Kind = kind;
            WaterSide = side;
        }

        public override string ToString() => $"{Template.Name} {Template.IconWidth}x{Template.IconHeight} {Kind} water={WaterSide}";
    }

    public static class ShoreCatalog
    {
        private static bool IsWater(LandType t) => t == LandType.Water || t == LandType.River;
        private static bool IsLand(LandType t) => t == LandType.Clear || t == LandType.Rough || t == LandType.Road || t == LandType.Rock;

        /// <summary>The land grid, row-major, with masked-out icons as None.</summary>
        public static LandType[,] LandGrid(TemplateType template)
        {
            int w = template.IconWidth, h = template.IconHeight;
            LandType[,] grid = new LandType[h, w];
            bool[,] mask = template.IconMask;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool present = mask == null || (y < mask.GetLength(0) && x < mask.GetLength(1) && mask[y, x]);
                    grid[y, x] = present ? template.GetLandType(y * w + x) : LandType.None;
                }
            }
            return grid;
        }

        /// <summary>
        /// Classifies one piece by quadrant dominance: each quarter of the piece votes
        /// water or land. Two uniform halves make a cardinal Straight; a land corner
        /// opposing a water corner makes a 45-degree Diagonal run (WaterSide = the
        /// diagonal the water fills); pieces too small to quarter are Joints.
        /// </summary>
        public static ShorePiece Classify(TemplateType template)
        {
            LandType[,] grid = LandGrid(template);
            int h = grid.GetLength(0), w = grid.GetLength(1);
            double waterX = 0, waterY = 0, landX = 0, landY = 0;
            int waterCount = 0, landCount = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (IsWater(grid[y, x])) { waterX += x; waterY += y; waterCount++; }
                    else if (IsLand(grid[y, x])) { landX += x; landY += y; landCount++; }
                }
            }
            if (waterCount == 0 || landCount == 0) return new ShorePiece(template, ShoreKind.Other, ShoreSide.None);
            double dx = waterX / waterCount - landX / landCount;
            double dy = waterY / waterCount - landY / landCount;
            ShoreSide centroidCorner = dy < 0
                ? (dx < 0 ? ShoreSide.NorthWest : ShoreSide.NorthEast)
                : (dx < 0 ? ShoreSide.SouthWest : ShoreSide.SouthEast);
            if (w < 3 || h < 3)
            {
                return new ShorePiece(template, ShoreKind.Joint, centroidCorner);
            }

            int Dominance(int x0, int x1, int y0, int y1)
            {
                int water = 0, land = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        if (IsWater(grid[y, x])) water++;
                        else if (IsLand(grid[y, x])) land++;
                    }
                }
                return water > land ? 1 : land > water ? -1 : 0;
            }
            int midX = (w + 1) / 2, midY = (h + 1) / 2;
            int nw = Dominance(0, midX, 0, midY), ne = Dominance(w - midX, w, 0, midY);
            int sw = Dominance(0, midX, h - midY, h), se = Dominance(w - midX, w, h - midY, h);

            if (nw < 0 && ne < 0 && sw > 0 && se > 0) return new ShorePiece(template, ShoreKind.Straight, ShoreSide.South);
            if (nw > 0 && ne > 0 && sw < 0 && se < 0) return new ShorePiece(template, ShoreKind.Straight, ShoreSide.North);
            if (nw > 0 && sw > 0 && ne < 0 && se < 0) return new ShorePiece(template, ShoreKind.Straight, ShoreSide.West);
            if (nw < 0 && sw < 0 && ne > 0 && se > 0) return new ShorePiece(template, ShoreKind.Straight, ShoreSide.East);

            if (nw < 0 && se > 0) return new ShorePiece(template, ShoreKind.Diagonal, ShoreSide.SouthEast);
            if (se < 0 && nw > 0) return new ShorePiece(template, ShoreKind.Diagonal, ShoreSide.NorthWest);
            if (ne < 0 && sw > 0) return new ShorePiece(template, ShoreKind.Diagonal, ShoreSide.SouthWest);
            if (sw < 0 && ne > 0) return new ShorePiece(template, ShoreKind.Diagonal, ShoreSide.NorthEast);

            return new ShorePiece(template, ShoreKind.Diagonal, centroidCorner);
        }

        /// <summary>
        /// All shore pieces of the family, classified. The game's own equivalence groups are
        /// authoritative: when any member of a group classifies as a Straight, every member
        /// takes that classification — the ragged variants are interchangeable by design.
        /// </summary>
        public static List<ShorePiece> Build(IEnumerable<TemplateType> templates, string prefix = "sh")
        {
            List<ShorePiece> pieces = templates
                .Where(t => t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && t.Name.Length > prefix.Length && char.IsDigit(t.Name[prefix.Length]))
                .Select(Classify)
                .ToList();
            var byName = pieces.ToDictionary(p => p.Template.Name, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pieces.Count; i++)
            {
                ShorePiece piece = pieces[i];
                string[] group = piece.Template.GroupTiles;
                if (piece.Kind == ShoreKind.Straight || group == null || group.Length == 0) continue;
                ShorePiece straightSibling = group
                    .Where(byName.ContainsKey)
                    .Select(n => byName[n])
                    .FirstOrDefault(p => p.Kind == ShoreKind.Straight);
                if (straightSibling != null)
                {
                    pieces[i] = new ShorePiece(piece.Template, ShoreKind.Straight, straightSibling.WaterSide);
                }
            }
            return pieces;
        }
    }
}
