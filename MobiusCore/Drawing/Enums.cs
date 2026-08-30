// Skia-backed replacement for the parts of System.Drawing the editor core uses.
// Types live in the System.Drawing namespaces so the core compiles unchanged; only the
// surface the core actually calls is implemented.
using System;

namespace System.Drawing
{
    public enum GraphicsUnit { World, Display, Pixel, Point, Inch, Document, Millimeter }
    [Flags] public enum FontStyle { Regular = 0, Bold = 1, Italic = 2, Underline = 4, Strikeout = 8 }
    public enum StringAlignment { Near, Center, Far }
    public enum StringTrimming { None, Character, Word, EllipsisCharacter, EllipsisWord, EllipsisPath }
    [Flags] public enum StringFormatFlags { DirectionRightToLeft = 1, DirectionVertical = 2, FitBlackBox = 4, DisplayFormatControl = 0x20, NoFontFallback = 0x400, MeasureTrailingSpaces = 0x800, NoWrap = 0x1000, LineLimit = 0x2000, NoClip = 0x4000 }
    public enum RotateFlipType { RotateNoneFlipNone = 0, Rotate90FlipNone = 1, Rotate180FlipNone = 2, Rotate270FlipNone = 3, RotateNoneFlipX = 4, Rotate90FlipX = 5, Rotate180FlipX = 6, Rotate270FlipX = 7, RotateNoneFlipXY = 2, RotateNoneFlipY = 6, Rotate90FlipY = 7, Rotate180FlipXY = 0, Rotate180FlipY = 4, Rotate270FlipXY = 1, Rotate270FlipY = 5, Rotate90FlipXY = 3 }
}

namespace System.Drawing.Imaging
{
    public enum PixelFormat
    {
        Indexed = 0x00010000, Gdi = 0x00020000, Alpha = 0x00040000, PAlpha = 0x00080000,
        Extended = 0x00100000, Canonical = 0x00200000, Undefined = 0, DontCare = 0,
        Format1bppIndexed = 0x00030101, Format4bppIndexed = 0x00030402, Format8bppIndexed = 0x00030803,
        Format16bppGrayScale = 0x00101004, Format16bppRgb555 = 0x00021005, Format16bppRgb565 = 0x00021006,
        Format16bppArgb1555 = 0x00061007, Format24bppRgb = 0x00021808, Format32bppRgb = 0x00022009,
        Format32bppArgb = 0x0026200A, Format32bppPArgb = 0x000E200B, Format48bppRgb = 0x0010300C,
        Format64bppArgb = 0x0034400D, Format64bppPArgb = 0x001A400E, Max = 0x0F,
    }
    public enum ImageLockMode { ReadOnly = 1, WriteOnly = 2, ReadWrite = 3, UserInputBuffer = 4 }
    public enum ColorMatrixFlag { Default, SkipGrays, AltGrays }
    public enum ColorAdjustType { Default, Bitmap, Brush, Pen, Text, Count, Any }
}

namespace System.Drawing.Drawing2D
{
    public enum SmoothingMode { Invalid = -1, Default, HighSpeed, HighQuality, None, AntiAlias }
    public enum PixelOffsetMode { Invalid = -1, Default, HighSpeed, HighQuality, None, Half }
    public enum InterpolationMode { Invalid = -1, Default, Low, High, Bilinear, Bicubic, NearestNeighbor, HighQualityBilinear, HighQualityBicubic }
    public enum CompositingQuality { Invalid = -1, Default, HighSpeed, HighQuality, GammaCorrected, AssumeLinear }
    public enum CompositingMode { SourceOver, SourceCopy }
    public enum DashStyle { Solid, Dash, Dot, DashDot, DashDotDot, Custom }
    public enum DashCap { Flat = 0, Round = 2, Triangle = 3 }
    public enum LineCap { Flat, Square, Round, Triangle, NoAnchor = 0x10, SquareAnchor, RoundAnchor, DiamondAnchor, ArrowAnchor, AnchorMask = 0xF0, Custom = 0xFF }
    public enum WrapMode { Tile, TileFlipX, TileFlipY, TileFlipXY, Clamp }
}

namespace System.Drawing.Text
{
    public enum TextRenderingHint { SystemDefault, SingleBitPerPixelGridFit, SingleBitPerPixel, AntiAliasGridFit, AntiAlias, ClearTypeGridFit }
}
