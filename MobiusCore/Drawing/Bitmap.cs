using System;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    /// <summary>
    /// A bitmap stored in GDI+ memory layout (little-endian BGRA rows, 4-byte-aligned stride,
    /// palette for indexed formats) so raw-pixel code written against LockBits keeps working.
    /// </summary>
    public abstract class Image : IDisposable
    {
        public int Width { get; protected set; }
        public int Height { get; protected set; }
        public Size Size => new Size(Width, Height);
        public PixelFormat PixelFormat { get; protected set; }
        public float HorizontalResolution { get; private set; } = 96f;
        public float VerticalResolution { get; private set; } = 96f;
        public void SetResolution(float xDpi, float yDpi) { HorizontalResolution = xDpi; VerticalResolution = yDpi; }
        public abstract ColorPalette Palette { get; set; }
        public abstract void RotateFlip(RotateFlipType type);
        public abstract void Save(Stream stream, ImageFormat format);
        public void Save(Stream stream) => Save(stream, ImageFormat.Png);
        public void Save(string path, ImageFormat format) { using (FileStream fs = File.Create(path)) Save(fs, format); }
        public void Save(string path) => Save(path, ImageFormat.Png);
        public abstract void Dispose();

        public static int GetPixelFormatSize(PixelFormat fmt) => ((int)fmt >> 8) & 0xFF;
        public static bool IsAlphaPixelFormat(PixelFormat fmt) => ((int)fmt & (int)PixelFormat.Alpha) != 0;
        public static bool IsIndexedPixelFormat(PixelFormat fmt) => ((int)fmt & (int)PixelFormat.Indexed) != 0;
        public static Image FromStream(Stream stream) => new Bitmap(stream);
        public static Image FromFile(string path) => new Bitmap(path);
    }

    public sealed class Bitmap : Image
    {
        internal byte[] Pixels;
        internal int Stride;
        private ColorPalette palette;
        private SKImage skiaCache;
        private BitmapData activeLock;

        public Bitmap(int width, int height) : this(width, height, PixelFormat.Format32bppArgb) { }

        public Bitmap(int width, int height, PixelFormat format)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Bitmap dimensions must be positive.");
            Width = width; Height = height; PixelFormat = format;
            int bpp = GetPixelFormatSize(format);
            if (bpp == 0) throw new ArgumentException("Unsupported pixel format " + format);
            Stride = ((width * bpp + 31) / 32) * 4;
            Pixels = new byte[Stride * height];
            if (IsIndexedPixelFormat(format))
            {
                int entries = 1 << bpp;
                palette = new ColorPalette(entries);
                for (int i = 0; i < entries; i++) palette.Entries[i] = Color.FromArgb(255, i * 255 / (entries - 1), i * 255 / (entries - 1), i * 255 / (entries - 1));
            }
        }

        public Bitmap(Image original) : this(original, original.Width, original.Height) { }
        public Bitmap(Image original, Size size) : this(original, size.Width, size.Height) { }

        public Bitmap(Image original, int width, int height) : this(width, height, PixelFormat.Format32bppArgb)
        {
            using (Graphics g = Graphics.FromImage(this))
            {
                g.DrawImage(original, new Rectangle(0, 0, width, height));
            }
        }

        public Bitmap(Stream stream) : this(DecodeUnpremul(SKCodec.Create(stream))) { }
        public Bitmap(string path) : this(DecodeUnpremul(SKCodec.Create(path))) { }

        /// <summary>Decodes without premultiplying so file pixels survive a load/save round trip untouched.</summary>
        private static SKBitmap DecodeUnpremul(SKCodec codec)
        {
            if (codec == null) throw new ArgumentException("Image could not be decoded.");
            using (codec)
            {
                SKImageInfo info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                return SKBitmap.Decode(codec, info);
            }
        }

        private Bitmap(SKBitmap decoded) : this(decoded?.Width ?? throw new ArgumentException("Image could not be decoded."), decoded.Height, PixelFormat.Format32bppArgb)
        {
            using (decoded)
            {
                SKImageInfo info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                GCHandle h = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
                try { using (SKPixmap pm = decoded.PeekPixels()) pm.ReadPixels(info, h.AddrOfPinnedObject(), Stride, 0, 0); }
                finally { h.Free(); }
            }
        }

        public override ColorPalette Palette
        {
            get => palette == null ? new ColorPalette(0) : new ColorPalette((Color[])palette.Entries.Clone());
            set { palette = value == null ? null : new ColorPalette((Color[])value.Entries.Clone()); Invalidate(); }
        }

        internal void Invalidate() { skiaCache?.Dispose(); skiaCache = null; }

        public BitmapData LockBits(Rectangle rect, ImageLockMode mode, PixelFormat format)
        {
            if (activeLock != null) throw new InvalidOperationException("Bitmap is already locked.");
            if (rect.X < 0 || rect.Y < 0 || rect.Right > Width || rect.Bottom > Height || rect.Width <= 0 || rect.Height <= 0)
                throw new ArgumentException("Lock rectangle is outside the bitmap.");
            BitmapData data = new BitmapData { Width = rect.Width, Height = rect.Height, PixelFormat = format, Mode = mode, Area = rect };
            bool whole = rect.X == 0 && rect.Y == 0 && rect.Width == Width && rect.Height == Height;
            if (format == PixelFormat && whole)
            {
                data.Buffer = Pixels;
                data.Stride = Stride;
                data.IsConverted = false;
            }
            else
            {
                int bpp = GetPixelFormatSize(format);
                data.Stride = ((rect.Width * bpp + 31) / 32) * 4;
                data.Buffer = new byte[data.Stride * rect.Height];
                data.IsConverted = true;
                if (mode != ImageLockMode.WriteOnly) ConvertOut(rect, format, data.Buffer, data.Stride);
            }
            data.Handle = GCHandle.Alloc(data.Buffer, GCHandleType.Pinned);
            data.Scan0 = data.Handle.AddrOfPinnedObject();
            activeLock = data;
            return data;
        }

        public void UnlockBits(BitmapData data)
        {
            if (data == null || data != activeLock) throw new ArgumentException("BitmapData does not belong to this bitmap.");
            if (data.IsConverted && data.Mode != ImageLockMode.ReadOnly) ConvertIn(data.Area, data.PixelFormat, data.Buffer, data.Stride);
            data.Handle.Free();
            activeLock = null;
            if (data.Mode != ImageLockMode.ReadOnly) Invalidate();
        }

        public Color GetPixel(int x, int y)
        {
            byte[] px = new byte[4];
            ConvertOut(new Rectangle(x, y, 1, 1), PixelFormat.Format32bppArgb, px, 4);
            return Color.FromArgb(px[3], px[2], px[1], px[0]);
        }

        public void SetPixel(int x, int y, Color c)
        {
            byte[] px = { c.B, c.G, c.R, c.A };
            ConvertIn(new Rectangle(x, y, 1, 1), PixelFormat.Format32bppArgb, px, 4);
            Invalidate();
        }

        public Bitmap Clone(Rectangle rect, PixelFormat format)
        {
            Bitmap bm = new Bitmap(rect.Width, rect.Height, format);
            if (IsIndexedPixelFormat(format))
            {
                if (!IsIndexedPixelFormat(PixelFormat)) throw new ArgumentException("Cannot clone a non-indexed bitmap to an indexed format.");
                bm.palette = new ColorPalette((Color[])palette.Entries.Clone());
            }
            ConvertOut(rect, format, bm.Pixels, bm.Stride);
            return bm;
        }
        public Bitmap Clone(RectangleF rect, PixelFormat format) => Clone(Rectangle.Round(rect), format);
        public object Clone() => Clone(new Rectangle(0, 0, Width, Height), PixelFormat);

        public void MakeTransparent() => MakeTransparent(GetPixel(0, Height - 1));

        public void MakeTransparent(Color key)
        {
            if (PixelFormat != PixelFormat.Format32bppArgb)
            {
                Bitmap conv = Clone(new Rectangle(0, 0, Width, Height), PixelFormat.Format32bppArgb);
                Pixels = conv.Pixels; Stride = conv.Stride; PixelFormat = PixelFormat.Format32bppArgb; palette = null;
            }
            for (int y = 0; y < Height; y++)
            {
                int row = y * Stride;
                for (int x = 0; x < Width; x++)
                {
                    int i = row + x * 4;
                    if (Pixels[i] == key.B && Pixels[i + 1] == key.G && Pixels[i + 2] == key.R) Pixels[i + 3] = 0;
                }
            }
            Invalidate();
        }

        public override void RotateFlip(RotateFlipType type)
        {
            int t = (int)type;
            int rot = t & 3; bool flipX = (t & 4) != 0;
            // Only the flip variants the core uses are supported; rotation is 0 for all of them.
            bool fx = false, fy = false;
            switch (type)
            {
                case RotateFlipType.RotateNoneFlipNone: return;
                case RotateFlipType.RotateNoneFlipX: fx = true; break;
                case RotateFlipType.RotateNoneFlipY: fy = true; break;
                case RotateFlipType.RotateNoneFlipXY: fx = fy = true; break;
                default: throw new NotSupportedException("RotateFlip " + type + " is not supported.");
            }
            int bpp = GetPixelFormatSize(PixelFormat);
            if (bpp < 8 && fx) throw new NotSupportedException("Horizontal flip of sub-byte formats is not supported.");
            int bytesPer = bpp / 8;
            byte[] outp = new byte[Pixels.Length];
            for (int y = 0; y < Height; y++)
            {
                int srcRow = (fy ? Height - 1 - y : y) * Stride;
                int dstRow = y * Stride;
                if (!fx) { Buffer.BlockCopy(Pixels, srcRow, outp, dstRow, Stride); continue; }
                for (int x = 0; x < Width; x++)
                {
                    Buffer.BlockCopy(Pixels, srcRow + (Width - 1 - x) * bytesPer, outp, dstRow + x * bytesPer, bytesPer);
                }
            }
            Pixels = outp;
            Invalidate();
        }

        public override void Save(Stream stream, ImageFormat format)
        {
            // Encode straight from the unpremultiplied pixels so a save/load round trip is lossless.
            SKImageInfo info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            byte[] bgra = Pixels;
            int stride = Stride;
            if (PixelFormat != PixelFormat.Format32bppArgb)
            {
                stride = Width * 4;
                bgra = new byte[stride * Height];
                ConvertOut(new Rectangle(0, 0, Width, Height), PixelFormat.Format32bppArgb, bgra, stride);
            }
            GCHandle h = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                using (SKPixmap pm = new SKPixmap(info, h.AddrOfPinnedObject(), stride))
                using (SKData data = pm.Encode(format.Skia, 100))
                {
                    data.SaveTo(stream);
                }
            }
            finally { h.Free(); }
        }

        public override void Dispose() { Invalidate(); Pixels = null; }

        /// <summary>Premultiplied BGRA Skia image of the current pixels; cached until the bitmap changes.</summary>
        internal SKImage ToSkia()
        {
            if (skiaCache != null) return skiaCache;
            SKImageInfo info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            byte[] bgra = Pixels;
            int stride = Stride;
            if (PixelFormat != PixelFormat.Format32bppArgb)
            {
                stride = Width * 4;
                bgra = new byte[stride * Height];
                ConvertOut(new Rectangle(0, 0, Width, Height), PixelFormat.Format32bppArgb, bgra, stride);
            }
            GCHandle h = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                using (SKPixmap pm = new SKPixmap(info, h.AddrOfPinnedObject(), stride))
                using (SKImage raw = SKImage.FromPixels(pm))
                {
                    // Copy into Skia-owned premultiplied memory so the pinned buffer can be released.
                    SKImageInfo premul = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    SKBitmap bm = new SKBitmap(premul);
                    raw.ReadPixels(premul, bm.GetPixels(), bm.RowBytes, 0, 0);
                    skiaCache = SKImage.FromBitmap(bm);
                    bm.Dispose();
                }
            }
            finally { h.Free(); }
            return skiaCache;
        }

        // ----- format conversion -----

        private Color ReadPixel(int x, int y)
        {
            int row = y * Stride;
            switch (PixelFormat)
            {
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                    { int i = row + x * 4; return Color.FromArgb(Pixels[i + 3], Pixels[i + 2], Pixels[i + 1], Pixels[i]); }
                case PixelFormat.Format32bppRgb:
                    { int i = row + x * 4; return Color.FromArgb(255, Pixels[i + 2], Pixels[i + 1], Pixels[i]); }
                case PixelFormat.Format24bppRgb:
                    { int i = row + x * 3; return Color.FromArgb(255, Pixels[i + 2], Pixels[i + 1], Pixels[i]); }
                case PixelFormat.Format8bppIndexed:
                    return palette.Entries[Pixels[row + x]];
                case PixelFormat.Format4bppIndexed:
                    { byte b = Pixels[row + x / 2]; return palette.Entries[(x & 1) == 0 ? b >> 4 : b & 0xF]; }
                case PixelFormat.Format1bppIndexed:
                    { byte b = Pixels[row + x / 8]; return palette.Entries[(b >> (7 - (x & 7))) & 1]; }
                case PixelFormat.Format16bppGrayScale:
                    { int i = row + x * 2; int v = Pixels[i + 1]; return Color.FromArgb(255, v, v, v); }
                case PixelFormat.Format16bppRgb555:
                    { int v = Pixels[row + x * 2] | (Pixels[row + x * 2 + 1] << 8); return Color.FromArgb(255, Ex5((v >> 10) & 31), Ex5((v >> 5) & 31), Ex5(v & 31)); }
                case PixelFormat.Format16bppArgb1555:
                    { int v = Pixels[row + x * 2] | (Pixels[row + x * 2 + 1] << 8); return Color.FromArgb((v & 0x8000) != 0 ? 255 : 0, Ex5((v >> 10) & 31), Ex5((v >> 5) & 31), Ex5(v & 31)); }
                case PixelFormat.Format16bppRgb565:
                    { int v = Pixels[row + x * 2] | (Pixels[row + x * 2 + 1] << 8); return Color.FromArgb(255, Ex5((v >> 11) & 31), ((v >> 5) & 63) * 255 / 63, Ex5(v & 31)); }
                default: throw new NotSupportedException("Reading " + PixelFormat + " is not supported.");
            }
        }
        private static int Ex5(int v) => v * 255 / 31;

        private void WritePixel(byte[] buf, int stride, PixelFormat fmt, int x, int y, Color c)
        {
            int row = y * stride;
            switch (fmt)
            {
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                    { int i = row + x * 4; buf[i] = c.B; buf[i + 1] = c.G; buf[i + 2] = c.R; buf[i + 3] = c.A; return; }
                case PixelFormat.Format32bppRgb:
                    { int i = row + x * 4; buf[i] = c.B; buf[i + 1] = c.G; buf[i + 2] = c.R; buf[i + 3] = 255; return; }
                case PixelFormat.Format24bppRgb:
                    { int i = row + x * 3; buf[i] = c.B; buf[i + 1] = c.G; buf[i + 2] = c.R; return; }
                default: throw new NotSupportedException("Converting to " + fmt + " is not supported.");
            }
        }

        private void ConvertOut(Rectangle rect, PixelFormat fmt, byte[] buf, int stride)
        {
            if (fmt == PixelFormat)
            {
                int bpp = GetPixelFormatSize(fmt);
                if (bpp >= 8)
                {
                    int bytesPer = bpp / 8;
                    for (int y = 0; y < rect.Height; y++)
                        Buffer.BlockCopy(Pixels, (rect.Y + y) * Stride + rect.X * bytesPer, buf, y * stride, rect.Width * bytesPer);
                    return;
                }
                if (rect.X == 0)
                {
                    int rowBytes = (rect.Width * bpp + 7) / 8;
                    for (int y = 0; y < rect.Height; y++)
                        Buffer.BlockCopy(Pixels, (rect.Y + y) * Stride, buf, y * stride, rowBytes);
                    return;
                }
            }
            if (IsIndexedPixelFormat(fmt)) throw new NotSupportedException("Converting " + PixelFormat + " to " + fmt + " is not supported.");
            for (int y = 0; y < rect.Height; y++)
                for (int x = 0; x < rect.Width; x++)
                    WritePixel(buf, stride, fmt, x, y, ReadPixel(rect.X + x, rect.Y + y));
        }

        private void ConvertIn(Rectangle rect, PixelFormat fmt, byte[] buf, int stride)
        {
            if (fmt == PixelFormat)
            {
                int bpp = GetPixelFormatSize(fmt);
                int bytesPer = Math.Max(1, bpp / 8);
                int rowBytes = bpp >= 8 ? rect.Width * bytesPer : (rect.Width * bpp + 7) / 8;
                for (int y = 0; y < rect.Height; y++)
                    Buffer.BlockCopy(buf, y * stride, Pixels, (rect.Y + y) * Stride + rect.X * bytesPer, rowBytes);
                return;
            }
            if (IsIndexedPixelFormat(PixelFormat) || IsIndexedPixelFormat(fmt)) throw new NotSupportedException("Writing " + fmt + " into " + PixelFormat + " is not supported.");
            for (int y = 0; y < rect.Height; y++)
            {
                for (int x = 0; x < rect.Width; x++)
                {
                    Color c;
                    int i = y * stride;
                    switch (fmt)
                    {
                        case PixelFormat.Format32bppArgb: case PixelFormat.Format32bppPArgb: i += x * 4; c = Color.FromArgb(buf[i + 3], buf[i + 2], buf[i + 1], buf[i]); break;
                        case PixelFormat.Format32bppRgb: i += x * 4; c = Color.FromArgb(255, buf[i + 2], buf[i + 1], buf[i]); break;
                        case PixelFormat.Format24bppRgb: i += x * 3; c = Color.FromArgb(255, buf[i + 2], buf[i + 1], buf[i]); break;
                        default: throw new NotSupportedException("Writing " + fmt + " is not supported.");
                    }
                    WritePixel(Pixels, Stride, PixelFormat, rect.X + x, rect.Y + y, c);
                }
            }
        }
    }
}
