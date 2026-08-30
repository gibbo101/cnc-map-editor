using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using SkiaSharp;

namespace System.Drawing
{
    public abstract class Brush : IDisposable
    {
        public abstract Color Color { get; }
        public void Dispose() { }
    }

    public sealed class SolidBrush : Brush
    {
        public override Color Color { get; }
        public SolidBrush(Color color) { Color = color; }
    }

    public sealed class Pen : IDisposable
    {
        public Color Color { get; set; }
        public float Width { get; set; }
        public DashStyle DashStyle { get; set; } = DashStyle.Solid;
        public float[] DashPattern { get; set; }
        public DashCap DashCap { get; set; } = DashCap.Flat;
        public LineCap StartCap { get; set; } = LineCap.Flat;
        public LineCap EndCap { get; set; } = LineCap.Flat;
        public Pen(Color color) : this(color, 1f) { }
        public Pen(Color color, float width) { Color = color; Width = width; }
        public Pen(Brush brush, float width) : this(brush.Color, width) { }
        public void Dispose() { }

        internal SKPathEffect DashEffect()
        {
            float[] pattern = DashPattern;
            if (pattern == null)
            {
                switch (DashStyle)
                {
                    case DashStyle.Dash: pattern = new[] { 3f, 1f }; break;
                    case DashStyle.Dot: pattern = new[] { 1f, 1f }; break;
                    case DashStyle.DashDot: pattern = new[] { 3f, 1f, 1f, 1f }; break;
                    case DashStyle.DashDotDot: pattern = new[] { 3f, 1f, 1f, 1f, 1f, 1f }; break;
                    default: return null;
                }
            }
            float w = Math.Max(1f, Width);
            return SKPathEffect.CreateDash(pattern.Select(p => p * w).ToArray(), 0);
        }
    }

    public sealed class Font : IDisposable
    {
        public string Name { get; }
        public float Size { get; }
        public FontStyle Style { get; }
        public GraphicsUnit Unit => GraphicsUnit.Point;
        public float SizeInPoints => Size;
        public bool Bold => (Style & FontStyle.Bold) != 0;
        public bool Italic => (Style & FontStyle.Italic) != 0;
        public Font(string name, float size) : this(name, size, FontStyle.Regular) { }
        public Font(string name, float size, FontStyle style) { Name = name; Size = size; Style = style; }
        public Font(Font prototype, FontStyle style) : this(prototype.Name, prototype.Size, style) { }
        public void Dispose() { }

        /// <summary>Pixel size at 96 dpi, as GDI+ resolves point sizes on a 96 dpi surface.</summary>
        internal float PixelSize => Size * 96f / 72f;

        internal SKTypeface Typeface()
        {
            SKFontStyle fs = new SKFontStyle(Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            return SKTypeface.FromFamilyName(Name, fs) ?? SKTypeface.FromFamilyName(null, fs);
        }
    }

    public static class SystemFonts
    {
        public static Font DefaultFont { get; } = new Font("DejaVu Sans", 8.25f);
    }

    public sealed class StringFormat : IDisposable
    {
        public StringAlignment Alignment { get; set; } = StringAlignment.Near;
        public StringAlignment LineAlignment { get; set; } = StringAlignment.Near;
        public StringFormatFlags FormatFlags { get; set; }
        public StringTrimming Trimming { get; set; } = StringTrimming.Character;
        public StringFormat() { }
        public StringFormat(StringFormatFlags flags) { FormatFlags = flags; }
        public static StringFormat GenericDefault => new StringFormat();
        public static StringFormat GenericTypographic => new StringFormat(StringFormatFlags.LineLimit | StringFormatFlags.NoClip);
        public void Dispose() { }
    }

    /// <summary>Serialised region: a list of non-overlapping horizontal spans.</summary>
    public sealed class RegionData
    {
        internal readonly (int Y, int X0, int X1)[] Spans;
        internal RegionData((int, int, int)[] spans) { Spans = spans; }
        public byte[] Data
        {
            get
            {
                byte[] d = new byte[Spans.Length * 12];
                for (int i = 0; i < Spans.Length; i++)
                {
                    BitConverter.GetBytes(Spans[i].Y).CopyTo(d, i * 12);
                    BitConverter.GetBytes(Spans[i].X0).CopyTo(d, i * 12 + 4);
                    BitConverter.GetBytes(Spans[i].X1).CopyTo(d, i * 12 + 8);
                }
                return d;
            }
        }
    }

    /// <summary>Pixel-aligned region as a set of horizontal spans, exclusive of X1.</summary>
    public sealed class Region : IDisposable
    {
        private SortedDictionary<int, List<(int X0, int X1)>> rows = new SortedDictionary<int, List<(int, int)>>();

        public Region() { MakeInfinite(); }
        public Region(Rectangle rect) { Union(rect); }
        public Region(RegionData data)
        {
            foreach ((int y, int x0, int x1) in data.Spans) AddSpan(y, x0, x1);
        }

        private bool infinite;
        public void MakeEmpty() { rows.Clear(); infinite = false; }
        public void MakeInfinite() { rows.Clear(); infinite = true; }
        public bool IsEmpty(Graphics g) => !infinite && rows.Count == 0;

        private void AddSpan(int y, int x0, int x1)
        {
            if (x1 <= x0) return;
            if (!rows.TryGetValue(y, out List<(int X0, int X1)> spans)) { spans = new List<(int, int)>(); rows[y] = spans; }
            List<(int, int)> merged = new List<(int, int)>();
            foreach ((int a, int b) in spans)
            {
                if (b < x0 || a > x1) { merged.Add((a, b)); continue; }
                x0 = Math.Min(x0, a); x1 = Math.Max(x1, b);
            }
            merged.Add((x0, x1));
            merged.Sort();
            rows[y] = merged;
        }

        public void Union(Rectangle rect)
        {
            if (infinite) return;
            for (int y = rect.Top; y < rect.Bottom; y++) AddSpan(y, rect.Left, rect.Right);
        }

        public void Union(Region other)
        {
            if (other.infinite) { MakeInfinite(); return; }
            foreach (KeyValuePair<int, List<(int X0, int X1)>> row in other.rows)
                foreach ((int a, int b) in row.Value) AddSpan(row.Key, a, b);
        }

        public void Intersect(Rectangle rect) => Intersect(new Region(rect));

        public void Intersect(Region other)
        {
            if (other.infinite) return;
            if (infinite) { rows = Clone(other.rows); infinite = false; return; }
            SortedDictionary<int, List<(int X0, int X1)>> result = new SortedDictionary<int, List<(int, int)>>();
            foreach (KeyValuePair<int, List<(int X0, int X1)>> row in rows)
            {
                if (!other.rows.TryGetValue(row.Key, out List<(int X0, int X1)> theirs)) continue;
                List<(int, int)> outSpans = new List<(int, int)>();
                foreach ((int a, int b) in row.Value)
                    foreach ((int c, int d) in theirs)
                    {
                        int x0 = Math.Max(a, c), x1 = Math.Min(b, d);
                        if (x1 > x0) outSpans.Add((x0, x1));
                    }
                if (outSpans.Count > 0) { outSpans.Sort(); result[row.Key] = outSpans; }
            }
            rows = result;
        }

        public void Exclude(Rectangle rect)
        {
            if (infinite) throw new NotSupportedException("Exclude from an infinite region is not supported.");
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                if (!rows.TryGetValue(y, out List<(int X0, int X1)> spans)) continue;
                List<(int, int)> outSpans = new List<(int, int)>();
                foreach ((int a, int b) in spans)
                {
                    if (b <= rect.Left || a >= rect.Right) { outSpans.Add((a, b)); continue; }
                    if (a < rect.Left) outSpans.Add((a, rect.Left));
                    if (b > rect.Right) outSpans.Add((rect.Right, b));
                }
                if (outSpans.Count == 0) rows.Remove(y); else rows[y] = outSpans;
            }
        }

        public void Translate(int dx, int dy)
        {
            if (infinite) return;
            SortedDictionary<int, List<(int X0, int X1)>> moved = new SortedDictionary<int, List<(int, int)>>();
            foreach (KeyValuePair<int, List<(int X0, int X1)>> row in rows)
                moved[row.Key + dy] = row.Value.Select(s => (s.X0 + dx, s.X1 + dx)).ToList();
            rows = moved;
        }

        public RegionData GetRegionData()
        {
            List<(int, int, int)> spans = new List<(int, int, int)>();
            foreach (KeyValuePair<int, List<(int X0, int X1)>> row in rows)
                foreach ((int a, int b) in row.Value) spans.Add((row.Key, a, b));
            return new RegionData(spans.ToArray());
        }

        public RectangleF GetBounds(Graphics g)
        {
            if (rows.Count == 0) return RectangleF.Empty;
            int minX = int.MaxValue, maxX = int.MinValue;
            foreach (List<(int X0, int X1)> spans in rows.Values) { minX = Math.Min(minX, spans[0].X0); maxX = Math.Max(maxX, spans[spans.Count - 1].X1); }
            return new RectangleF(minX, rows.Keys.First(), maxX - minX, rows.Keys.Last() - rows.Keys.First() + 1);
        }

        public bool IsVisible(Point p) => infinite || (rows.TryGetValue(p.Y, out List<(int X0, int X1)> s) && s.Any(sp => p.X >= sp.X0 && p.X < sp.X1));

        internal IEnumerable<SKRectI> Rects()
        {
            foreach (KeyValuePair<int, List<(int X0, int X1)>> row in rows)
                foreach ((int a, int b) in row.Value) yield return new SKRectI(a, row.Key, b, row.Key + 1);
        }
        internal bool IsInfinite => infinite;

        private static SortedDictionary<int, List<(int X0, int X1)>> Clone(SortedDictionary<int, List<(int X0, int X1)>> src)
        {
            SortedDictionary<int, List<(int, int)>> d = new SortedDictionary<int, List<(int, int)>>();
            foreach (KeyValuePair<int, List<(int X0, int X1)>> kv in src) d[kv.Key] = new List<(int, int)>(kv.Value);
            return d;
        }

        public Region Clone() { Region r = new Region(); r.infinite = infinite; r.rows = Clone(rows); return r; }
        public void Dispose() { }
    }
}
