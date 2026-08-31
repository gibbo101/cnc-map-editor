using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// SmudgeEdit ports the fork's smudge tool semantics: single-cell craters/scorches and
    /// multi-cell bib smudges place per-cell with running icons, building-attached bibs are
    /// never overwritten, a placement fully replaces a loose smudge based on the same origin,
    /// erasing removes the whole smudge from any of its cells and re-heals cells that nearby
    /// multi-cell smudges should still cover. Callers accumulate a stroke's before/after
    /// values in the undo/redo dictionaries, first snapshot per cell wins.
    /// </summary>
    public class SmudgeEditTests
    {
        private static IGamePlugin Load() =>
            EditorHost.Shared.Load(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), out _);

        private static (Dictionary<Point, Smudge> undo, Dictionary<Point, Smudge> redo) Journals() =>
            (new Dictionary<Point, Smudge>(), new Dictionary<Point, Smudge>());

        [Fact]
        public void SingleCellSmudgePlacesAndErases()
        {
            IGamePlugin plugin = Load();
            Map map = plugin.Map;
            SmudgeType crater = map.SmudgeTypes.First(t => !t.IsMultiCell && t.ExistsInTheater);
            (Dictionary<Point, Smudge> undo, Dictionary<Point, Smudge> redo) = Journals();
            Point at = new Point(50, 50);
            SmudgeEdit.Place(map, crater, at, undo, redo);
            Assert.Same(crater, map.Smudge[at].Type);
            Assert.Null(undo[at]);
            Assert.Same(map.Smudge[at], redo[at]);

            (undo, redo) = Journals();
            SmudgeEdit.Erase(map, crater, at, undo, redo);
            Assert.Null(map.Smudge[at]);
            Assert.Null(redo[at]);
        }

        [Fact]
        public void MultiCellBibPlacesRunningIconsAndErasesWholeFromAnyCell()
        {
            IGamePlugin plugin = Load();
            Map map = plugin.Map;
            SmudgeType bib = map.SmudgeTypes.First(t => t.IsMultiCell && t.ExistsInTheater);
            (Dictionary<Point, Smudge> undo, Dictionary<Point, Smudge> redo) = Journals();
            Point at = new Point(52, 52);
            SmudgeEdit.Place(map, bib, at, undo, redo);
            int icon = 0;
            for (int y = 0; y < bib.Size.Height; y++)
            {
                for (int x = 0; x < bib.Size.Width; x++)
                {
                    Smudge cell = map.Smudge[new Point(at.X + x, at.Y + y)];
                    Assert.Same(bib, cell.Type);
                    Assert.Equal(icon++, cell.Icon);
                }
            }

            (undo, redo) = Journals();
            Point middle = new Point(at.X + bib.Size.Width - 1, at.Y + bib.Size.Height - 1);
            SmudgeEdit.Erase(map, bib, middle, undo, redo);
            for (int y = 0; y < bib.Size.Height; y++)
            {
                for (int x = 0; x < bib.Size.Width; x++)
                {
                    Assert.Null(map.Smudge[new Point(at.X + x, at.Y + y)]);
                }
            }
        }

        [Fact]
        public void BuildingAttachedBibsAreNeverTouched()
        {
            IGamePlugin plugin = Load();
            Map map = plugin.Map;
            BuildingType withBib = map.BuildingTypes.First(b => b.HasBib && b.ExistsInTheater);
            Building building = new Building
            {
                Type = withBib,
                House = map.HouseTypes.First(),
                Strength = 256,
                Direction = map.BuildingDirectionTypes.First(),
            };
            Point at = new Point(60, 60);
            Assert.True(map.Buildings.Add(at, building));
            try
            {
                Point bibCell = building.GetBib(at, map.SmudgeTypes).Keys.First();
                Smudge autoBib = map.Smudge[bibCell];
                Assert.True(autoBib.IsAutoBib);
                SmudgeType crater = map.SmudgeTypes.First(t => !t.IsMultiCell && t.ExistsInTheater);
                (Dictionary<Point, Smudge> undo, Dictionary<Point, Smudge> redo) = Journals();
                SmudgeEdit.Place(map, crater, bibCell, undo, redo);
                Assert.Same(autoBib, map.Smudge[bibCell]);
                SmudgeEdit.Erase(map, crater, bibCell, undo, redo);
                Assert.Same(autoBib, map.Smudge[bibCell]);
            }
            finally
            {
                map.Buildings.Remove(building);
            }
        }
    }
}
