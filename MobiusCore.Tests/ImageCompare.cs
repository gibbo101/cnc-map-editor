using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MobiusCore.Tests
{
    public sealed class ImageDiff
    {
        public long Differing;
        public long Total;
        public int MaxChannelDelta;
        public Rectangle Bounds = Rectangle.Empty;
        public string FirstExample;
        public override string ToString() => $"{Differing}/{Total} pixels differ (max channel delta {MaxChannelDelta}) within {Bounds}; first: {FirstExample}";
    }

    public static class ImageCompare
    {
        /// <summary>Per-pixel BGRA comparison; pixels whose every channel is within tolerance count as equal.</summary>
        public static ImageDiff Compare(Bitmap a, Bitmap b, int tolerance = 0)
        {
            if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException($"Size mismatch: {a.Width}x{a.Height} vs {b.Width}x{b.Height}");
            ImageDiff d = new ImageDiff { Total = (long)a.Width * a.Height };
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            Rectangle all = new Rectangle(0, 0, a.Width, a.Height);
            BitmapData da = a.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData db = b.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] ra = new byte[da.Stride], rb = new byte[db.Stride];
                for (int y = 0; y < a.Height; y++)
                {
                    Marshal.Copy(da.Scan0 + y * da.Stride, ra, 0, da.Stride);
                    Marshal.Copy(db.Scan0 + y * db.Stride, rb, 0, db.Stride);
                    for (int x = 0; x < a.Width; x++)
                    {
                        int i = x * 4;
                        // Fully transparent pixels compare equal regardless of their colour channels.
                        if (ra[i + 3] == 0 && rb[i + 3] == 0) continue;
                        int delta = Math.Max(Math.Max(Math.Abs(ra[i] - rb[i]), Math.Abs(ra[i + 1] - rb[i + 1])), Math.Max(Math.Abs(ra[i + 2] - rb[i + 2]), Math.Abs(ra[i + 3] - rb[i + 3])));
                        if (delta <= tolerance) continue;
                        d.Differing++;
                        d.MaxChannelDelta = Math.Max(d.MaxChannelDelta, delta);
                        if (d.FirstExample == null) d.FirstExample = $"({x},{y}) expected BGRA {ra[i]},{ra[i + 1]},{ra[i + 2]},{ra[i + 3]} got {rb[i]},{rb[i + 1]},{rb[i + 2]},{rb[i + 3]}";
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    }
                }
            }
            finally { a.UnlockBits(da); b.UnlockBits(db); }
            if (maxX >= 0) d.Bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            return d;
        }
    }
}
