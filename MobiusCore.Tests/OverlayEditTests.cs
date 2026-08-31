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
    /// OverlayEdit ports the fork's overlay/wall/resource tool semantics headlessly. The
    /// core's Overlay grid self-heals on assignment (wall adjacency icons, resource density,
    /// concrete shapes), so the operations are thin guards: nothing places on the top or
    /// bottom row (the game engine won't read it back), a real existing overlay is never
    /// overwritten, and erase only removes its own category — a wall eraser leaves resources
    /// alone. Resource types re-randomise deterministically per cell; density is derived,
    /// never authored.
    /// </summary>
    public class OverlayEditTests
    {
        private static IGamePlugin plugin;

        private static Map LoadMap()
        {
            if (plugin == null)
            {
                plugin = EditorHost.Shared.Load(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), out _);
            }
            return plugin.Map;
        }

        private static OverlayType Type(Map map, string name) => map.OverlayTypes.Single(t => t.Name == name);

        private static (Dictionary<int, Overlay> undo, Dictionary<int, Overlay> redo) Dicts() =>
            (new Dictionary<int, Overlay>(), new Dictionary<int, Overlay>());

        /// <summary>The oracle map has real content; give each test a clean patch to work on.</summary>
        private static void ClearArea(Map map, int x, int y, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                    map.Overlay[y + dy, x + dx] = null;
        }

        [Fact]
        public void AdjacentWallsHealTheirShapeIcons()
        {
            Map map = LoadMap();
            OverlayType brik = Type(map, "brik");
            ClearArea(map, 10, 10, 2);
            (Dictionary<int, Overlay> undo, Dictionary<int, Overlay> redo) = Dicts();
            OverlayEdit.Place(map, brik, new Point(10, 10), undo, redo);
            OverlayEdit.Place(map, brik, new Point(11, 10), undo, redo);
            // Shape icon bits: N=1, E=2, S=4, W=8.
            Assert.Equal(2, map.Overlay[10, 10].Icon);
            Assert.Equal(8, map.Overlay[10, 11].Icon);
            Assert.Equal(2, redo.Count);
        }

        [Fact]
        public void TopAndBottomRowsRefusePlacement()
        {
            Map map = LoadMap();
            OverlayType brik = Type(map, "brik");
            (Dictionary<int, Overlay> undo, Dictionary<int, Overlay> redo) = Dicts();
            OverlayEdit.Place(map, brik, new Point(20, 0), undo, redo);
            OverlayEdit.Place(map, brik, new Point(20, map.Metrics.Height - 1), undo, redo);
            Assert.Empty(redo);
            Assert.Null(map.Overlay[0, 20]);
        }

        [Fact]
        public void ResourceDensityIsDerivedFromNeighbours()
        {
            Map map = LoadMap();
            OverlayType gold = Type(map, "gold01");
            ClearArea(map, 60, 60, 2);
            (Dictionary<int, Overlay> undo, Dictionary<int, Overlay> redo) = Dicts();
            OverlayEdit.Place(map, gold, new Point(60, 60), undo, redo);
            Overlay lone = map.Overlay[60, 60];
            Assert.NotNull(lone);
            Assert.True(lone.Type.IsTiberiumOrGold);
            Assert.Equal(0, lone.Icon);
            // Surround it: the centre's density climbs to the 4-neighbour stage.
            OverlayEdit.Place(map, gold, new Point(59, 60), undo, redo);
            OverlayEdit.Place(map, gold, new Point(61, 60), undo, redo);
            OverlayEdit.Place(map, gold, new Point(60, 59), undo, redo);
            OverlayEdit.Place(map, gold, new Point(60, 61), undo, redo);
            Assert.Equal(6, map.Overlay[60, 60].Icon);
        }

        [Fact]
        public void ARealExistingOverlayIsNeverOverwritten()
        {
            Map map = LoadMap();
            OverlayType gold = Type(map, "gold01");
            OverlayType brik = Type(map, "brik");
            ClearArea(map, 70, 70, 1);
            (Dictionary<int, Overlay> undo, Dictionary<int, Overlay> redo) = Dicts();
            OverlayEdit.Place(map, gold, new Point(70, 70), undo, redo);
            OverlayType placed = map.Overlay[70, 70].Type;
            (undo, redo) = Dicts();
            OverlayEdit.Place(map, brik, new Point(70, 70), undo, redo);
            Assert.Empty(redo);
            Assert.Same(placed, map.Overlay[70, 70].Type);
        }

        [Fact]
        public void EraseOnlyRemovesItsOwnCategory()
        {
            Map map = LoadMap();
            OverlayType gold = Type(map, "gold01");
            OverlayType brik = Type(map, "brik");
            ClearArea(map, 80, 80, 1);
            (Dictionary<int, Overlay> undo, Dictionary<int, Overlay> redo) = Dicts();
            OverlayEdit.Place(map, gold, new Point(80, 80), undo, redo);
            (undo, redo) = Dicts();
            OverlayEdit.Erase(map, brik, new Point(80, 80), undo, redo);
            Assert.NotNull(map.Overlay[80, 80]);
            Assert.Empty(redo);
            OverlayEdit.Erase(map, gold, new Point(80, 80), undo, redo);
            Assert.Null(map.Overlay[80, 80]);
            Assert.Single(redo);
        }
    }
}
