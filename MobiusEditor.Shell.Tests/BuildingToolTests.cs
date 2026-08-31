using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// Building placement on the document: the building set validates the footprint and the
    /// map auto-manages the bib smudge under it; a hand-placed smudge the bib covers comes
    /// back on undo, exactly as the fork restores it. Defaults are the fork's mock building
    /// (placement house, full strength, prebuilt, no base priority). One undo step each way.
    /// </summary>
    public class BuildingToolTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        [Fact]
        public void PlacementAddsTheBibAndRefusesCollisions()
        {
            using (MapDocument doc = Open())
            {
                doc.PlacementHouse = doc.Map.HouseTypes.First(h => h.Name == "USSR");
                BuildingType type = doc.AvailableBuildings().First(b => b.HasBib);
                Building building = doc.PlaceBuilding(new Point(20, 20), type);
                Assert.NotNull(building);
                Assert.Equal("USSR", building.House.Name);
                Assert.True(building.IsPrebuilt);
                Dictionary<Point, Smudge> bib = building.GetBib(new Point(20, 20), doc.Map.SmudgeTypes);
                Assert.NotEmpty(bib);
                foreach (Point p in bib.Keys)
                {
                    Assert.True(doc.Map.Smudge[p]?.IsAutoBib, "expected auto-bib at " + p);
                }
                Assert.Null(doc.PlaceBuilding(new Point(20, 20), type));
                doc.Undo();
                Assert.Null(doc.Map.Buildings[building]);
                foreach (Point p in bib.Keys)
                {
                    Assert.Null(doc.Map.Smudge[p]);
                }
                doc.Redo();
                Assert.NotNull(doc.Map.Buildings[building]);
            }
        }

        [Fact]
        public void EraseRestoresOnUndoIncludingTheBib()
        {
            using (MapDocument doc = Open())
            {
                BuildingType type = doc.AvailableBuildings().First(b => b.HasBib);
                Building building = doc.PlaceBuilding(new Point(30, 30), type);
                Assert.NotNull(building);
                Point occupied = doc.Map.Buildings[building].Value;
                doc.EraseBuildingAt(occupied);
                Assert.Null(doc.Map.Buildings[building]);
                doc.Undo();
                Assert.NotNull(doc.Map.Buildings[building]);
                Dictionary<Point, Smudge> bib = building.GetBib(new Point(30, 30), doc.Map.SmudgeTypes);
                foreach (Point p in bib.Keys)
                {
                    Assert.True(doc.Map.Smudge[p]?.IsAutoBib, "expected auto-bib restored at " + p);
                }
            }
        }

        [Fact]
        public void BuildingEditsKeepIncrementalRenderIdentical()
        {
            using (MapDocument doc = Open())
            {
                doc.Scale = 1.0 / 16;
                using (doc.Render()) { }
                BuildingType type = doc.AvailableBuildings().First(b => b.HasBib);
                doc.PlaceBuilding(new Point(22, 22), type);
                doc.PlaceBuilding(new Point(30, 30), type);
                doc.EraseBuildingAt(new Point(30, 30));
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
