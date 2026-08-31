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
        /// <summary>The type's INI name, so search finds "3tnk" as well as "Heavy Tank".</summary>
        public string IniName { get; }
        public Bitmap Thumbnail { get; }
        public Size FootprintCells { get; }

        private PaletteItem(object type, string label, string iniName, Bitmap thumbnail, Size footprint)
        {
            Type = type;
            Label = label;
            IniName = iniName ?? "";
            Thumbnail = thumbnail;
            FootprintCells = footprint;
        }

        public static PaletteItem From(object type)
        {
            switch (type)
            {
                case TemplateType t:
                    string label = t.IconWidth > 1 || t.IconHeight > 1 ? $"{t.Name} ({t.IconWidth}×{t.IconHeight})" : t.Name;
                    return new PaletteItem(t, label, t.Name, t.Thumbnail, new Size(t.IconWidth, t.IconHeight));
                case BuildingType b:
                    return new PaletteItem(b, b.DisplayName, b.Name, b.Thumbnail, b.Size);
                case TerrainType tt:
                    return new PaletteItem(tt, tt.DisplayName, tt.Name, tt.Thumbnail, tt.Size);
                case SmudgeType s:
                    return new PaletteItem(s, s.DisplayName, s.Name, s.Thumbnail, s.Size);
                case OverlayType o:
                    return new PaletteItem(o, o.DisplayName, o.Name, o.Thumbnail, new Size(1, 1));
                case ITechnoType techno when type is IBrowsableType browsable:
                    // Units and infantry: single-cell.
                    return new PaletteItem(type, browsable.DisplayName, techno.Name, browsable.Thumbnail, new Size(1, 1));
                case IBrowsableType browsable:
                    return new PaletteItem(type, browsable.DisplayName, null, browsable.Thumbnail, new Size(1, 1));
                default:
                    return new PaletteItem(type, type?.ToString() ?? "", null, null, new Size(1, 1));
            }
        }
    }
}
