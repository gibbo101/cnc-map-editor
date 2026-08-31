using Avalonia;
using Avalonia.Platform;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MobiusEditor.Shell;

namespace MobiusEditor.App
{
    /// <summary>A palette row: the Shell's palette item plus its thumbnail converted for Avalonia.</summary>
    public sealed class PaletteEntry
    {
        public PaletteItem Item { get; }
        public string Label => Item.Label;
        public Avalonia.Media.Imaging.Bitmap Image { get; }
        public object Type => Item.Type;

        public PaletteEntry(PaletteItem item)
        {
            Item = item;
            Image = ToAvalonia(item.Thumbnail);
        }

        /// <summary>Copies a core bitmap (BGRA, unpremultiplied) into an Avalonia bitmap of the same layout.</summary>
        public static Avalonia.Media.Imaging.Bitmap ToAvalonia(System.Drawing.Bitmap source)
        {
            if (source == null) return null;
            Avalonia.Media.Imaging.WriteableBitmap wb = new Avalonia.Media.Imaging.WriteableBitmap(
                new PixelSize(source.Width, source.Height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);
            BitmapData data = source.LockBits(new System.Drawing.Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (ILockedFramebuffer fb = wb.Lock())
                {
                    int rowBytes = source.Width * 4;
                    byte[] row = new byte[rowBytes];
                    for (int y = 0; y < source.Height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, rowBytes);
                        Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, rowBytes);
                    }
                }
            }
            finally { source.UnlockBits(data); }
            return wb;
        }
    }
}
