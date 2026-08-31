//
// What a palette shows for a placeable type: the friendly display name (the tile code
// with its size for templates, which have no real names), the preview thumbnail the type
// classes render during theater init, and the footprint in cells so a placement ghost can
// size itself at the cursor.
using System.Drawing;
using MobiusEditor.Interface;
using MobiusEditor.Model;

namespace MobiusEditor.Shell
{
    public sealed class PaletteItem
    {
        public object Type { get; }
        public string Label { get; }
        public Bitmap Thumbnail { get; }
        public Size FootprintCells { get; }

        private PaletteItem(object type, string label, Bitmap thumbnail, Size footprint)
        {
            Type = type;
            Label = label;
            Thumbnail = thumbnail;
            FootprintCells = footprint;
        }

        public static PaletteItem From(object type)
        {
            switch (type)
            {
                case TemplateType t:
                    string label = t.IconWidth > 1 || t.IconHeight > 1 ? $"{t.Name} ({t.IconWidth}×{t.IconHeight})" : t.Name;
                    return new PaletteItem(t, label, t.Thumbnail, new Size(t.IconWidth, t.IconHeight));
                case BuildingType b:
                    return new PaletteItem(b, b.DisplayName, b.Thumbnail, b.Size);
                case TerrainType tt:
                    return new PaletteItem(tt, tt.DisplayName, tt.Thumbnail, tt.Size);
                case SmudgeType s:
                    return new PaletteItem(s, s.DisplayName, s.Thumbnail, s.Size);
                case IBrowsableType browsable:
                    // Overlay, units and infantry: single-cell.
                    return new PaletteItem(type, browsable.DisplayName, browsable.Thumbnail, new Size(1, 1));
                default:
                    return new PaletteItem(type, type?.ToString() ?? "", null, new Size(1, 1));
            }
        }
    }
}
