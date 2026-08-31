//
// Headless overlay placement, porting the fork's Overlays/Walls/Resources tool semantics.
// The Overlay grid self-heals on assignment (wall adjacency icons, resource type + density,
// concrete shapes — Map.Overlay_CellChanged), so these operations are thin guards routed by
// the overlay's category: nothing places on the top or bottom map row (the game engine never
// reads those back), a real existing overlay is never overwritten, solid overlays respect
// building occupancy, and erase removes only its own category — a wall eraser leaves
// resources alone. Callers accumulate a stroke's before/after values in the undo/redo
// dictionaries, first snapshot per cell wins.
using System.Collections.Generic;
using System.Drawing;

namespace MobiusEditor.Model
{
    public static class OverlayEdit
    {
        public static void Place(Map map, OverlayType type, Point location, IDictionary<int, Overlay> undo, IDictionary<int, Overlay> redo)
        {
            if (type == null || location.Y == 0 || location.Y == map.Metrics.Height - 1) return;
            if (!map.Metrics.GetCell(location, out int cell)) return;
            Overlay overlay = new Overlay { Type = type, Icon = 0 };
            if (type.IsSolid && !map.Buildings.CanAdd(cell, overlay)) return;
            Overlay current = map.Overlay[cell];
            if (current != null && !Map.IsIgnorableOverlay(current)) return;
            Write(map, cell, overlay, undo, redo);
        }

        /// <summary>Erases the overlay at location when it belongs to the selected type's category (wall / resource / plain overlay).</summary>
        public static void Erase(Map map, OverlayType selected, Point location, IDictionary<int, Overlay> undo, IDictionary<int, Overlay> redo)
        {
            if (!map.Metrics.GetCell(location, out int cell)) return;
            Overlay current = map.Overlay[cell];
            if (current == null) return;
            bool matches = selected == null
                ? current.Type.IsOverlay && !Map.IsIgnorableOverlay(current)
                : selected.IsWall ? current.Type.IsWall
                : selected.IsResource ? current.Type.IsResource
                : current.Type.IsOverlay && !Map.IsIgnorableOverlay(current);
            if (!matches) return;
            Write(map, cell, null, undo, redo);
        }

        private static void Write(Map map, int cell, Overlay value, IDictionary<int, Overlay> undo, IDictionary<int, Overlay> redo)
        {
            if (!undo.ContainsKey(cell)) undo[cell] = map.Overlay[cell];
            map.Overlay[cell] = value;
            redo[cell] = value;
        }
    }
}
