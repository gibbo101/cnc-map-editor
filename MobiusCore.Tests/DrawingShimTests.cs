using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>The Skia-backed System.Drawing shim must reproduce the GDI+ semantics the core relies on.</summary>
    public class DrawingShimTests
    {
        private static Bitmap Noise(int w, int h, int seed, bool alpha)
        {
            Random r = new Random(seed);
            Bitmap bm = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    bm.SetPixel(x, y, Color.FromArgb(alpha ? r.Next(256) : 255, r.Next(256), r.Next(256), r.Next(256)));
            return bm;
        }

        [Fact]
        public void OpaqueBlitAtOneToOneIsExact()
        {
            using (Bitmap src = Noise(64, 64, 1, false))
            using (Bitmap dst = new Bitmap(128, 128, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dst))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(src, new Rectangle(32, 16, 64, 64), 0, 0, 64, 64, GraphicsUnit.Pixel, null);
                }
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        Assert.Equal(src.GetPixel(x, y), dst.GetPixel(x + 32, y + 16));
                Assert.Equal(0, dst.GetPixel(0, 0).A);
            }
        }

        [Fact]
        public void OpaqueBlitWithDefaultInterpolationIsExact()
        {
            using (Bitmap src = Noise(64, 64, 2, false))
            using (Bitmap dst = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dst)) g.DrawImage(src, 0, 0);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        Assert.Equal(src.GetPixel(x, y), dst.GetPixel(x, y));
            }
        }

        [Fact]
        public void AlphaBlitOntoTransparentCanvasKeepsColour()
        {
            using (Bitmap src = Noise(32, 32, 3, true))
            using (Bitmap dst = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dst)) g.DrawImage(src, 0, 0);
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        Color a = src.GetPixel(x, y), b = dst.GetPixel(x, y);
                        Assert.Equal(a.A, b.A);
                        if (a.A == 0) continue;
                        // Premultiplied storage quantises colour to steps of 255/alpha; GDI+ has the same loss.
                        int allowed = (int)Math.Ceiling(255.0 / a.A / 2.0);
                        int drift = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                        Assert.True(drift <= allowed, $"drift {drift} at alpha {a.A} exceeds {allowed}");
                    }
            }
        }

        [Fact]
        public void LockBitsRoundTripsAndConverts()
        {
            using (Bitmap bm = new Bitmap(3, 2, PixelFormat.Format24bppRgb))
            {
                bm.SetPixel(2, 1, Color.FromArgb(255, 10, 20, 30));
                BitmapData d = bm.LockBits(new Rectangle(0, 0, 3, 2), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] px = new byte[4];
                Marshal.Copy(d.Scan0 + d.Stride + 8, px, 0, 4);
                bm.UnlockBits(d);
                Assert.Equal(new byte[] { 30, 20, 10, 255 }, px);
            }
        }

        [Fact]
        public void IndexedBitmapDrawsThroughPalette()
        {
            using (Bitmap idx = new Bitmap(2, 1, PixelFormat.Format8bppIndexed))
            {
                ColorPalette pal = idx.Palette;
                pal.Entries[0] = Color.FromArgb(255, 1, 2, 3);
                pal.Entries[7] = Color.FromArgb(255, 200, 100, 50);
                idx.Palette = pal;
                BitmapData d = idx.LockBits(new Rectangle(0, 0, 2, 1), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                Marshal.Copy(new byte[] { 0, 7 }, 0, d.Scan0, 2);
                idx.UnlockBits(d);
                using (Bitmap dst = new Bitmap(2, 1))
                {
                    using (Graphics g = Graphics.FromImage(dst)) g.DrawImage(idx, 0, 0);
                    Assert.Equal(Color.FromArgb(255, 1, 2, 3), dst.GetPixel(0, 0));
                    Assert.Equal(Color.FromArgb(255, 200, 100, 50), dst.GetPixel(1, 0));
                }
            }
        }

        [Fact]
        public void ColorMatrixScalesAlpha()
        {
            using (Bitmap src = new Bitmap(1, 1))
            using (Bitmap dst = new Bitmap(1, 1))
            using (ImageAttributes attr = new ImageAttributes())
            {
                src.SetPixel(0, 0, Color.FromArgb(255, 100, 150, 200));
                attr.SetColorMatrix(new ColorMatrix { Matrix33 = 0.5f });
                using (Graphics g = Graphics.FromImage(dst)) g.DrawImage(src, new Rectangle(0, 0, 1, 1), 0, 0, 1, 1, GraphicsUnit.Pixel, attr);
                Color c = dst.GetPixel(0, 0);
                Assert.InRange(c.A, 127, 128);
                Assert.InRange(c.R, 99, 101); Assert.InRange(c.G, 149, 151); Assert.InRange(c.B, 199, 201);
            }
        }

        [Fact]
        public void FillRectangleCoversExactPixels()
        {
            using (Bitmap dst = new Bitmap(8, 8))
            {
                using (Graphics g = Graphics.FromImage(dst))
                using (SolidBrush b = new SolidBrush(Color.Red))
                    g.FillRectangle(b, new Rectangle(2, 3, 3, 2));
                Assert.Equal(255, dst.GetPixel(2, 3).A); Assert.Equal(255, dst.GetPixel(4, 4).A);
                Assert.Equal(0, dst.GetPixel(1, 3).A); Assert.Equal(0, dst.GetPixel(5, 3).A); Assert.Equal(0, dst.GetPixel(2, 5).A);
            }
        }

        [Fact]
        public void DrawRectangleIsInclusiveLikeGdiPlus()
        {
            using (Bitmap dst = new Bitmap(8, 8))
            {
                using (Graphics g = Graphics.FromImage(dst))
                using (Pen p = new Pen(Color.Blue))
                    g.DrawRectangle(p, new Rectangle(1, 1, 4, 4));
                Assert.Equal(255, dst.GetPixel(1, 1).A); Assert.Equal(255, dst.GetPixel(5, 5).A); Assert.Equal(255, dst.GetPixel(3, 1).A);
                Assert.Equal(0, dst.GetPixel(3, 3).A); Assert.Equal(0, dst.GetPixel(6, 6).A); Assert.Equal(0, dst.GetPixel(0, 0).A);
            }
        }

        [Fact]
        public void RegionUnionIntersectTranslateRoundTrip()
        {
            using (Region r = new Region())
            {
                r.MakeEmpty();
                r.Union(new Rectangle(0, 0, 4, 2));
                r.Union(new Rectangle(2, 0, 4, 2));
                r.Union(new Rectangle(0, 5, 1, 1));
                using (Region clip = new Region(new Rectangle(1, 0, 3, 10))) r.Intersect(clip);
                r.Translate(10, 10);
                RegionData data = r.GetRegionData();
                using (Region back = new Region(data))
                {
                    Assert.True(back.IsVisible(new Point(11, 10))); Assert.True(back.IsVisible(new Point(13, 11)));
                    Assert.False(back.IsVisible(new Point(10, 10))); Assert.False(back.IsVisible(new Point(14, 10))); Assert.False(back.IsVisible(new Point(10, 15)));
                }
            }
        }

        [Fact]
        public void SaveAndReloadPngIsLossless()
        {
            using (Bitmap src = Noise(16, 16, 4, true))
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                src.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                using (Bitmap back = new Bitmap(ms))
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                            Assert.Equal(src.GetPixel(x, y), back.GetPixel(x, y));
            }
        }
    }
}
