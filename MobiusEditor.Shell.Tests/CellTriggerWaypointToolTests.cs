using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;
using RA = MobiusEditor.RedAlert;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// The cell-trigger and waypoint tools on the document, porting the fork's tool
    /// semantics: a cell trigger places only into an empty cell and only for a trigger
    /// whose event can actually fire from a cell; placing a waypoint moves its flag (one
    /// cell per waypoint); erasing a waypoint at a cell clears the first one found there.
    /// Cell trigger strokes batch like brush strokes; waypoint moves are one undo step each.
    /// </summary>
    public class CellTriggerWaypointToolTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        private static void AddCellTrigger(MapDocument doc, string name)
        {
            doc.EditTriggers(ed =>
            {
                Trigger t = ed.Add();
                ed.TryRename(t, name);
                t.Event1.EventType = RA.EventTypes.TEVENT_PLAYER_ENTERED;
            });
        }

        [Fact]
        public void CellTriggerPlacesOnlyEligibleTriggersIntoEmptyCells()
        {
            using (MapDocument doc = Open())
            {
                Assert.DoesNotContain("ghost", doc.AvailableCellTriggers());
                doc.PlaceCellTrigger(new Point(10, 10), "ghost");
                Assert.Null(doc.Map.CellTriggers[new Point(10, 10)]);

                AddCellTrigger(doc, "celx");
                AddCellTrigger(doc, "othr");
                Assert.Contains("celx", doc.AvailableCellTriggers());
                doc.PlaceCellTrigger(new Point(10, 10), "celx");
                Assert.Equal("celx", doc.Map.CellTriggers[new Point(10, 10)].Trigger);

                // An occupied cell is never overwritten.
                doc.PlaceCellTrigger(new Point(10, 10), "othr");
                Assert.Equal("celx", doc.Map.CellTriggers[new Point(10, 10)].Trigger);

                doc.EraseCellTrigger(new Point(10, 10));
                Assert.Null(doc.Map.CellTriggers[new Point(10, 10)]);
                doc.Undo();
                Assert.Equal("celx", doc.Map.CellTriggers[new Point(10, 10)].Trigger);
                doc.Undo();
                Assert.Null(doc.Map.CellTriggers[new Point(10, 10)]);
            }
        }

        [Fact]
        public void CellTriggerStrokesBatchIntoOneUndoStep()
        {
            using (MapDocument doc = Open())
            {
                AddCellTrigger(doc, "celx");
                doc.BeginStroke();
                doc.PlaceCellTrigger(new Point(5, 5), "celx");
                doc.PlaceCellTrigger(new Point(6, 5), "celx");
                doc.EndStroke();
                Assert.NotNull(doc.Map.CellTriggers[new Point(5, 5)]);
                Assert.NotNull(doc.Map.CellTriggers[new Point(6, 5)]);
                doc.Undo();
                Assert.Null(doc.Map.CellTriggers[new Point(5, 5)]);
                Assert.Null(doc.Map.CellTriggers[new Point(6, 5)]);
            }
        }

        [Fact]
        public void WaypointPlacementMovesTheFlagAndIsUndoable()
        {
            using (MapDocument doc = Open())
            {
                int index = System.Array.FindIndex(doc.Map.Waypoints, w => !w.Cell.HasValue);
                doc.PlaceWaypoint(index, new Point(5, 5));
                doc.Map.Metrics.GetCell(new Point(5, 5), out int firstCell);
                Assert.Equal(firstCell, doc.Map.Waypoints[index].Cell);
                doc.PlaceWaypoint(index, new Point(7, 7));
                doc.Map.Metrics.GetCell(new Point(7, 7), out int secondCell);
                Assert.Equal(secondCell, doc.Map.Waypoints[index].Cell);
                doc.Undo();
                Assert.Equal(firstCell, doc.Map.Waypoints[index].Cell);
                doc.Undo();
                Assert.False(doc.Map.Waypoints[index].Cell.HasValue);
                doc.Redo();
                Assert.Equal(firstCell, doc.Map.Waypoints[index].Cell);
            }
        }

        [Fact]
        public void EraseWaypointAtClearsTheFirstFlagOnTheCell()
        {
            using (MapDocument doc = Open())
            {
                int index = System.Array.FindIndex(doc.Map.Waypoints, w => !w.Cell.HasValue);
                doc.PlaceWaypoint(index, new Point(9, 9));
                doc.EraseWaypointAt(new Point(9, 9));
                Assert.False(doc.Map.Waypoints[index].Cell.HasValue);
                doc.Undo();
                Assert.True(doc.Map.Waypoints[index].Cell.HasValue);
            }
        }

        [Fact]
        public void ToolEditsKeepIncrementalRenderIdentical()
        {
            using (MapDocument doc = Open())
            {
                doc.Scale = 1.0 / 16;
                doc.Layers |= MapLayerFlag.CellTriggers;
                AddCellTrigger(doc, "celx");
                using (doc.Render()) { }
                doc.PlaceCellTrigger(new Point(12, 12), "celx");
                int index = System.Array.FindIndex(doc.Map.Waypoints, w => !w.Cell.HasValue);
                doc.PlaceWaypoint(index, new Point(14, 14));
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
