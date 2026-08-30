using System;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    /// <summary>
    /// Draws onto a Bitmap through a Skia surface. The surface holds a premultiplied copy of the
    /// target; Flush/Dispose write it back in GDI+ layout.
    /// </summary>
    public sealed class Graphics : IDisposable
    {
        private readonly Bitmap target;
        private readonly SKBitmap work;
        private readonly SKSurface surface;
        private readonly SKCanvas canvas;
        private Matrix transform = new Matrix();

        public InterpolationMode InterpolationMode { get; set; } = InterpolationMode.Default;
        public SmoothingMode SmoothingMode { get; set; } = SmoothingMode.Default;
        public PixelOffsetMode PixelOffsetMode { get; set; } = PixelOffsetMode.Default;
        public CompositingQuality CompositingQuality { get; set; } = CompositingQuality.Default;
        public CompositingMode CompositingMode { get; set; } = CompositingMode.SourceOver;
        public TextRenderingHint TextRenderingHint { get; set; } = TextRenderingHint.SystemDefault;
        public float DpiX => target.HorizontalResolution;
        public float DpiY => target.VerticalResolution;

        private Graphics(Bitmap bitmap)
        {
            target = bitmap;
            SKImageInfo premul = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            work = new SKBitmap(premul);
            SKImage current = bitmap.ToSkia();
            current.ReadPixels(premul, work.GetPixels(), work.RowBytes, 0, 0);
            surface = SKSurface.Create(premul, work.GetPixels(), work.RowBytes);
            canvas = surface.Canvas;
        }

        public static Graphics FromImage(Image image)
        {
            if (!(image is Bitmap bm)) throw new ArgumentException("Only bitmaps can be drawn on.");
            if (Image.IsIndexedPixelFormat(bm.PixelFormat)) throw new Exception("A Graphics object cannot be created from an image that has an indexed pixel format.");
            return new Graphics(bm);
        }

        public Matrix Transform
        {
            get => transform;
            set { transform = value; canvas.SetMatrix(value.Skia); }
        }
        public void ResetTransform() => Transform = new Matrix();
        public void TranslateTransform(float dx, float dy) { transform.Translate(dx, dy); canvas.SetMatrix(transform.Skia); }
        public void ScaleTransform(float sx, float sy) { transform.Scale(sx, sy); canvas.SetMatrix(transform.Skia); }

        public void Flush()
        {
            canvas.Flush();
            SKImageInfo unpremul = new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            GCHandle h = GCHandle.Alloc(target.Pixels, GCHandleType.Pinned);
            try
            {
                if (target.PixelFormat == PixelFormat.Format32bppArgb || target.PixelFormat == PixelFormat.Format32bppPArgb)
                {
                    surface.ReadPixels(unpremul, h.AddrOfPinnedObject(), target.Stride, 0, 0);
                }
                else
                {
                    byte[] tmp = new byte[target.Width * 4 * target.Height];
                    GCHandle th = GCHandle.Alloc(tmp, GCHandleType.Pinned);
                    try { surface.ReadPixels(unpremul, th.AddrOfPinnedObject(), target.Width * 4, 0, 0); }
                    finally { th.Free(); }
                    BitmapData d = target.LockBits(new Rectangle(0, 0, target.Width, target.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try { Marshal.Copy(tmp, 0, d.Scan0, tmp.Length); }
                    finally { target.UnlockBits(d); }
                }
            }
            finally { h.Free(); }
            target.Invalidate();
        }

        public void Dispose()
        {
            Flush();
            surface.Dispose();
            work.Dispose();
        }

        // ----- paint helpers -----

        private bool AntiAlias => SmoothingMode == SmoothingMode.AntiAlias || SmoothingMode == SmoothingMode.HighQuality;
        private float StrokeOffset => (PixelOffsetMode == PixelOffsetMode.Half || PixelOffsetMode == PixelOffsetMode.HighQuality) ? 0f : 0.5f;

        private SKPaint FillPaint(Brush brush)
        {
            return new SKPaint { Color = ToSk(brush.Color), IsAntialias = AntiAlias, Style = SKPaintStyle.Fill, BlendMode = Blend };
        }

        private SKPaint StrokePaint(Pen pen)
        {
            SKPaint p = new SKPaint { Color = ToSk(pen.Color), IsAntialias = AntiAlias, Style = SKPaintStyle.Stroke, StrokeWidth = pen.Width, BlendMode = Blend };
            SKPathEffect dash = pen.DashEffect();
            if (dash != null) p.PathEffect = dash;
            return p;
        }

        private SKBlendMode Blend => CompositingMode == CompositingMode.SourceCopy ? SKBlendMode.Src : SKBlendMode.SrcOver;
        private static SKColor ToSk(Color c) => new SKColor(c.R, c.G, c.B, c.A);

        private SKSamplingOptions Sampling
        {
            get
            {
                switch (InterpolationMode)
                {
                    case InterpolationMode.NearestNeighbor: return new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                    case InterpolationMode.HighQualityBicubic: case InterpolationMode.Bicubic: case InterpolationMode.High: case InterpolationMode.HighQualityBilinear: return new SKSamplingOptions(SKCubicResampler.Mitchell);
                    default: return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
                }
            }
        }

        // ----- images -----

        public void DrawImage(Image image, int x, int y) => DrawImage(image, new Rectangle(x, y, image.Width, image.Height), new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel, null);
        public void DrawImage(Image image, float x, float y) => DrawImage(image, new RectangleF(x, y, image.Width, image.Height), new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        public void DrawImage(Image image, Point p) => DrawImage(image, p.X, p.Y);
        public void DrawImage(Image image, PointF p) => DrawImage(image, p.X, p.Y);
        public void DrawImage(Image image, Rectangle dest) => DrawImage(image, dest, new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        public void DrawImage(Image image, RectangleF dest) => DrawImage(image, dest, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        public void DrawImage(Image image, int x, int y, int w, int h) => DrawImage(image, new Rectangle(x, y, w, h));
        public void DrawImage(Image image, Rectangle dest, Rectangle src, GraphicsUnit unit) => DrawImage(image, dest, src.X, src.Y, src.Width, src.Height, unit, null);
        public void DrawImage(Image image, Rectangle dest, Rectangle src, GraphicsUnit unit, ImageAttributes attr) => DrawImage(image, dest, src.X, src.Y, src.Width, src.Height, unit, attr);
        public void DrawImage(Image image, Rectangle dest, int sx, int sy, int sw, int sh, GraphicsUnit unit) => DrawImage(image, dest, sx, sy, sw, sh, unit, null);
        public void DrawImage(Image image, Rectangle dest, int sx, int sy, int sw, int sh, GraphicsUnit unit, ImageAttributes attr)
            => Draw(image, new SKRect(sx, sy, sx + sw, sy + sh), new SKRect(dest.X, dest.Y, dest.Right, dest.Bottom), attr);
        public void DrawImage(Image image, Rectangle dest, float sx, float sy, float sw, float sh, GraphicsUnit unit, ImageAttributes attr)
            => Draw(image, new SKRect(sx, sy, sx + sw, sy + sh), new SKRect(dest.X, dest.Y, dest.Right, dest.Bottom), attr);
        public void DrawImage(Image image, RectangleF dest, RectangleF src, GraphicsUnit unit)
            => Draw(image, new SKRect(src.X, src.Y, src.Right, src.Bottom), new SKRect(dest.X, dest.Y, dest.Right, dest.Bottom), null);

        private void Draw(Image image, SKRect src, SKRect dest, ImageAttributes attr)
        {
            if (!(image is Bitmap bm)) throw new ArgumentException("Unsupported image type.");
            if (ReferenceEquals(bm, target)) throw new InvalidOperationException("Cannot draw a bitmap onto itself.");
            using (SKPaint paint = new SKPaint { IsAntialias = false, BlendMode = Blend })
            {
                if (attr?.Matrix != null) paint.ColorFilter = SKColorFilter.CreateColorMatrix(attr.Matrix.ToSkia());
                canvas.DrawImage(bm.ToSkia(), src, dest, Sampling, paint);
            }
        }

        // ----- shapes -----

        public void Clear(Color color) => canvas.Clear(ToSk(color));

        public void FillRectangle(Brush brush, Rectangle r) => FillRectangle(brush, (float)r.X, r.Y, r.Width, r.Height);
        public void FillRectangle(Brush brush, RectangleF r) => FillRectangle(brush, r.X, r.Y, r.Width, r.Height);
        public void FillRectangle(Brush brush, int x, int y, int w, int h) => FillRectangle(brush, (float)x, y, w, h);
        public void FillRectangle(Brush brush, float x, float y, float w, float h)
        {
            using (SKPaint p = FillPaint(brush)) canvas.DrawRect(x, y, w, h, p);
        }

        public void FillRectangles(Brush brush, Rectangle[] rects) { foreach (Rectangle r in rects) FillRectangle(brush, r); }
        public void FillRectangles(Brush brush, RectangleF[] rects) { foreach (RectangleF r in rects) FillRectangle(brush, r); }

        public void DrawRectangle(Pen pen, Rectangle r) => DrawRectangle(pen, (float)r.X, r.Y, r.Width, r.Height);
        public void DrawRectangle(Pen pen, RectangleF r) => DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        public void DrawRectangle(Pen pen, int x, int y, int w, int h) => DrawRectangle(pen, (float)x, y, w, h);
        public void DrawRectangle(Pen pen, float x, float y, float w, float h)
        {
            float o = StrokeOffset;
            using (SKPaint p = StrokePaint(pen)) canvas.DrawRect(x + o, y + o, w, h, p);
        }
        public void DrawRectangles(Pen pen, Rectangle[] rects) { foreach (Rectangle r in rects) DrawRectangle(pen, r); }

        public void DrawLine(Pen pen, Point a, Point b) => DrawLine(pen, (float)a.X, a.Y, b.X, b.Y);
        public void DrawLine(Pen pen, PointF a, PointF b) => DrawLine(pen, a.X, a.Y, b.X, b.Y);
        public void DrawLine(Pen pen, int x1, int y1, int x2, int y2) => DrawLine(pen, (float)x1, y1, x2, y2);
        public void DrawLine(Pen pen, float x1, float y1, float x2, float y2)
        {
            float o = StrokeOffset;
            using (SKPaint p = StrokePaint(pen)) canvas.DrawLine(x1 + o, y1 + o, x2 + o, y2 + o, p);
        }
        public void DrawLines(Pen pen, Point[] pts) { for (int i = 1; i < pts.Length; i++) DrawLine(pen, pts[i - 1], pts[i]); }
        public void DrawLines(Pen pen, PointF[] pts) { for (int i = 1; i < pts.Length; i++) DrawLine(pen, pts[i - 1], pts[i]); }

        public void DrawPolygon(Pen pen, Point[] pts) => DrawPolygon(pen, pts.Select(p => new PointF(p.X, p.Y)).ToArray());
        public void DrawPolygon(Pen pen, PointF[] pts)
        {
            float o = StrokeOffset;
            using (SKPath path = new SKPath())
            using (SKPaint p = StrokePaint(pen))
            {
                path.MoveTo(pts[0].X + o, pts[0].Y + o);
                for (int i = 1; i < pts.Length; i++) path.LineTo(pts[i].X + o, pts[i].Y + o);
                path.Close();
                canvas.DrawPath(path, p);
            }
        }
        public void FillPolygon(Brush brush, Point[] pts) => FillPolygon(brush, pts.Select(p => new PointF(p.X, p.Y)).ToArray());
        public void FillPolygon(Brush brush, PointF[] pts)
        {
            using (SKPath path = new SKPath())
            using (SKPaint p = FillPaint(brush))
            {
                path.MoveTo(pts[0].X, pts[0].Y);
                for (int i = 1; i < pts.Length; i++) path.LineTo(pts[i].X, pts[i].Y);
                path.Close();
                canvas.DrawPath(path, p);
            }
        }

        public void DrawEllipse(Pen pen, Rectangle r) => DrawEllipse(pen, (float)r.X, r.Y, r.Width, r.Height);
        public void DrawEllipse(Pen pen, RectangleF r) => DrawEllipse(pen, r.X, r.Y, r.Width, r.Height);
        public void DrawEllipse(Pen pen, int x, int y, int w, int h) => DrawEllipse(pen, (float)x, y, w, h);
        public void DrawEllipse(Pen pen, float x, float y, float w, float h)
        {
            float o = StrokeOffset;
            using (SKPaint p = StrokePaint(pen)) canvas.DrawOval(new SKRect(x + o, y + o, x + w + o, y + h + o), p);
        }
        public void FillEllipse(Brush brush, Rectangle r) => FillEllipse(brush, (float)r.X, r.Y, r.Width, r.Height);
        public void FillEllipse(Brush brush, RectangleF r) => FillEllipse(brush, r.X, r.Y, r.Width, r.Height);
        public void FillEllipse(Brush brush, int x, int y, int w, int h) => FillEllipse(brush, (float)x, y, w, h);
        public void FillEllipse(Brush brush, float x, float y, float w, float h)
        {
            using (SKPaint p = FillPaint(brush)) canvas.DrawOval(new SKRect(x, y, x + w, y + h), p);
        }

        public void DrawArc(Pen pen, Rectangle r, float start, float sweep) => DrawArc(pen, (float)r.X, r.Y, r.Width, r.Height, start, sweep);
        public void DrawArc(Pen pen, RectangleF r, float start, float sweep) => DrawArc(pen, r.X, r.Y, r.Width, r.Height, start, sweep);
        public void DrawArc(Pen pen, float x, float y, float w, float h, float start, float sweep)
        {
            float o = StrokeOffset;
            using (SKPath path = new SKPath())
            using (SKPaint p = StrokePaint(pen))
            {
                path.AddArc(new SKRect(x + o, y + o, x + w + o, y + h + o), start, sweep);
                canvas.DrawPath(path, p);
            }
        }

        public void FillRegion(Brush brush, Region region)
        {
            using (SKPaint p = FillPaint(brush))
            {
                p.IsAntialias = false;
                if (region.IsInfinite) { canvas.DrawPaint(p); return; }
                foreach (SKRectI r in region.Rects()) canvas.DrawRect(r, p);
            }
        }

        // ----- text -----

        private static (SKFont font, float lineHeight) MakeFont(Font font)
        {
            SKFont f = new SKFont(font.Typeface(), font.PixelSize) { Subpixel = false, Edging = SKFontEdging.Antialias };
            SKFontMetrics m = f.Metrics;
            return (f, m.Descent - m.Ascent + m.Leading);
        }

        /// <summary>GDI+ pads measured strings by roughly a sixth of an em on each side.</summary>
        private static float Padding(Font font) => font.PixelSize / 6f;

        public SizeF MeasureString(string text, Font font) => MeasureString(text, font, new SizeF(float.MaxValue, float.MaxValue), null, out _, out _);
        public SizeF MeasureString(string text, Font font, int width) => MeasureString(text, font, new SizeF(width, float.MaxValue), null, out _, out _);
        public SizeF MeasureString(string text, Font font, int width, StringFormat format) => MeasureString(text, font, new SizeF(width, float.MaxValue), format, out _, out _);
        public SizeF MeasureString(string text, Font font, SizeF area) => MeasureString(text, font, area, null, out _, out _);
        public SizeF MeasureString(string text, Font font, SizeF area, StringFormat format) => MeasureString(text, font, area, format, out _, out _);
        public SizeF MeasureString(string text, Font font, SizeF area, StringFormat format, out int charsFitted, out int linesFilled)
        {
            charsFitted = text?.Length ?? 0;
            if (string.IsNullOrEmpty(text)) { linesFilled = 0; return SizeF.Empty; }
            (SKFont f, float lineHeight) = MakeFont(font);
            using (f)
            {
                string[] lines = Wrap(text, f, area.Width - 2 * Padding(font), format);
                linesFilled = lines.Length;
                float maxW = lines.Max(l => f.MeasureText(l, (SKPaint)null));
                return new SizeF(maxW + 2 * Padding(font), lineHeight * lines.Length);
            }
        }

        private static string[] Wrap(string text, SKFont f, float maxWidth, StringFormat format)
        {
            bool noWrap = format != null && (format.FormatFlags & StringFormatFlags.NoWrap) != 0;
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
            foreach (string para in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (noWrap || maxWidth <= 0 || f.MeasureText(para, (SKPaint)null) <= maxWidth) { lines.Add(para); continue; }
                string current = "";
                foreach (string word in para.Split(' '))
                {
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (f.MeasureText(candidate, (SKPaint)null) <= maxWidth || current.Length == 0) current = candidate;
                    else { lines.Add(current); current = word; }
                }
                lines.Add(current);
            }
            return lines.ToArray();
        }

        public void DrawString(string text, Font font, Brush brush, float x, float y) => DrawString(text, font, brush, new RectangleF(x, y, float.MaxValue / 4, float.MaxValue / 4), null);
        public void DrawString(string text, Font font, Brush brush, PointF p) => DrawString(text, font, brush, p.X, p.Y);
        public void DrawString(string text, Font font, Brush brush, PointF p, StringFormat format) => DrawString(text, font, brush, new RectangleF(p.X, p.Y, float.MaxValue / 4, float.MaxValue / 4), format);
        public void DrawString(string text, Font font, Brush brush, RectangleF area) => DrawString(text, font, brush, area, null);
        public void DrawString(string text, Font font, Brush brush, RectangleF area, StringFormat format)
        {
            if (string.IsNullOrEmpty(text)) return;
            (SKFont f, float lineHeight) = MakeFont(font);
            using (f)
            using (SKPaint p = new SKPaint { Color = ToSk(brush.Color), IsAntialias = TextRenderingHint != TextRenderingHint.SingleBitPerPixel && TextRenderingHint != TextRenderingHint.SingleBitPerPixelGridFit, BlendMode = Blend })
            {
                float pad = Padding(font);
                string[] lines = Wrap(text, f, area.Width - 2 * pad, format);
                float totalH = lineHeight * lines.Length;
                StringAlignment h = format?.Alignment ?? StringAlignment.Near;
                StringAlignment v = format?.LineAlignment ?? StringAlignment.Near;
                float top = area.Y;
                if (v == StringAlignment.Center) top += (area.Height - totalH) / 2f;
                else if (v == StringAlignment.Far) top += area.Height - totalH;
                float ascent = -f.Metrics.Ascent;
                for (int i = 0; i < lines.Length; i++)
                {
                    float w = f.MeasureText(lines[i], (SKPaint)null);
                    float left = area.X + pad;
                    if (h == StringAlignment.Center) left = area.X + (area.Width - w) / 2f;
                    else if (h == StringAlignment.Far) left = area.Right - pad - w;
                    canvas.DrawText(lines[i], left, top + i * lineHeight + ascent, f, p);
                }
            }
        }
    }
}
