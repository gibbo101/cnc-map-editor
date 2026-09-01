using System;
using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// The random map generator fills a fresh skirmish map deterministically from a seed:
    /// spaced player starts, noise-clumped tree cover, ore fields by each start and contested
    /// gem patches between them — all vanilla types, all placed through the same guarded
    /// operations as the brushes, and the result validates, saves and reloads.
    /// </summary>
    public class MapGeneratorTests
    {
        private static IGamePlugin Generate(MapGeneratorOptions options)
        {
            IGamePlugin plugin = EditorHost.Shared.New("Temperate", out _);
            MapGenerator.Generate(plugin, options);
            return plugin;
        }

        private static byte[] SaveBytes(IGamePlugin plugin, string name)
        {
            string outDir = TestPaths.Output("generated-maps");
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, name);
            plugin.Save(path, FileType.INI);
            return File.ReadAllBytes(path);
        }

        [Fact]
        public void SameSeedIsByteIdenticalAndSeedsDiffer()
        {
            MapGeneratorOptions options = new MapGeneratorOptions { Seed = 7, Players = 4 };
            byte[] first = SaveBytes(Generate(options), "seed7a.mpr");
            byte[] second = SaveBytes(Generate(options), "seed7b.mpr");
            Assert.Equal(first, second);
            byte[] other = SaveBytes(Generate(new MapGeneratorOptions { Seed = 8, Players = 4 }), "seed8.mpr");
            Assert.NotEqual(first, other);
        }

        [Fact]
        public void PlayerStartsAreAssignedSpacedAndInBounds()
        {
            IGamePlugin plugin = Generate(new MapGeneratorOptions { Seed = 3, Players = 6 });
            Map map = plugin.Map;
            var starts = map.Waypoints.Where(w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue).ToList();
            Assert.Equal(6, starts.Count);
            var points = starts.Select(w => { map.Metrics.GetLocation(w.Cell.Value, out System.Drawing.Point p); return p; }).ToList();
            foreach (System.Drawing.Point p in points)
            {
                Assert.True(map.Bounds.Contains(p), "start outside playable bounds: " + p);
            }
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double dist = Math.Sqrt(Math.Pow(points[i].X - points[j].X, 2) + Math.Pow(points[i].Y - points[j].Y, 2));
                    Assert.True(dist >= 15, $"starts {i} and {j} only {dist:0.0} cells apart");
                }
            }
        }

        [Fact]
        public void DensityDialsControlTreesAndOre()
        {
            IGamePlugin bare = Generate(new MapGeneratorOptions { Seed = 5, Players = 2, Trees = 0, Ore = 0 });
            Assert.Empty(bare.Map.Technos.Occupiers.OfType<Terrain>());
            Assert.DoesNotContain(bare.Map.Overlay, c => c.Value?.Type.IsResource == true);

            IGamePlugin sparse = Generate(new MapGeneratorOptions { Seed = 5, Players = 2, Trees = 0.2, Ore = 0.5 });
            IGamePlugin dense = Generate(new MapGeneratorOptions { Seed = 5, Players = 2, Trees = 0.9, Ore = 0.5 });
            int sparseTrees = sparse.Map.Technos.Occupiers.OfType<Terrain>().Count();
            int denseTrees = dense.Map.Technos.Occupiers.OfType<Terrain>().Count();
            Assert.True(sparseTrees > 0, "sparse setting placed no trees at all");
            Assert.True(denseTrees > sparseTrees, $"dense {denseTrees} should beat sparse {sparseTrees}");

            // Every start gets an ore field close by.
            Map map = dense.Map;
            var starts = map.Waypoints.Where(w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue).ToList();
            var orePoints = map.Overlay.Where(c => c.Value?.Type.IsResource == true)
                .Select(c => { map.Metrics.GetLocation(c.Cell, out System.Drawing.Point p); return p; }).ToList();
            Assert.NotEmpty(orePoints);
            foreach (Waypoint start in starts)
            {
                map.Metrics.GetLocation(start.Cell.Value, out System.Drawing.Point sp);
                Assert.Contains(orePoints, p => Math.Abs(p.X - sp.X) <= 10 && Math.Abs(p.Y - sp.Y) <= 10);
            }
        }

        [Fact]
        public void GeneratedMapUsesVanillaTypesValidatesSavesAndReloads()
        {
            IGamePlugin plugin = Generate(new MapGeneratorOptions { Seed = 11, Players = 4 });
            Assert.Null(plugin.Validate(FileType.INI, false, false));
            SaveBytes(plugin, "reload.mpr");
            string path = Path.Combine(TestPaths.Output("generated-maps"), "reload.mpr");
            IGamePlugin reloaded = EditorHost.Shared.Load(path, out string[] errors);
            Assert.Empty(errors);
            Assert.Equal(plugin.Map.Technos.Occupiers.OfType<Terrain>().Count(), reloaded.Map.Technos.Occupiers.OfType<Terrain>().Count());
            Assert.Equal(
                plugin.Map.Overlay.Count(c => c.Value?.Type.IsResource == true),
                reloaded.Map.Overlay.Count(c => c.Value?.Type.IsResource == true));
        }

        [Fact]
        public void LakesDrawRealShoresAndStartsStayOnLand()
        {
            IGamePlugin dry = Generate(new MapGeneratorOptions { Seed = 6, Players = 2, Water = 0 });
            Assert.DoesNotContain(EnumerateLand(dry.Map), t => t == LandType.Water);

            IGamePlugin wet = Generate(new MapGeneratorOptions { Seed = 6, Players = 4, Water = 1 });
            var lands = EnumerateLand(wet.Map).ToList();
            Assert.Contains(lands, t => t == LandType.Water);
            Assert.Contains(lands, t => t == LandType.Beach);
            foreach (Waypoint start in wet.Map.Waypoints.Where(w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue))
            {
                wet.Map.Metrics.GetLocation(start.Cell.Value, out System.Drawing.Point p);
                Assert.Equal(LandType.Clear, LakeBuilder.LandAt(wet.Map, p));
            }
        }

        private static System.Collections.Generic.IEnumerable<LandType> EnumerateLand(Map map)
        {
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                    yield return LakeBuilder.LandAt(map, new System.Drawing.Point(x, y));
        }

        [Fact]
        public void VillagesAndRoadsArePlacedWhenAsked()
        {
            IGamePlugin plugin = Generate(new MapGeneratorOptions { Seed = 5, Players = 4, Water = 0.4, Villages = 3 });
            Assert.NotEmpty(plugin.Map.Buildings.Occupiers.OfType<Building>()
                .Where(b => b.House.Name.Equals("Neutral", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(EnumerateLand(plugin.Map), t => t == LandType.Road);

            IGamePlugin bare = Generate(new MapGeneratorOptions { Seed = 5, Players = 4, Water = 0.4, Villages = 0 });
            Assert.Empty(bare.Map.Buildings.Occupiers.OfType<Building>());
        }

        [Fact]
        public void TiberiumDialSwitchesFieldsAndSpawnersViaTheModManifest()
        {
            IGamePlugin tib = Generate(new MapGeneratorOptions { Seed = 9, Players = 4, Tiberium = 1 });
            Assert.Contains(tib.Map.Overlay, c => c.Value?.Type.Name == "tib01");
            Assert.Contains(tib.Map.Buildings.Occupiers.OfType<Building>(), b => b.Type.Name == "tdblossom");

            IGamePlugin ore = Generate(new MapGeneratorOptions { Seed = 9, Players = 4, Tiberium = 0 });
            Assert.DoesNotContain(ore.Map.Overlay, c => c.Value?.Type.Name == "tib01");
            Assert.Contains(ore.Map.Technos.Occupiers.OfType<Terrain>(), t => t.Type.Name == "mine");
        }

        [Fact]
        public void WalkedOceanIsDeterministicAndSealed()
        {
            MapGeneratorOptions options = new MapGeneratorOptions
            { Seed = 44, Players = 4, Style = WaterStyle.Ocean, OceanEdge = 1 };
            byte[] first = SaveBytes(Generate(options), "ocean-a.mpr");
            byte[] second = SaveBytes(Generate(options), "ocean-b.mpr");
            Assert.Equal(first, second);

            IGamePlugin plugin = Generate(options);
            Map map = plugin.Map;
            int clearAgainstWater = 0;
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    // Authored pieces paint their own internal grass-to-water transitions;
                    // the defect is bare clear land against flood-filled open water.
                    Template fill = map.Templates[y, x];
                    if (fill?.Type == null || (fill.Type.Name != "w1" && fill.Type.Name != "w2")) continue;
                    foreach (System.Drawing.Point n in new[] { new System.Drawing.Point(x + 1, y), new System.Drawing.Point(x - 1, y), new System.Drawing.Point(x, y + 1), new System.Drawing.Point(x, y - 1) })
                    {
                        if (map.Bounds.Contains(n) && LakeBuilder.LandAt(map, n) == LandType.Clear) clearAgainstWater++;
                    }
                }
            }
            Assert.Equal(0, clearAgainstWater);
        }

        [Fact]
        public void LakesStyleWalksAClosedLoopCenterpiece()
        {
            MapGeneratorOptions options = new MapGeneratorOptions
            { Seed = 21, Players = 4, Style = WaterStyle.Lakes, Water = 1 };
            byte[] first = SaveBytes(Generate(options), "walked-lake-a.mpr");
            byte[] second = SaveBytes(Generate(options), "walked-lake-b.mpr");
            Assert.Equal(first, second);

            IGamePlugin plugin = Generate(options);
            Map map = plugin.Map;
            // The walked centerpiece is a single basin far bigger than any block pond
            // (which top out around 18x18 with shores taking part of that).
            Assert.True(LargestWaterBody(map) >= 300, "largest water body only " + LargestWaterBody(map) + " cells");
            var contacts = new System.Collections.Generic.List<string>();
            int clearAgainstWater = 0;
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    Template fill = map.Templates[y, x];
                    if (fill?.Type == null || (fill.Type.Name != "w1" && fill.Type.Name != "w2")) continue;
                    foreach (System.Drawing.Point n in new[] { new System.Drawing.Point(x + 1, y), new System.Drawing.Point(x - 1, y), new System.Drawing.Point(x, y + 1), new System.Drawing.Point(x, y - 1) })
                    {
                        if (map.Bounds.Contains(n) && LakeBuilder.LandAt(map, n) == LandType.Clear)
                        {
                            clearAgainstWater++;
                            contacts.Add($"{fill.Type.Name}@{x},{y} vs {map.Templates[n.Y, n.X]?.Type?.Name ?? "null"}@{n.X},{n.Y}");
                        }
                    }
                }
            }
            Assert.True(clearAgainstWater == 0, string.Join("; ", contacts));
        }

        private static int LargestWaterBody(Map map)
        {
            bool IsWater(System.Drawing.Point p) => map.Bounds.Contains(p)
                && LakeBuilder.LandAt(map, p) is LandType land && (land == LandType.Water || land == LandType.River);
            var seen = new System.Collections.Generic.HashSet<System.Drawing.Point>();
            int best = 0;
            for (int y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
            {
                for (int x = map.Bounds.Left; x < map.Bounds.Right; x++)
                {
                    System.Drawing.Point start = new System.Drawing.Point(x, y);
                    if (!IsWater(start) || !seen.Add(start)) continue;
                    int size = 0;
                    var frontier = new System.Collections.Generic.Queue<System.Drawing.Point>();
                    frontier.Enqueue(start);
                    while (frontier.Count > 0)
                    {
                        System.Drawing.Point p = frontier.Dequeue();
                        size++;
                        foreach (System.Drawing.Point n in new[] { new System.Drawing.Point(p.X + 1, p.Y), new System.Drawing.Point(p.X - 1, p.Y), new System.Drawing.Point(p.X, p.Y + 1), new System.Drawing.Point(p.X, p.Y - 1) })
                        {
                            if (IsWater(n) && seen.Add(n)) frontier.Enqueue(n);
                        }
                    }
                    best = Math.Max(best, size);
                }
            }
            return best;
        }

        [Fact]
        public void TiberianDawnMapsGenerateToo()
        {
            IGamePlugin plugin = EditorHost.SharedFor(GameType.TiberianDawn).New(null, out _);
            MapGenerator.Generate(plugin, new MapGeneratorOptions { Seed = 4, Players = 4 });
            Assert.Null(plugin.Validate(FileType.INI, false, false));
            Assert.NotEmpty(plugin.Map.Technos.Occupiers.OfType<Terrain>());
            Assert.Contains(plugin.Map.Overlay, c => c.Value?.Type.IsResource == true);
            Assert.Equal(4, plugin.Map.Waypoints.Count(w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue));
        }

        [Fact]
        public void PlayerCountIsClampedToTheGamesStartSlots()
        {
            IGamePlugin plugin = Generate(new MapGeneratorOptions { Seed = 2, Players = 99 });
            int slots = plugin.Map.Waypoints.Count(w => w.Flags.HasFlag(WaypointFlag.PlayerStart));
            Assert.Equal(slots, plugin.Map.Waypoints.Count(w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue));
        }
    }
}
