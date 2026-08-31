//
// Headless template placement, porting the WinForms TemplateTool's semantics: a stamp
// writes row-major positional icons; masked (filler) icons leave their cells untouched but
// still advance the icon number; off-grid cells clip silently; a 1x1 random template rolls
// its icon; a group pseudo-template resolves to a member type with icon 0 and is never
// itself stored; erase writes null, because null IS clear terrain. Callers accumulate a
// stroke's before/after cell values in the undo/redo dictionaries (first snapshot per cell
// wins) and commit them to an undo stack as one action. Deliberate deviation from the fork:
// randomness comes from the caller's DeterministicRandom, never an unseeded RNG.
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Utility;

namespace MobiusEditor.Model
{
    public static class TemplateEdit
    {
        public static void Place(IList<TemplateType> templateTypes, CellGrid<Template> templates, TemplateType selected, Point location,
            Point? selectedIcon, DeterministicRandom random, IDictionary<int, Template> undo, IDictionary<int, Template> redo)
        {
            if (selected == null) return;
            bool isGroup = selected.IsGroup;
            if (selectedIcon.HasValue)
            {
                if (!templates.Metrics.GetCell(location, out int cell)) return;
                int icon = selectedIcon.Value.Y * selected.ThumbnailIconWidth + selectedIcon.Value.X;
                TemplateType placeType = selected;
                if (isGroup)
                {
                    string member = selected.GroupTiles[icon];
                    placeType = templateTypes.First(t => t.Name == member);
                    icon = 0;
                }
                Write(templates, cell, new Template { Type = placeType, Icon = icon }, undo, redo);
                return;
            }
            bool isRandom = selected.IsRandom && selected.IconWidth == 1 && selected.IconHeight == 1;
            int iconIndex = 0;
            for (int y = 0; y < selected.IconHeight; y++)
            {
                for (int x = 0; x < selected.IconWidth; x++, iconIndex++)
                {
                    if (selected.IconMask != null && !selected.IconMask[y, x]) continue;
                    Point subLocation = new Point(location.X + x, location.Y + y);
                    if (!templates.Metrics.GetCell(subLocation, out int cell)) continue;
                    TemplateType placeType = selected;
                    int placeIcon = iconIndex;
                    if (isGroup)
                    {
                        string member = selected.GroupTiles[random.Next(selected.NumIcons)];
                        placeType = templateTypes.First(t => t.Name == member);
                        placeIcon = 0;
                    }
                    else if (isRandom)
                    {
                        placeIcon = random.Next(selected.NumIcons);
                    }
                    Write(templates, cell, new Template { Type = placeType, Icon = placeIcon }, undo, redo);
                }
            }
        }

        /// <summary>Erases the selected template's footprint at location; a null selection or a single-icon pick erases just that cell.</summary>
        public static void Erase(CellGrid<Template> templates, TemplateType selected, Point? selectedIcon, Point location,
            IDictionary<int, Template> undo, IDictionary<int, Template> redo)
        {
            if (selected == null || selectedIcon.HasValue)
            {
                if (templates.Metrics.GetCell(location, out int cell)) Write(templates, cell, null, undo, redo);
                return;
            }
            for (int y = 0; y < selected.IconHeight; y++)
            {
                for (int x = 0; x < selected.IconWidth; x++)
                {
                    if (selected.IconMask != null && !selected.IconMask[y, x]) continue;
                    if (!templates.Metrics.GetCell(new Point(location.X + x, location.Y + y), out int cell)) continue;
                    Write(templates, cell, null, undo, redo);
                }
            }
        }

        private static void Write(CellGrid<Template> templates, int cell, Template value, IDictionary<int, Template> undo, IDictionary<int, Template> redo)
        {
            if (!undo.ContainsKey(cell)) undo[cell] = templates[cell];
            templates[cell] = value;
            redo[cell] = value;
        }
    }
}
