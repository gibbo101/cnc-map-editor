using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// Unit and infantry placement on the document. Units go through the occupier set with
    /// the fork's defaults (placement house, full strength, north facing, the type's default
    /// mission); infantry share a cell as an InfantryGroup of five stops, filling one free
    /// stop per placement — the group appears with the first man and leaves with the last.
    /// Every placement or removal is one undo step.
    /// </summary>
    public class UnitInfantryToolTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        [Fact]
        public void UnitPlacementUsesThePlacementHouseAndDefaults()
        {
            using (MapDocument doc = Open())
            {
                doc.PlacementHouse = doc.Map.HouseTypes.First(h => h.Name == "USSR");
                UnitType type = doc.AvailableUnits().First(t => t.IsGroundUnit);
                Unit unit = doc.PlaceUnit(new Point(20, 20), type);
                Assert.NotNull(unit);
                Assert.Equal("USSR", unit.House.Name);
                Assert.Equal(256, unit.Strength);
                Assert.Equal(doc.Map.GetDefaultMission(type), unit.Mission);
                Assert.Same(unit, doc.Map.Technos[new Point(20, 20)]);
                Assert.Null(doc.PlaceUnit(new Point(20, 20), type));
                doc.EraseUnitAt(new Point(20, 20));
                Assert.Null(doc.Map.Technos[new Point(20, 20)]);
                doc.Undo();
                Assert.Same(unit, doc.Map.Technos[new Point(20, 20)]);
                doc.Undo();
                Assert.Null(doc.Map.Technos[new Point(20, 20)]);
                Assert.False(doc.CanUndo);
            }
        }

        [Fact]
        public void InfantryFillOneStopPerPlacementAndTheGroupLeavesWithTheLastMan()
        {
            using (MapDocument doc = Open())
            {
                InfantryType type = doc.AvailableInfantry().First();
                Point at = new Point(22, 22);
                for (int i = 0; i < 5; i++)
                {
                    Assert.NotNull(doc.PlaceInfantry(at, type));
                }
                InfantryGroup group = Assert.IsType<InfantryGroup>(doc.Map.Technos[at]);
                Assert.Equal(5, group.Infantry.Count(x => x != null));
                Assert.Null(doc.PlaceInfantry(at, type));

                for (int i = 0; i < 5; i++)
                {
                    doc.EraseInfantryAt(at);
                }
                Assert.Null(doc.Map.Technos[at]);
                doc.Undo();
                InfantryGroup restored = Assert.IsType<InfantryGroup>(doc.Map.Technos[at]);
                Assert.Equal(1, restored.Infantry.Count(x => x != null));
                doc.Redo();
                Assert.Null(doc.Map.Technos[at]);
            }
        }

        [Fact]
        public void FirstPlacedInfantrymanIsOneUndoStepIncludingItsGroup()
        {
            using (MapDocument doc = Open())
            {
                InfantryType type = doc.AvailableInfantry().First();
                Point at = new Point(24, 24);
                Infantry man = doc.PlaceInfantry(at, type);
                Assert.NotNull(man);
                doc.Undo();
                Assert.Null(doc.Map.Technos[at]);
                doc.Redo();
                InfantryGroup group = Assert.IsType<InfantryGroup>(doc.Map.Technos[at]);
                Assert.Same(man, group.Infantry.Single(x => x != null));
            }
        }

        [Fact]
        public void SmudgeStrokeIsUndoableAndKeepsIncrementalRenderIdentical()
        {
            using (MapDocument doc = Open())
            {
                doc.Scale = 1.0 / 16;
                using (doc.Render()) { }
                SmudgeType crater = doc.AvailableSmudge().First(t => !t.IsMultiCell);
                doc.PlaceSmudge(new Point(26, 26), crater);
                Assert.Same(crater, doc.Map.Smudge[new Point(26, 26)].Type);
                doc.Undo();
                Assert.Null(doc.Map.Smudge[new Point(26, 26)]);
                doc.Redo();
                doc.EraseSmudge(new Point(26, 26), crater);
                Assert.Null(doc.Map.Smudge[new Point(26, 26)]);
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

        [Fact]
        public void ObjectEditsKeepIncrementalRenderIdentical()
        {
            using (MapDocument doc = Open())
            {
                doc.Scale = 1.0 / 16;
                using (doc.Render()) { }
                doc.PlaceUnit(new Point(18, 18), doc.AvailableUnits().First(t => t.IsGroundUnit));
                doc.PlaceInfantry(new Point(19, 18), doc.AvailableInfantry().First());
                doc.PlaceInfantry(new Point(19, 18), doc.AvailableInfantry().First());
                doc.EraseUnitAt(new Point(18, 18));
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
