using System;
using System.Drawing.Drawing2D;
using SkiaSharp;

namespace System.Drawing.Imaging
{
    public sealed class ColorPalette
    {
        public Color[] Entries { get; }
        public int Flags => 0;
        internal ColorPalette(int count) { Entries = new Color[count]; }
        internal ColorPalette(Color[] entries) { Entries = entries; }
    }

    public sealed class BitmapData
    {
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public int Stride { get; internal set; }
        public PixelFormat PixelFormat { get; internal set; }
        public IntPtr Scan0 { get; internal set; }
        internal byte[] Buffer;
        internal Runtime.InteropServices.GCHandle Handle;
        internal bool IsConverted;
        internal ImageLockMode Mode;
        internal Rectangle Area;
    }

    public sealed class ImageFormat
    {
        public static readonly ImageFormat Png = new ImageFormat(SKEncodedImageFormat.Png);
        public static readonly ImageFormat Bmp = new ImageFormat(SKEncodedImageFormat.Bmp);
        public static readonly ImageFormat Jpeg = new ImageFormat(SKEncodedImageFormat.Jpeg);
        public static readonly ImageFormat Gif = new ImageFormat(SKEncodedImageFormat.Gif);
        internal SKEncodedImageFormat Skia { get; }
        private ImageFormat(SKEncodedImageFormat skia) { Skia = skia; }
    }

    /// <summary>GDI+ 5x5 colour matrix: [R G B A 1] row vector times the matrix.</summary>
    public sealed class ColorMatrix
    {
        private readonly float[] m = new float[25];
        public ColorMatrix() { for (int i = 0; i < 5; i++) m[i * 5 + i] = 1f; }
        public ColorMatrix(float[][] rows) { for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) m[r * 5 + c] = rows[r][c]; }
        public float this[int row, int col] { get => m[row * 5 + col]; set => m[row * 5 + col] = value; }
        public float Matrix00 { get => this[0, 0]; set => this[0, 0] = value; }
        public float Matrix01 { get => this[0, 1]; set => this[0, 1] = value; }
        public float Matrix02 { get => this[0, 2]; set => this[0, 2] = value; }
        public float Matrix03 { get => this[0, 3]; set => this[0, 3] = value; }
        public float Matrix04 { get => this[0, 4]; set => this[0, 4] = value; }
        public float Matrix10 { get => this[1, 0]; set => this[1, 0] = value; }
        public float Matrix11 { get => this[1, 1]; set => this[1, 1] = value; }
        public float Matrix12 { get => this[1, 2]; set => this[1, 2] = value; }
        public float Matrix13 { get => this[1, 3]; set => this[1, 3] = value; }
        public float Matrix14 { get => this[1, 4]; set => this[1, 4] = value; }
        public float Matrix20 { get => this[2, 0]; set => this[2, 0] = value; }
        public float Matrix21 { get => this[2, 1]; set => this[2, 1] = value; }
        public float Matrix22 { get => this[2, 2]; set => this[2, 2] = value; }
        public float Matrix23 { get => this[2, 3]; set => this[2, 3] = value; }
        public float Matrix24 { get => this[2, 4]; set => this[2, 4] = value; }
        public float Matrix30 { get => this[3, 0]; set => this[3, 0] = value; }
        public float Matrix31 { get => this[3, 1]; set => this[3, 1] = value; }
        public float Matrix32 { get => this[3, 2]; set => this[3, 2] = value; }
        public float Matrix33 { get => this[3, 3]; set => this[3, 3] = value; }
        public float Matrix34 { get => this[3, 4]; set => this[3, 4] = value; }
        public float Matrix40 { get => this[4, 0]; set => this[4, 0] = value; }
        public float Matrix41 { get => this[4, 1]; set => this[4, 1] = value; }
        public float Matrix42 { get => this[4, 2]; set => this[4, 2] = value; }
        public float Matrix43 { get => this[4, 3]; set => this[4, 3] = value; }
        public float Matrix44 { get => this[4, 4]; set => this[4, 4] = value; }

        /// <summary>Skia's 4x5 row-major matrix: output channel per row, translate in 0..255.</summary>
        internal float[] ToSkia()
        {
            float[] s = new float[20];
            for (int outCh = 0; outCh < 4; outCh++)
            {
                for (int inCh = 0; inCh < 4; inCh++) s[outCh * 5 + inCh] = this[inCh, outCh];
                s[outCh * 5 + 4] = this[4, outCh] * 255f;
            }
            return s;
        }
    }

    public sealed class ImageAttributes : IDisposable
    {
        internal ColorMatrix Matrix { get; private set; }
        internal WrapMode WrapMode { get; private set; } = WrapMode.Clamp;
        public void SetColorMatrix(ColorMatrix matrix) => Matrix = matrix;
        public void SetColorMatrix(ColorMatrix matrix, ColorMatrixFlag flag) => Matrix = matrix;
        public void SetColorMatrix(ColorMatrix matrix, ColorMatrixFlag flag, ColorAdjustType type) => Matrix = matrix;
        public void ClearColorMatrix() => Matrix = null;
        public void SetWrapMode(WrapMode mode) => WrapMode = mode;
        public void Dispose() { }
    }
}
