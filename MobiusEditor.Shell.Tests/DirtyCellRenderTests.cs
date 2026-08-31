using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// Render() keeps a cached bitmap and repaints only the cells the brushes touched
    /// (expanded one cell outward, because the Overlay grid self-heals neighboring wall
    /// icons and resource density without journaling them). The invariant: an incremental
    /// render is pixel-identical to a from-scratch render of the same map state — across
    /// paints, erases, wall adjacency healing, and undo.
    /// </summary>
    public class DirtyCellRenderTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            doc.Scale = 1.0 / 16;
            return doc;
        }

        /// <summary>A full render of the document's current state, by cycling the scale so the cache can't be reused.</summary>
        private static Bitmap FullRender(MapDocument doc)
        {
            double original = doc.Scale;
            doc.Scale = original * 2;
            using (doc.Render()) { }
            doc.Scale = original;
            return doc.Render();
        }

        private static void AssertIdentical(Bitmap incremental, MapDocument doc)
        {
            using (Bitmap full = FullRender(doc))
            {
                ImageDiff diff = ImageCompare.Compare(incremental, full);
                Assert.True(diff.Differing == 0, "incremental render differs from full render: " + diff);
            }
        }

        [Fact]
        public void IncrementalRenderAfterBrushOpsMatchesAFullRender()
        {
            using (MapDocument doc = Open())
            {
                using (doc.Render()) { }
                TemplateType template = doc.AvailableTemplates().First(t => !t.Flags.HasFlag(TemplateTypeFlag.Clear));
                doc.PlaceTemplate(new Point(10, 10), template);
                OverlayType wall = doc.AvailableOverlays().First(o => o.IsWall);
                doc.PlaceOverlay(new Point(20, 20), wall);
                doc.EraseTemplate(new Point(10, 10), template);
                using (Bitmap incremental = doc.Render())
                {
                    AssertIdentical(incremental, doc);
                }
            }
        }

        [Fact]
        public void NeighborWallHealingIsRepaintedWithoutBeingJournaled()
        {
            using (MapDocument doc = Open())
            {
                OverlayType wall = doc.AvailableOverlays().First(o => o.IsWall);
                doc.PlaceOverlay(new Point(20, 20), wall);
                using (doc.Render()) { }
                // Only this op is journaled, but it re-heals the first wall's adjacency icon.
                doc.PlaceOverlay(new Point(21, 20), wall);
                using (Bitmap incremental = doc.Render())
                {
                    AssertIdentical(incremental, doc);
                }
            }
        }

        [Fact]
        public void UndoRepaintsTheReplayedCells()
        {
            using (MapDocument doc = Open())
            {
                TemplateType template = doc.AvailableTemplates().First(t => !t.Flags.HasFlag(TemplateTypeFlag.Clear));
                doc.PlaceTemplate(new Point(30, 30), template);
                using (doc.Render()) { }
                doc.Undo();
                using (Bitmap incremental = doc.Render())
                {
                    AssertIdentical(incremental, doc);
                }
                doc.Redo();
                using (Bitmap incremental = doc.Render())
                {
                    AssertIdentical(incremental, doc);
                }
            }
        }

        [Fact]
        public void RenderBlockMatchesTheFullRenderCrop()
        {
            using (MapDocument doc = Open())
            {
                doc.Scale = 0.25;
                Rectangle block = new Rectangle(38, 34, 12, 9);
                using (Bitmap blockRender = doc.RenderBlock(block))
                using (Bitmap full = doc.Render())
                {
                    System.Drawing.Size tile = doc.TileSize;
                    Rectangle cropRect = new Rectangle(block.X * tile.Width, block.Y * tile.Height, block.Width * tile.Width, block.Height * tile.Height);
                    using (Bitmap crop = new Bitmap(cropRect.Width, cropRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        System.Drawing.Imaging.BitmapData src = full.LockBits(cropRect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        System.Drawing.Imaging.BitmapData dst = crop.LockBits(new Rectangle(0, 0, cropRect.Width, cropRect.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        try
                        {
                            byte[] row = new byte[cropRect.Width * 4];
                            for (int y = 0; y < cropRect.Height; y++)
                            {
                                System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, row, 0, row.Length);
                                System.Runtime.InteropServices.Marshal.Copy(row, 0, dst.Scan0 + y * dst.Stride, row.Length);
                            }
                        }
                        finally
                        {
                            full.UnlockBits(src);
                            crop.UnlockBits(dst);
                        }
                        // Tolerance 1: drawing under a translated transform shifts Skia's blend
                        // rounding on partially transparent sprites by at most one channel unit.
                        ImageDiff diff = ImageCompare.Compare(blockRender, crop, 1);
                        Assert.True(diff.Differing == 0, "block render differs from the full render's crop: " + diff);
                    }
                }
            }
        }

        [Fact]
        public void TriggerSessionsInvalidateTheWholeCache()
        {
            using (MapDocument doc = Open())
            {
                using (doc.Render()) { }
                doc.EditTriggers(ed => ed.Add());
                using (Bitmap incremental = doc.Render())
                {
                    AssertIdentical(incremental, doc);
                }
            }
        }
    }
}
