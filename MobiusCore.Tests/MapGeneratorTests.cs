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
