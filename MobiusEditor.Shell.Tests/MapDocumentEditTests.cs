using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using MobiusEditor.Shell;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// The document's editing surface: place/erase templates and overlay with per-stroke
    /// undo. Operations outside an explicit stroke commit as one undo step each; a
    /// BeginStroke/EndStroke pair batches everything between into a single step (a mouse
    /// drag). Placing the Clear template is erasing — null cells ARE clear terrain.
    /// </summary>
    public class MapDocumentEditTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            // Temperate: the TF TD-temperate tile family has art here. On other theaters a
            // template's Init leaves an all-false icon mask and placement correctly no-ops
            // (no art, no placement) — pinned in TemplateEditTests.
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        private static TemplateType Template(MapDocument doc, string name) => doc.Map.TemplateTypes.Single(t => t.Name == name);
        private static OverlayType OverlayOf(MapDocument doc, string name) => doc.Map.OverlayTypes.Single(t => t.Name == name);

        [Fact]
        public void PlacingATemplateIsUndoableAndRedoable()
        {
            using (MapDocument doc = Open())
            {
                TemplateType sh1 = Template(doc, "tdsh1");
                Template before = doc.Map.Templates[10, 10];
                int changes = 0;
                doc.Changed += (s, e) => changes++;
                Assert.False(doc.CanUndo);
                doc.PlaceTemplate(new Point(10, 10), sh1);
                Assert.Same(sh1, doc.Map.Templates[10, 10].Type);
                Assert.True(doc.CanUndo);
                Assert.Equal(1, changes);
                doc.Undo();
                Assert.Same(before, doc.Map.Templates[10, 10]);
                Assert.True(doc.CanRedo);
                Assert.False(doc.CanUndo);
                doc.Redo();
                Assert.Same(sh1, doc.Map.Templates[10, 10].Type);
                Assert.Equal(3, changes);
            }
        }

        [Fact]
        public void AStrokeBatchesIntoOneUndoStep()
        {
            using (MapDocument doc = Open())
            {
                TemplateType sh1 = Template(doc, "tdsh1");
                Template before20 = doc.Map.Templates[20, 20];
                Template before30 = doc.Map.Templates[30, 30];
                doc.BeginStroke();
                doc.PlaceTemplate(new Point(20, 20), sh1);
                doc.PlaceTemplate(new Point(30, 30), sh1);
                Assert.False(doc.CanUndo);
                doc.EndStroke();
                Assert.True(doc.CanUndo);
                doc.Undo();
                Assert.False(doc.CanUndo);
                Assert.Same(before20, doc.Map.Templates[20, 20]);
                Assert.Same(before30, doc.Map.Templates[30, 30]);
            }
        }

        [Fact]
        public void SeparateOperationsAreSeparateUndoSteps()
        {
            using (MapDocument doc = Open())
            {
                TemplateType sh1 = Template(doc, "tdsh1");
                doc.PlaceTemplate(new Point(40, 40), sh1);
                doc.PlaceTemplate(new Point(50, 50), sh1);
                doc.Undo();
                Assert.Same(sh1, doc.Map.Templates[40, 40].Type);
                Assert.NotEqual(sh1, doc.Map.Templates[50, 50]?.Type);
                doc.Undo();
                Assert.NotEqual(sh1, doc.Map.Templates[40, 40]?.Type);
            }
        }

        [Fact]
        public void PlacingTheClearTemplateErases()
        {
            using (MapDocument doc = Open())
            {
                TemplateType sh1 = Template(doc, "tdsh1");
                TemplateType clear = doc.Map.TemplateTypes.First(t => t.Flags.HasFlag(TemplateTypeFlag.Clear));
                doc.PlaceTemplate(new Point(60, 60), sh1);
                doc.PlaceTemplate(new Point(60, 60), clear);
                Assert.Null(doc.Map.Templates[60, 60]);
            }
        }

        [Fact]
        public void OverlayPlacementIsUndoable()
        {
            using (MapDocument doc = Open())
            {
                OverlayType brik = OverlayOf(doc, "brik");
                doc.EraseOverlay(new Point(70, 70), null);
                doc.PlaceOverlay(new Point(70, 70), brik);
                Assert.Same(brik, doc.Map.Overlay[70, 70].Type);
                doc.Undo();
                Assert.NotEqual(brik, doc.Map.Overlay[70, 70]?.Type);
            }
        }

        [Fact]
        public void UndoneEditsLeaveNoTraceInTheSavedFile()
        {
            using (MapDocument doc = Open())
            {
                string baseline = TestPaths.Output("edit-baseline.ini");
                string edited = TestPaths.Output("edit-undone.ini");
                doc.Save(baseline);
                doc.BeginStroke();
                doc.PlaceTemplate(new Point(90, 90), Template(doc, "tdsh1"));
                doc.PlaceOverlay(new Point(95, 95), OverlayOf(doc, "brik"));
                doc.PlaceOverlay(new Point(97, 97), OverlayOf(doc, "gold01"));
                doc.EndStroke();
                doc.EraseTemplate(new Point(91, 91));
                doc.Undo();
                doc.Undo();
                Assert.False(doc.CanUndo);
                doc.Save(edited);
                Assert.Equal(File.ReadAllBytes(baseline), File.ReadAllBytes(edited));
            }
        }

        [Fact]
        public void EditsMarkThePluginDirty()
        {
            using (MapDocument doc = Open())
            {
                doc.Plugin.Dirty = false;
                doc.PlaceTemplate(new Point(80, 80), Template(doc, "tdsh1"));
                Assert.True(doc.Plugin.Dirty);
            }
        }
    }
}
