using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// The terrain-object tool (trees, rocks): placement goes through the occupier set, so
    /// footprints validate against everything already on the map; each placement or removal
    /// is one undo step; erasing works from any occupied cell of the object. Incremental
    /// rendering stays pixel-identical, with the object's full overlap marked dirty.
    /// </summary>
    public class TerrainToolTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        /// <summary>First cell the type's occupy mask actually claims, for a placement at origin.</summary>
        private static Point OccupiedCell(TerrainType type, Point origin)
        {
            bool[,] mask = type.OccupyMask;
            for (int y = 0; y < mask.GetLength(0); y++)
            {
                for (int x = 0; x < mask.GetLength(1); x++)
                {
                    if (mask[y, x]) return new Point(origin.X + x, origin.Y + y);
                }
            }
            return origin;
        }

        [Fact]
        public void PlacementOccupiesAndRefusesCollisions()
        {
            using (MapDocument doc = Open())
            {
                TerrainType tree = doc.AvailableTerrain().First();
                Terrain placed = doc.PlaceTerrain(new Point(20, 20), tree);
                Assert.NotNull(placed);
                Assert.Same(tree, placed.Type);
                Point occupied = OccupiedCell(tree, new Point(20, 20));
                Assert.Same(placed, doc.Map.Technos[occupied]);
                // A second placement on the same footprint is refused and adds no undo step.
                Assert.Null(doc.PlaceTerrain(new Point(20, 20), tree));
                doc.Undo();
                Assert.Null(doc.Map.Technos[occupied]);
                Assert.False(doc.CanUndo);
                doc.Redo();
                Assert.Same(placed, doc.Map.Technos[occupied]);
            }
        }

        [Fact]
        public void EraseWorksFromAnyOccupiedCellAndIsUndoable()
        {
            using (MapDocument doc = Open())
            {
                TerrainType type = doc.AvailableTerrain().First();
                Terrain placed = doc.PlaceTerrain(new Point(30, 30), type);
                Assert.NotNull(placed);
                Point occupied = OccupiedCell(type, new Point(30, 30));
                doc.EraseTerrainAt(occupied);
                Assert.Null(doc.Map.Technos[occupied]);
                doc.Undo();
                Assert.Same(placed, doc.Map.Technos[occupied]);
            }
        }

        [Fact]
        public void TerrainEditsKeepIncrementalRenderIdentical()
        {
            using (MapDocument doc = Open())
            {
                doc.Scale = 1.0 / 16;
                using (doc.Render()) { }
                TerrainType tree = doc.AvailableTerrain().First();
                doc.PlaceTerrain(new Point(22, 22), tree);
                doc.PlaceTerrain(new Point(26, 22), tree);
                doc.EraseTerrainAt(new Point(26, 22));
                using (Bitmap incremental = doc.Render())
                {
                    double original = doc.Scale;
                    doc.Scale = original * 2;
                    using (doc.Render()) { }
                    doc.Scale = original;
                    using (Bitmap full = doc.Render())
                    {
                        ImageDiff diff = ImageCompare.Compare(incremental, full);
                        Assert.True(diff.Differing == 0, "incremental render differs from full render: " + diff);
                    }
                }
            }
        }
    }
}
