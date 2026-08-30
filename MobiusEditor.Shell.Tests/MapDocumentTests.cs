using System.Drawing;
using System.IO;
using MobiusCore.Tests;
using MobiusEditor.Model;
using MobiusEditor.Shell;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>The GUI's map document is fully exercisable without a window.</summary>
    public class MapDocumentTests
    {
        private static MapDocument OpenKeepOffTheGrass()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm111ea.ini"));
            return doc;
        }

        [Fact]
        public void OpensAndTitlesFromTheMapName()
        {
            using (MapDocument doc = OpenKeepOffTheGrass())
            {
                Assert.True(doc.IsOpen);
                Assert.False(string.IsNullOrEmpty(doc.Title));
                Assert.NotEqual("No map", doc.Title);
            }
        }

        [Fact]
        public void RendersAtTheChosenScale()
        {
            using (MapDocument doc = OpenKeepOffTheGrass())
            {
                doc.Scale = 0.125;
                using (Bitmap bm = doc.Render())
                {
                    Assert.Equal(128 * 16, bm.Width);
                    Assert.Equal(128 * 16, bm.Height);
                }
            }
        }

        [Fact]
        public void ScaleIsClamped()
        {
            using (MapDocument doc = OpenKeepOffTheGrass())
            {
                doc.Scale = 50; Assert.Equal(2.0, doc.Scale);
                doc.Scale = 0; Assert.Equal(1.0 / 16, doc.Scale);
            }
        }

        [Fact]
        public void MapsPixelsToCellsAndDescribesThem()
        {
            using (MapDocument doc = OpenKeepOffTheGrass())
            {
                doc.Scale = 0.25;
                Assert.Equal(new Point(3, 2), doc.CellAt(3 * 32 + 5, 2 * 32 + 31));
                Assert.Null(doc.CellAt(-1, 0));
                Assert.Null(doc.CellAt(128 * 32, 0));
                Point inside = new Point(doc.Map.Bounds.X + 2, doc.Map.Bounds.Y + 2);
                Assert.StartsWith($"({inside.X},{inside.Y})", doc.Describe(inside));
            }
        }

        [Fact]
        public void RaisesChangedOnOpen()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            int changes = 0;
            doc.Changed += (s, e) => changes++;
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm111ea.ini"));
            Assert.Equal(1, changes);
            doc.Dispose();
        }
    }
}
