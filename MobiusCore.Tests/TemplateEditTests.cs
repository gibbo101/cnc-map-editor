using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// TemplateEdit ports the mono editor's TemplateTool placement semantics headlessly:
    /// a whole-template stamp writes row-major positional icons, masked icons leave their
    /// cells untouched (but still advance the icon number), off-grid cells clip silently,
    /// 1x1 random templates roll their icon, group pseudo-templates resolve to a member
    /// type, and erase clears the same footprint to null (null IS clear terrain). The one
    /// deliberate deviation from the fork: randomness comes from an injected
    /// DeterministicRandom, never an unseeded RNG.
    /// </summary>
    public class TemplateEditTests
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

        private static TemplateType Type(Map map, string name) => map.TemplateTypes.Single(t => t.Name == name);

        private static (Dictionary<int, Template> undo, Dictionary<int, Template> redo) Dicts() =>
            (new Dictionary<int, Template>(), new Dictionary<int, Template>());

        [Fact]
        public void StampWritesRowMajorPositionalIcons()
        {
            Map map = LoadMap();
            TemplateType sh1 = Type(map, "tdsh1");
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh1, new Point(10, 10), null, new DeterministicRandom(1), undo, redo);
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    Template t = map.Templates[10 + y, 10 + x];
                    Assert.NotNull(t);
                    Assert.Same(sh1, t.Type);
                    Assert.Equal(y * 3 + x, t.Icon);
                }
            }
            Assert.Equal(9, redo.Count);
            Assert.Equal(9, undo.Count);
        }

        [Fact]
        public void MaskedIconsLeaveTheirCellsUntouchedButStillAdvanceTheIconNumber()
        {
            Map map = LoadMap();
            // tdsh15: 3x3, mask "011 111 111" - icon 0 is filler.
            TemplateType sh15 = Type(map, "tdsh15");
            TemplateType sh1 = Type(map, "tdsh1");
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh1, new Point(20, 20), null, new DeterministicRandom(1), undo, redo);
            (undo, redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh15, new Point(20, 20), null, new DeterministicRandom(1), undo, redo);
            // Masked corner keeps the sh1 tile placed before; its neighbour got sh15's icon 1.
            Assert.Same(sh1, map.Templates[20, 20].Type);
            Assert.Same(sh15, map.Templates[20, 21].Type);
            Assert.Equal(1, map.Templates[20, 21].Icon);
            Assert.Equal(8, redo.Count);
            Assert.False(undo.ContainsKey(20 * map.Metrics.Width + 20));
        }

        [Fact]
        public void PlacementClipsAtTheGridEdge()
        {
            Map map = LoadMap();
            TemplateType sh1 = Type(map, "tdsh1");
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            int w = map.Metrics.Width, h = map.Metrics.Height;
            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh1, new Point(w - 2, h - 2), null, new DeterministicRandom(1), undo, redo);
            Assert.Equal(4, redo.Count);
            Assert.Equal(0, map.Templates[h - 2, w - 2].Icon);
            Assert.Equal(4, map.Templates[h - 1, w - 1].Icon);
        }

        [Fact]
        public void SingleIconPickPlacesOneCellUsingTheThumbnailGrid()
        {
            Map map = LoadMap();
            TemplateType sh1 = Type(map, "tdsh1");
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh1, new Point(30, 30), new Point(1, 2), new DeterministicRandom(1), undo, redo);
            Template placed = Assert.Single(redo).Value;
            Assert.Same(sh1, placed.Type);
            Assert.Equal(2 * sh1.ThumbnailIconWidth + 1, placed.Icon);
            Assert.Same(placed, map.Templates[30, 30]);
        }

        [Fact]
        public void RandomOneByOneRollsItsIconFromTheInjectedRandom()
        {
            Map map = LoadMap();
            // No vanilla RA declaration carries RandomCell; 1x1 randomness only appears when a
            // theater ships extra tiles, so pin the branch with a synthetic type instead.
            TemplateType random1x1 = new TemplateType(999, "fake1x1", 1, 1, "C", TemplateTypeFlag.RandomCell);
            typeof(TemplateType).GetProperty(nameof(TemplateType.NumIcons)).SetValue(random1x1, 4);
            Assert.True(random1x1.IsRandom);
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, random1x1, new Point(40, 40), null, new DeterministicRandom(7), undo, redo);
            Assert.Equal(new DeterministicRandom(7).Next(4), map.Templates[40, 40].Icon);
        }

        [Fact]
        public void GroupTemplateResolvesToAMemberTypeWithIconZero()
        {
            Map map = LoadMap();
            TemplateType group = map.TemplateTypes.First(t => t.IsGroup);
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, group, new Point(50, 50), null, new DeterministicRandom(3), undo, redo);
            Template placed = map.Templates[50, 50];
            Assert.NotSame(group, placed.Type);
            Assert.Contains(placed.Type.Name, group.GroupTiles);
            Assert.Equal(0, placed.Icon);
            Assert.Equal(group.GroupTiles[new DeterministicRandom(3).Next(group.NumIcons)], placed.Type.Name);
        }

        [Fact]
        public void ATemplateWithNoArtInTheTheaterPlacesNothing()
        {
            // Theater Init rebuilds IconMask from the tiles actually found; a template whose
            // family has no art in the map's theater ends up all-false and placement no-ops,
            // matching the engine (and the fork, whose picker never offers such templates).
            Map map = LoadMap();
            TemplateType sh1 = Type(map, "tdsh1");
            TemplateType unavailable = new TemplateType(998, "fakeunavail", 2, 1, "CC") { IconMask = new bool[1, 2] };
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, unavailable, new Point(90, 90), null, new DeterministicRandom(1), undo, redo);
            Assert.Empty(redo);
            Assert.Empty(undo);
        }

        [Fact]
        public void EraseClearsTheFootprintToNullAndSingleCellEraseClearsOneCell()
        {
            Map map = LoadMap();
            TemplateType sh1 = Type(map, "tdsh1");
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh1, new Point(60, 60), null, new DeterministicRandom(1), undo, redo);
            (undo, redo) = Dicts();
            TemplateEdit.Erase(map.Templates, sh1, null, new Point(60, 60), undo, redo);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    Assert.Null(map.Templates[60 + y, 60 + x]);
            Assert.Equal(9, redo.Count);
            Assert.All(redo.Values, v => Assert.Null(v));

            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh1, new Point(60, 60), null, new DeterministicRandom(1), undo, redo);
            (undo, redo) = Dicts();
            TemplateEdit.Erase(map.Templates, sh1, new Point(0, 0), new Point(61, 61), undo, redo);
            Assert.Null(map.Templates[61, 61]);
            Assert.NotNull(map.Templates[60, 60]);
            Assert.Single(redo);
        }

        [Fact]
        public void UndoDictionariesCaptureBeforeValuesOnceAcrossAStroke()
        {
            Map map = LoadMap();
            TemplateType sh1 = Type(map, "tdsh1");
            Template original = map.Templates[70, 70];
            (Dictionary<int, Template> undo, Dictionary<int, Template> redo) = Dicts();
            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh1, new Point(70, 70), null, new DeterministicRandom(1), undo, redo);
            // Same stroke stamps again over the same origin: the first snapshot survives.
            TemplateEdit.Place(map.TemplateTypes, map.Templates, sh1, new Point(70, 70), null, new DeterministicRandom(1), undo, redo);
            int cell = 70 * map.Metrics.Width + 70;
            Assert.Same(original, undo[cell]);
            // Replaying undo restores the pre-stroke state.
            foreach (KeyValuePair<int, Template> kv in undo) map.Templates[kv.Key] = kv.Value;
            Assert.Same(original, map.Templates[70, 70]);
        }
    }
}
