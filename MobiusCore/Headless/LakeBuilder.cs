//
// Draws water bodies out of real shore templates. Everything is built from 3x3 shore
// blocks: straight runs along coast edges, 45-degree diagonal pieces where the coast
// turns — the land-dominant (convex) variants wrap land around a lake corner, the
// water-dominant (concave) variants wrap water around an island corner or a river-bank
// turn. Piece variants come from the shore catalog, so raggedness varies with the seed
// the way hand-placed shores do.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.Headless
{
    public static class LakeBuilder
    {
        public const int Block = 3;

        private sealed class Vocabulary
        {
            public Map Map;
            public DeterministicRandom Random;
            public Dictionary<int, Template> Undo = new Dictionary<int, Template>(), Redo = new Dictionary<int, Template>();
            public List<TemplateType> North, South, East, West;
            public List<TemplateType> ConvexNE, ConvexNW, ConvexSE, ConvexSW;
            public List<TemplateType> ConcaveNE, ConcaveNW, ConcaveSE, ConcaveSW;
            public TemplateType WaterFill;

            public TemplateType Pick(List<TemplateType> family) => family[Random.Next(family.Count)];

            public void Stamp(TemplateType piece, Point cell)
            {
                TemplateEdit.Place(Map.TemplateTypes, Map.Templates, piece, cell, null, Random, Undo, Redo);
            }

            public TemplateType WaterPatch;

            public void Water(Point cell)
            {
                // Official maps texture open water by scattering 2x2 w2 tiles through w1;
                // the 2x2 must sit fully inside the bounds or it bleeds past the map edge.
                bool patchFits = WaterPatch != null
                    && cell.X + 1 < Map.Bounds.Right && cell.Y + 1 < Map.Bounds.Bottom;
                if (patchFits && Random.Next(4) == 0) Stamp(WaterPatch, cell);
                else Stamp(WaterFill, cell);
            }

            public void Clear(Point cell) => TemplateEdit.Erase(Map.Templates, null, null, cell, Undo, Redo);
        }

        private static Vocabulary Resolve(Map map, IReadOnlyList<ShorePiece> catalog, DeterministicRandom random)
        {
            List<TemplateType> Family(ShoreKind kind, ShoreSide side) => catalog
                .Where(p => p.Kind == kind && p.WaterSide == side
                    && p.Template.IconWidth == Block && p.Template.IconHeight == Block
                    && p.Template.ExistsInTheater)
                .Select(p => p.Template)
                .ToList();
            int LandLead(TemplateType t)
            {
                LandType[,] grid = ShoreCatalog.LandGrid(t);
                int lead = 0;
                foreach (LandType land in grid)
                {
                    if (land == LandType.Clear || land == LandType.Rough || land == LandType.Road || land == LandType.Rock) lead++;
                    else if (land == LandType.Water || land == LandType.River) lead--;
                }
                return lead;
            }
            bool HasRiverCells(TemplateType t)
            {
                foreach (LandType land in ShoreCatalog.LandGrid(t)) if (land == LandType.River) return true;
                return false;
            }
            // River-type cells render as flat shallows: fine tucked into a beach against
            // grass, ugly floating in open sea — prefer the pure-water variants.
            List<TemplateType> PureWaterFirst(List<TemplateType> family)
            {
                List<TemplateType> pure = family.Where(t => !HasRiverCells(t)).ToList();
                return pure.Count > 0 ? pure : family;
            }
            List<TemplateType> Dominant(List<TemplateType> family, bool land)
            {
                if (family.Count == 0) return family;
                int best = land ? family.Max(LandLead) : family.Min(LandLead);
                return PureWaterFirst(family.Where(t => LandLead(t) == best).ToList());
            }
            Vocabulary v = new Vocabulary
            {
                Map = map,
                Random = random,
                North = PureWaterFirst(Family(ShoreKind.Straight, ShoreSide.North)),
                South = PureWaterFirst(Family(ShoreKind.Straight, ShoreSide.South)),
                East = PureWaterFirst(Family(ShoreKind.Straight, ShoreSide.East)),
                West = PureWaterFirst(Family(ShoreKind.Straight, ShoreSide.West)),
                WaterFill = map.TemplateTypes.FirstOrDefault(t =>
                    (t.Flags & TemplateTypeFlag.DefaultFill) == TemplateTypeFlag.DefaultFill && t.ExistsInTheater
                    && t.LandTypes != null && t.LandTypes.Length > 0 && t.LandTypes[0] == LandType.Water),
            };
            v.WaterPatch = map.TemplateTypes.FirstOrDefault(t => t.Name == "w2" && t.ExistsInTheater);
            v.ConvexNE = Dominant(Family(ShoreKind.Diagonal, ShoreSide.NorthEast), land: true);
            v.ConvexNW = Dominant(Family(ShoreKind.Diagonal, ShoreSide.NorthWest), land: true);
            v.ConvexSE = Dominant(Family(ShoreKind.Diagonal, ShoreSide.SouthEast), land: true);
            v.ConvexSW = Dominant(Family(ShoreKind.Diagonal, ShoreSide.SouthWest), land: true);
            v.ConcaveNE = Dominant(Family(ShoreKind.Diagonal, ShoreSide.NorthEast), land: false);
            v.ConcaveNW = Dominant(Family(ShoreKind.Diagonal, ShoreSide.NorthWest), land: false);
            v.ConcaveSE = Dominant(Family(ShoreKind.Diagonal, ShoreSide.SouthEast), land: false);
            v.ConcaveSW = Dominant(Family(ShoreKind.Diagonal, ShoreSide.SouthWest), land: false);
            bool complete = v.WaterFill != null && new[]
            {
                v.North, v.South, v.East, v.West,
                v.ConvexNE, v.ConvexNW, v.ConvexSE, v.ConvexSW,
                v.ConcaveNE, v.ConcaveNW, v.ConcaveSE, v.ConcaveSW,
            }.All(f => f.Count > 0);
            return complete ? v : null;
        }

        /// <summary>True when the catalog and theater can supply every coast family plus the water fill.</summary>
        public static bool HaveVocabulary(Map map, IReadOnlyList<ShorePiece> catalog) =>
            Resolve(map, catalog, new DeterministicRandom(0)) != null;

        /// <summary>
        /// Places a lake (or, inverted, an island in already-laid water) spanning
        /// blocksWide x blocksHigh 3x3 blocks with its top-left at origin. Returns the
        /// covered cell rectangle, or null when the vocabulary or bounds do not allow it.
        /// </summary>
        public static Rectangle? Place(Map map, Point origin, int blocksWide, int blocksHigh,
            DeterministicRandom random, IReadOnlyList<ShorePiece> catalog, bool island = false)
        {
            if (blocksWide < 2 || blocksHigh < 2) return null;
            Rectangle area = new Rectangle(origin.X, origin.Y, blocksWide * Block, blocksHigh * Block);
            if (!map.Bounds.Contains(area)) return null;
            Vocabulary v = Resolve(map, catalog, random);
            if (v == null) return null;

            for (int by = 0; by < blocksHigh; by++)
            {
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    Point cell = new Point(origin.X + bx * Block, origin.Y + by * Block);
                    bool west = bx == 0, east = bx == blocksWide - 1;
                    bool north = by == 0, south = by == blocksHigh - 1;
                    // A lake's ring holds the water inside: its north edge shows water to the
                    // south, and its corners wrap land around the turn. An island is the
                    // inverse: water outside, corners wrapping water.
                    bool ring = north || south || west || east;
                    if (island && ring)
                    {
                        // Masked piece cells show whatever lies beneath; on an island that
                        // must be land, not the sea the bounds were flooded with.
                        for (int y = 0; y < Block; y++)
                            for (int x = 0; x < Block; x++)
                                v.Clear(new Point(cell.X + x, cell.Y + y));
                    }
                    if (north && west) v.Stamp(v.Pick(island ? v.ConcaveNW : v.ConvexSE), cell);
                    else if (north && east) v.Stamp(v.Pick(island ? v.ConcaveNE : v.ConvexSW), cell);
                    else if (south && west) v.Stamp(v.Pick(island ? v.ConcaveSW : v.ConvexNE), cell);
                    else if (south && east) v.Stamp(v.Pick(island ? v.ConcaveSE : v.ConvexNW), cell);
                    else if (north) v.Stamp(v.Pick(island ? v.North : v.South), cell);
                    else if (south) v.Stamp(v.Pick(island ? v.South : v.North), cell);
                    else if (west) v.Stamp(v.Pick(island ? v.West : v.East), cell);
                    else if (east) v.Stamp(v.Pick(island ? v.East : v.West), cell);
                    else
                    {
                        for (int y = 0; y < Block; y++)
                        {
                            for (int x = 0; x < Block; x++)
                            {
                                Point inner = new Point(cell.X + x, cell.Y + y);
                                if (island) v.Clear(inner);
                                else v.Water(inner);
                            }
                        }
                    }
                }
            }
            return area;
        }

        /// <summary>Fills every bounds cell with water tiles — the canvas the island style carves land out of.</summary>
        public static bool FloodBounds(Map map, DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
        {
            Vocabulary v = Resolve(map, catalog, random);
            if (v == null) return false;
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    v.Water(new Point(x, y));
                }
            }
            return true;
        }

        /// <summary>
        /// An ocean along one map edge (0 north, 1 south, 2 east, 3 west): a band of water
        /// depthBlocks deep with a shoreline of straight runs on its inland side.
        /// </summary>
        public static bool PlaceOcean(Map map, int edge, int depthBlocks, DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
        {
            Vocabulary v = Resolve(map, catalog, random);
            if (v == null) return false;
            Rectangle b = map.Bounds;
            int depth = Math.Max(1, depthBlocks) * Block;
            bool horizontal = edge == 0 || edge == 1;
            // The water body itself, bounds edge to the shoreline.
            Rectangle water = edge switch
            {
                0 => new Rectangle(b.Left, b.Top, b.Width, depth),
                1 => new Rectangle(b.Left, b.Bottom - depth, b.Width, depth),
                2 => new Rectangle(b.Right - depth, b.Top, depth, b.Height),
                _ => new Rectangle(b.Left, b.Top, depth, b.Height),
            };
            for (int y = water.Top; y < water.Bottom; y++)
                for (int x = water.Left; x < water.Right; x++)
                    v.Water(new Point(x, y));
            // The shoreline block row/column on the inland side of the band.
            List<TemplateType> family = edge switch { 0 => v.North, 1 => v.South, 2 => v.East, _ => v.West };
            if (horizontal)
            {
                int y = edge == 0 ? water.Bottom : water.Top - Block;
                for (int x = b.Left; x + Block <= b.Right; x += Block) v.Stamp(v.Pick(family), new Point(x, y));
                if (b.Width % Block != 0) v.Stamp(v.Pick(family), new Point(b.Right - Block, y));
            }
            else
            {
                int x = edge == 3 ? water.Right : water.Left - Block;
                for (int y = b.Top; y + Block <= b.Bottom; y += Block) v.Stamp(v.Pick(family), new Point(x, y));
                if (b.Height % Block != 0) v.Stamp(v.Pick(family), new Point(x, b.Bottom - Block));
            }
            return true;
        }

        /// <summary>
        /// A river crossing the whole map top-to-bottom (or left-to-right when vertical is
        /// false): a corridor of water blocks that jogs sideways as it goes, banked with
        /// straight runs, the bank turns taken by concave diagonals. Fords are stamped at
        /// crossable rows so ground forces are never fully cut off. Returns the water
        /// block-spans by row for the caller, or null when the vocabulary is missing.
        /// </summary>
        public static bool PlaceRiver(Map map, bool vertical, int widthBlocks, int fords,
            DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
        {
            Vocabulary v = Resolve(map, catalog, random);
            if (v == null) return false;
            Rectangle b = map.Bounds;
            int along = (vertical ? b.Height : b.Width) / Block;
            int across = (vertical ? b.Width : b.Height) / Block;
            int width = Math.Clamp(widthBlocks, 1, Math.Max(1, across - 4));
            if (along < 2 || across < width + 2) return false;

            // The corridor: water column index (in blocks) per row, jogging by at most 1.
            int[] col = new int[along];
            col[0] = 1 + random.Next(Math.Max(1, across - width - 2));
            // Straight for now: the 3x3 shore set has no watery quarter-turn for every
            // quadrant, so jogging banks leak hard edges. Meanders need the larger
            // diagonal-run pieces chained cell-stepped — the noted follow-up.
            for (int i = 1; i < along; i++)
            {
                col[i] = col[0];
            }
            List<TemplateType> fordFamily = map.TemplateTypes
                .Where(t => t.Name.StartsWith("ford", StringComparison.OrdinalIgnoreCase) && t.ExistsInTheater
                    && t.IconWidth == Block && t.IconHeight == Block)
                .ToList();
            HashSet<int> fordRows = new HashSet<int>();
            for (int i = 0; i < fords * 8 && fordRows.Count < fords && fordFamily.Count > 0; i++)
            {
                int row = 1 + random.Next(Math.Max(1, along - 2));
                // A ford needs the corridor straight through the row so the crossing lines up.
                bool straight = (row == 0 || col[row - 1] == col[row]) && (row == along - 1 || col[row + 1] == col[row]);
                if (straight) fordRows.Add(row);
            }

            Point Cell(int rowBlock, int acrossBlock) => vertical
                ? new Point(b.Left + acrossBlock * Block, b.Top + rowBlock * Block)
                : new Point(b.Left + rowBlock * Block, b.Top + acrossBlock * Block);
            bool WaterAt(int row, int acrossBlock) => row >= 0 && row < along
                && acrossBlock >= col[row] && acrossBlock < col[row] + width;

            for (int row = 0; row < along; row++)
            {
                // Water corridor (or a ford stamped straight across it).
                for (int w = 0; w < width; w++)
                {
                    Point cell = Cell(row, col[row] + w);
                    if (fordRows.Contains(row)) v.Stamp(v.Pick(fordFamily), cell);
                    else
                    {
                        for (int y = 0; y < Block; y++)
                            for (int x = 0; x < Block; x++)
                                v.Water(new Point(cell.X + x, cell.Y + y));
                    }
                }
                // Banks: a straight run facing the water, or a concave diagonal where the
                // corridor jogs past the bank block on the row before or after.
                int nearBank = col[row] - 1, farBank = col[row] + width;
                bool nearBefore = WaterAt(row - 1, nearBank), nearAfter = WaterAt(row + 1, nearBank);
                bool farBefore = WaterAt(row - 1, farBank), farAfter = WaterAt(row + 1, farBank);
                List<TemplateType> nearFamily = vertical
                    ? (nearBefore ? v.ConcaveNE : nearAfter ? v.ConcaveSE : v.East)
                    : (nearBefore ? v.ConcaveSW : nearAfter ? v.ConcaveSE : v.South);
                List<TemplateType> farFamily = vertical
                    ? (farBefore ? v.ConcaveNW : farAfter ? v.ConcaveSW : v.West)
                    : (farBefore ? v.ConcaveNW : farAfter ? v.ConcaveNE : v.North);
                v.Stamp(v.Pick(nearFamily), Cell(row, nearBank));
                v.Stamp(v.Pick(farFamily), Cell(row, farBank));
            }
            // The bounds height rarely divides by 3: extend the last row's corridor to the edge.
            int covered = along * Block;
            int remainder = (vertical ? b.Height : b.Width) - covered;
            for (int extra = 0; extra < remainder; extra++)
            {
                for (int w = -1; w <= width; w++)
                {
                    int acrossBlock = col[along - 1] + w;
                    Point baseCell = Cell(along - 1, acrossBlock);
                    for (int i = 0; i < Block; i++)
                    {
                        Point cell = vertical
                            ? new Point(baseCell.X + i, b.Top + covered + extra)
                            : new Point(b.Left + covered + extra, baseCell.Y + i);
                        if (w >= 0 && w < width) v.Water(cell);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// A straight land causeway between two aligned points (same block row or column):
        /// a one-block strip of land shored on both sides, punched through any island ring
        /// it meets. The from/to cells are block-aligned top-left corners.
        /// </summary>
        public static bool PlaceCauseway(Map map, Point from, Point to, DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
        {
            Vocabulary v = Resolve(map, catalog, random);
            if (v == null) return false;
            void ClearBlock(Point cell)
            {
                for (int y = 0; y < Block; y++)
                    for (int x = 0; x < Block; x++)
                        v.Clear(new Point(cell.X + x, cell.Y + y));
            }
            void Shore(List<TemplateType> family, Point cell)
            {
                ClearBlock(cell);
                v.Stamp(v.Pick(family), cell);
            }
            if (from.Y == to.Y)
            {
                int step = Math.Sign(to.X - from.X) * Block;
                if (step == 0) return false;
                for (int x = from.X; Math.Sign(to.X + step - x) == Math.Sign(step) && x != to.X + step || x == from.X; x += step)
                {
                    ClearBlock(new Point(x, from.Y));
                    Shore(v.North, new Point(x, from.Y - Block));
                    Shore(v.South, new Point(x, from.Y + Block));
                }
                return true;
            }
            if (from.X == to.X)
            {
                int step = Math.Sign(to.Y - from.Y) * Block;
                if (step == 0) return false;
                for (int y = from.Y; Math.Sign(to.Y + step - y) == Math.Sign(step) && y != to.Y + step || y == from.Y; y += step)
                {
                    ClearBlock(new Point(from.X, y));
                    Shore(v.West, new Point(from.X - Block, y));
                    Shore(v.East, new Point(from.X + Block, y));
                }
                return true;
            }
            return false;
        }

        /// <summary>Erases one 3x3 block to clear land — the doorway a causeway punches through an island ring.</summary>
        public static void PunchBlock(Map map, Point cell)
        {
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
            for (int y = 0; y < Block; y++)
                for (int x = 0; x < Block; x++)
                    TemplateEdit.Erase(map.Templates, null, null, new Point(cell.X + x, cell.Y + y), undo, redo);
        }

        /// <summary>The land type the map's template grid gives a cell (clear when empty).</summary>
        public static LandType LandAt(Map map, Point cell)
        {
            Template template = map.Templates[cell.Y, cell.X];
            if (template?.Type == null) return LandType.Clear;
            return template.Type.GetLandType(template.Icon);
        }
    }
}
