using SkiaSharp;

namespace System.Drawing.Drawing2D
{
    public sealed class Matrix : IDisposable
    {
        internal SKMatrix Skia = SKMatrix.CreateIdentity();
        public Matrix() { }
        public void Scale(float sx, float sy) => Skia = Skia.PreConcat(SKMatrix.CreateScale(sx, sy));
        public void Translate(float dx, float dy) => Skia = Skia.PreConcat(SKMatrix.CreateTranslation(dx, dy));
        public void Rotate(float degrees) => Skia = Skia.PreConcat(SKMatrix.CreateRotationDegrees(degrees));
        public void Reset() => Skia = SKMatrix.CreateIdentity();
        public bool IsIdentity => Skia.IsIdentity;
        public void Dispose() { }
    }
}
