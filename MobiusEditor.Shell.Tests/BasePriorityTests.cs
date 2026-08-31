using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// The AI rebuild base keeps consecutive build priorities, as the fork enforces: editing
    /// a building's priority renumbers every base building 0..n-1 with the edited one at its
    /// requested (clamped) slot, and removing a base building closes the gap. Undo restores
    /// every affected building's priority, not just the edited one's.
    /// </summary>
    public class BasePriorityTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        private static (MapDocument doc, Building a, Building b, Building c) ThreeBaseBuildings()
        {
            MapDocument doc = Open();
            BuildingType type = doc.AvailableBuildings().First(t => !t.HasTurret);
            Building a = doc.PlaceBuilding(new Point(20, 20), type);
            Building b = doc.PlaceBuilding(new Point(30, 30), type);
            Building c = doc.PlaceBuilding(new Point(40, 40), type);
            doc.EditObjectProperties(a, () => a.BasePriority = 0);
            doc.EditObjectProperties(b, () => b.BasePriority = 1);
            doc.EditObjectProperties(c, () => c.BasePriority = 2);
            return (doc, a, b, c);
        }

        [Fact]
        public void EditingAPriorityRenumbersTheWholeBaseAndUndoRestoresEveryone()
        {
            (MapDocument doc, Building a, Building b, Building c) = ThreeBaseBuildings();
            Assert.Equal((0, 1, 2), (a.BasePriority, b.BasePriority, c.BasePriority));
            // Move c to the front: everyone shifts.
            doc.EditObjectProperties(c, () => c.BasePriority = 0);
            Assert.Equal((1, 2, 0), (a.BasePriority, b.BasePriority, c.BasePriority));
            doc.Undo();
            Assert.Equal((0, 1, 2), (a.BasePriority, b.BasePriority, c.BasePriority));
            doc.Redo();
            Assert.Equal((1, 2, 0), (a.BasePriority, b.BasePriority, c.BasePriority));
            doc.Dispose();
        }

        [Fact]
        public void RequestedPriorityIsClampedIntoTheBase()
        {
            (MapDocument doc, Building a, Building b, Building c) = ThreeBaseBuildings();
            doc.EditObjectProperties(a, () => a.BasePriority = 99);
            Assert.Equal((2, 0, 1), (a.BasePriority, b.BasePriority, c.BasePriority));
            doc.Dispose();
        }

        [Fact]
        public void RemovingABaseBuildingClosesTheGapAndUndoReopensIt()
        {
            (MapDocument doc, Building a, Building b, Building c) = ThreeBaseBuildings();
            doc.EraseBuildingAt(doc.Map.Buildings[b].Value);
            Assert.Equal(0, a.BasePriority);
            Assert.Equal(1, c.BasePriority);
            doc.Undo();
            Assert.NotNull(doc.Map.Buildings[b]);
            Assert.Equal((0, 1, 2), (a.BasePriority, b.BasePriority, c.BasePriority));
            doc.Redo();
            Assert.Null(doc.Map.Buildings[b]);
            Assert.Equal((0, 1), (a.BasePriority, c.BasePriority));
            doc.Dispose();
        }

        [Fact]
        public void LeavingTheBaseCompactsTheRestAndForcesPrebuilt()
        {
            (MapDocument doc, Building a, Building b, Building c) = ThreeBaseBuildings();
            doc.EditObjectProperties(b, () => b.BasePriority = -1);
            Assert.Equal(-1, b.BasePriority);
            Assert.True(b.IsPrebuilt);
            Assert.Equal(0, a.BasePriority);
            Assert.Equal(1, c.BasePriority);
            doc.Undo();
            Assert.Equal((0, 1, 2), (a.BasePriority, b.BasePriority, c.BasePriority));
            doc.Dispose();
        }
    }
}
