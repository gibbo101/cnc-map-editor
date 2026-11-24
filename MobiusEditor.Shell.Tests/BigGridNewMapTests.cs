using System.Drawing;
using MobiusCore.Tests;
using MobiusEditor.Headless;
using MobiusEditor.Model;
using MobiusEditor.Shell;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>A new map past 126 cells a side goes on the 256 grid, and only when a loaded mod declares that grid.</summary>
    public class BigGridNewMapTests
    {
        [Fact]
        public void ASizePastTheSmallGridPicksTheBigGridWhenAModDeclaresIt()
        {
            EditorSession session = new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { FormatFourTests.GridMod() });
            using (MapDocument doc = new MapDocument(session))
            {
                Assert.Equal(254, doc.MaxPlayableSize);
                doc.NewMap("Temperate", new Size(200, 150));
                Assert.Equal(256, doc.Map.Metrics.Width);
                Assert.Equal(new Size(200, 150), doc.Map.Bounds.Size);
            }
        }

        [Fact]
        public void WithoutTheDeclarationTheSizeStaysOnTheSmallGrid()
        {
            using (MapDocument doc = new MapDocument(EditorHost.Shared))
            {
                Assert.Equal(126, doc.MaxPlayableSize);
                doc.NewMap("Temperate", new Size(200, 150));
                Assert.Equal(128, doc.Map.Metrics.Width);
                Assert.Equal(new Size(126, 126), doc.Map.Bounds.Size);
            }
        }
    }
}
