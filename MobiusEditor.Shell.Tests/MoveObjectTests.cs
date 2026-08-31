using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// Moving placed objects: one undo step per move, footprints validate at the target (a
    /// blocked move leaves everything untouched, as the fork rolls back), buildings carry
    /// their bib and give back any hand-placed smudge the new bib ate, and an infantryman
    /// moves cell-to-cell into the stop closest to the pointer — the source group retires
    /// with its last man.
    /// </summary>
    public class MoveObjectTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        [Fact]
        public void MoveUnitRelocatesAsOneUndoStep()
        {
            using (MapDocument doc = Open())
            {
                Unit unit = doc.PlaceUnit(new Point(20, 20), doc.AvailableUnits().First(t => t.IsGroundUnit));
                Assert.True(doc.MoveObject(unit, new Point(24, 24)));
                Assert.Null(doc.Map.Technos[new Point(20, 20)]);
                Assert.Same(unit, doc.Map.Technos[new Point(24, 24)]);
                doc.Undo();
                Assert.Same(unit, doc.Map.Technos[new Point(20, 20)]);
                Assert.Null(doc.Map.Technos[new Point(24, 24)]);
                doc.Redo();
                Assert.Same(unit, doc.Map.Technos[new Point(24, 24)]);
            }
        }

        [Fact]
        public void BlockedMoveLeavesEverythingInPlaceAndAddsNoUndoStep()
        {
            using (MapDocument doc = Open())
            {
                UnitType type = doc.AvailableUnits().First(t => t.IsGroundUnit);
                Unit a = doc.PlaceUnit(new Point(20, 20), type);
                Unit b = doc.PlaceUnit(new Point(24, 24), type);
                Assert.False(doc.MoveObject(a, new Point(24, 24)));
                Assert.Same(a, doc.Map.Technos[new Point(20, 20)]);
                Assert.Same(b, doc.Map.Technos[new Point(24, 24)]);
                doc.Undo();
                // The undo undid the second PLACEMENT, not a move.
                Assert.Null(doc.Map.Technos[new Point(24, 24)]);
                Assert.Same(a, doc.Map.Technos[new Point(20, 20)]);
            }
        }

        [Fact]
        public void MovedBuildingCarriesItsBibAndGivesBackEatenSmudge()
        {
            using (MapDocument doc = Open())
            {
                BuildingType type = doc.AvailableBuildings().First(b => b.HasBib);
                Building building = doc.PlaceBuilding(new Point(20, 20), type);
                Point target = new Point(40, 40);
                Point eatenCell = building.GetBib(target, doc.Map.SmudgeTypes).Keys.First();
                SmudgeType crater = doc.AvailableSmudge().First(s => !s.IsMultiCell);
                doc.PlaceSmudge(eatenCell, crater);

                Assert.True(doc.MoveObject(building, target));
                Assert.Same(building, doc.Map.Buildings[target]);
                Assert.True(doc.Map.Smudge[eatenCell]?.IsAutoBib, "new bib expected over the crater");
                foreach (Point p in building.GetBib(new Point(20, 20), doc.Map.SmudgeTypes).Keys)
                {
                    Assert.Null(doc.Map.Smudge[p]);
                }
                doc.Undo();
                Assert.Same(building, doc.Map.Buildings[new Point(20, 20)]);
                Assert.Same(crater, doc.Map.Smudge[eatenCell]?.Type);
                doc.Redo();
                Assert.Same(building, doc.Map.Buildings[target]);
            }
        }

        [Fact]
        public void InfantrymanMovesIntoTheClosestStopAndTheEmptySourceGroupRetires()
        {
            using (MapDocument doc = Open())
            {
                InfantryType type = doc.AvailableInfantry().First();
                Infantry stays = doc.PlaceInfantry(new Point(20, 20), type);
                Infantry moves = doc.PlaceInfantry(new Point(20, 20), type);
                Point target = new Point(24, 24);
                Assert.True(doc.MoveObject(moves, target, new Point(3, 3)));
                InfantryGroup targetGroup = Assert.IsType<InfantryGroup>(doc.Map.Technos[target]);
                Assert.Same(moves, targetGroup.Infantry[(int)InfantryStoppingType.UpperLeft]);
                InfantryGroup sourceGroup = Assert.IsType<InfantryGroup>(doc.Map.Technos[new Point(20, 20)]);
                Assert.Same(stays, sourceGroup.Infantry.Single(i => i != null));

                Assert.True(doc.MoveObject(stays, target, new Point(21, 21)));
                Assert.Null(doc.Map.Technos[new Point(20, 20)]);
                Assert.Same(stays, targetGroup.Infantry[(int)InfantryStoppingType.LowerRight]);
                doc.Undo();
                Assert.NotNull(doc.Map.Technos[new Point(20, 20)]);
                Assert.Null(targetGroup.Infantry[(int)InfantryStoppingType.LowerRight]);
            }
        }

        [Fact]
        public void MovesKeepIncrementalRenderIdentical()
        {
            using (MapDocument doc = Open())
            {
                doc.Scale = 1.0 / 16;
                Unit unit = doc.PlaceUnit(new Point(20, 20), doc.AvailableUnits().First(t => t.IsGroundUnit));
                Building building = doc.PlaceBuilding(new Point(30, 30), doc.AvailableBuildings().First(b => b.HasBib));
                using (doc.Render()) { }
                doc.MoveObject(unit, new Point(22, 22));
                doc.MoveObject(building, new Point(40, 40));
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
