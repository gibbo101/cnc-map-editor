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
    }

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
                    if (!PlaceWalkedOcean(map, edge, depth, random, catalog))
                    {
                        LakeBuilder.PlaceOcean(map, edge, depth, random, catalog);
                    }
                    break;
                case WaterStyle.River:
                    LakeBuilder.PlaceRiver(map, random.Next(2) == 0,
                        options.RiverWidth ?? 1 + (int)Math.Round(options.Water),
                        options.Fords ?? 1 + (int)Math.Round((1 - options.Water) * 2), random, catalog);
                    break;
                case WaterStyle.Islands:
                    PlaceIslands(map, options, random, catalog);
                    break;
                default:
                    PlaceScatteredLakes(map, options, random, catalog);
                    break;
            }
        }

        /// <summary>
        /// An ocean with a corpus-walked coastline: anchors jittered along the coast band,
        /// A* segments chained through mined idioms, both ends flush with the map edges,
        /// the sea flooded behind it. Plans everything before placing anything; false when
        /// planning fails and the caller should fall back.
        /// </summary>
        private static bool PlaceWalkedOcean(Map map, int edge, int depthBlocks,
            DeterministicRandom random, IReadOnlyList<ShorePiece> shoreCatalog)
        {
            TransitionGraph graph = TransitionGraph.Baked;
            Dictionary<string, ShorePiece> catalog = ShoreCatalog.Build(map.TemplateTypes)
                .Concat(ShoreCatalog.Build(map.TemplateTypes, "wc"))
                .GroupBy(p => p.Template.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            int desiredSide = edge switch { 0 => 0, 1 => 4, 2 => 2, _ => 6 };
            string start = CoastWalker.BestStartPiece(map, graph, catalog, desiredSide);
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
                    piece, origin, At(along, across), 70, desiredSide);
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
            CoastWalker.PatchBareContacts(map, random);
            CoastWalker.ExtendWaterIntoBorder(map, random);
            return true;
        }

        /// <summary>Lakes scaled by the water dial, spaced apart inside the playable bounds.</summary>
        private static void PlaceScatteredLakes(Map map, MapGeneratorOptions options, DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
        {
            List<Rectangle> placed = new List<Rectangle>();
            int lakeCount = options.Lakes ?? 1 + (int)Math.Round(options.Water * 2);
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
        /// An island world: the bounds flooded with water, then same-sized islands carved in
        /// a ring — a top band and a bottom band — and, when enabled, neighbouring islands
        /// joined by straight land causeways so ground forces can advance without transports.
        /// Starts land on the islands by the buildable-ground scoring.
        /// </summary>
        private static void PlaceIslands(Map map, MapGeneratorOptions options, DeterministicRandom random, IReadOnlyList<ShorePiece> catalog)
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
                bool tiberiumField = tiberiumOverlay != null && random.Next(1000) < options.Tiberium * 1000;
                PlaceSpawner(map, tiberiumField ? tiberium : null, center, random);
                PlacePatch(map, tiberiumField ? tiberiumOverlay : ore, center, radius);
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

        private static void PlacePatch(Map map, OverlayType resource, Point center, int radius)
        {
            Dictionary<int, Overlay> undo = new Dictionary<int, Overlay>(), redo = new Dictionary<int, Overlay>();
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    Point p = new Point(center.X + dx, center.Y + dy);
                    if (!map.Bounds.Contains(p) || map.Technos[p] != null || map.Buildings[p] != null || !IsClearGround(map, p)) continue;
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
                    if (!IsClearGround(map, p)) continue;
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
