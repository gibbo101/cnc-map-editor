using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// The placement ghost is a live render, not a stretched thumbnail: the type drawn at the
    /// current tile size through the map renderer's preview path (semi-transparent, remapped
    /// to the placement house). Templates return null — their thumbnails are already exact.
    /// </summary>
    public class BrushPreviewTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        private static (int opaque, int translucent) CountAlpha(Bitmap bmp)
        {
            int opaque = 0, translucent = 0;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[data.Stride * bmp.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] == 255) opaque++;
                    else if (pixels[i] > 0) translucent++;
                }
            }
            finally { bmp.UnlockBits(data); }
            return (opaque, translucent);
        }

        [Fact]
        public void BuildingPreviewIsFootprintSizedAndTranslucent()
        {
            using (MapDocument doc = Open())
            {
                BuildingType fact = doc.AvailableBuildings().First(b => b.Name == "fact");
                using (Bitmap preview = doc.RenderBrushPreview(fact))
                {
                    Assert.Equal(fact.Size.Width * doc.TileSize.Width, preview.Width);
                    Assert.Equal(fact.Size.Height * doc.TileSize.Height, preview.Height);
                    (int opaque, int translucent) = CountAlpha(preview);
                    // The renderer's preview path halves the alpha: content, none of it opaque.
                    Assert.True(translucent > 0, "no translucent content pixels");
                    Assert.Equal(0, opaque);
                }
            }
        }

        [Fact]
        public void PreviewFollowsTheCurrentScale()
        {
            using (MapDocument doc = Open())
            {
                BuildingType fact = doc.AvailableBuildings().First(b => b.Name == "fact");
                doc.Scale = 0.25;
                using (Bitmap preview = doc.RenderBrushPreview(fact))
                {
                    Assert.Equal(fact.Size.Width * 32, preview.Width);
                }
            }
        }

        [Fact]
        public void UnitPreviewRemapsToThePlacementHouse()
        {
            using (MapDocument doc = Open())
            {
                UnitType tank = doc.AvailableUnits().First(u => u.Name == "3tnk");
                doc.PlacementHouse = doc.Map.HouseTypes.First(h => h.Name == "Greece");
                using (Bitmap greece = doc.RenderBrushPreview(tank))
                {
                    doc.PlacementHouse = doc.Map.HouseTypes.First(h => h.Name == "USSR");
                    using (Bitmap ussr = doc.RenderBrushPreview(tank))
                    {
                        Assert.Equal(doc.TileSize.Width, greece.Width);
                        Assert.NotEqual(ToBytes(greece), ToBytes(ussr));
                    }
                }
            }
        }

        [Fact]
        public void EveryObjectBrushKindRendersAPreview()
        {
            using (MapDocument doc = Open())
            {
                object[] brushes =
                {
                    doc.AvailableUnits().First(u => u.IsGroundUnit),
                    doc.AvailableInfantry().First(),
                    doc.Map.TerrainTypes.First(t => t.ExistsInTheater),
                    doc.AvailableOverlays().First(o => o.IsResource),
                    doc.AvailableOverlays().First(o => o.IsWall),
                    doc.AvailableSmudge().First(),
                };
                foreach (object brush in brushes)
                {
                    using (Bitmap preview = doc.RenderBrushPreview(brush))
                    {
                        Assert.NotNull(preview);
                        (int opaque, int translucent) = CountAlpha(preview);
                        Assert.True(translucent > 0, brush + " rendered no translucent pixels");
                        Assert.Equal(0, opaque);
                    }
                }
                // Templates keep their exact thumbnails.
                Assert.Null(doc.RenderBrushPreview(doc.AvailableTemplates().First()));
            }
        }

        private static byte[] ToBytes(Bitmap bmp)
        {
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[data.Stride * bmp.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return pixels;
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
