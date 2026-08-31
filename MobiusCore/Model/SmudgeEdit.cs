//
// Headless smudge placement, porting the fork's Smudge tool semantics. Craters and
// scorches are single-cell; bib smudges are multi-cell and place per-cell with running
// icons. Building-attached bibs are never overwritten or erased; placing over a loose
// smudge based on the same origin replaces it whole; erasing removes the whole smudge
// from any of its cells and re-heals cells that overlapping multi-cell smudges should
// still cover. Callers accumulate a stroke's before/after values in the undo/redo
// dictionaries, first snapshot per cell wins; a placement whose footprint would overwrite
// a cell already touched in the same stroke is skipped entirely.
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Utility;

namespace MobiusEditor.Model
{
    public static class SmudgeEdit
    {
        public static void Place(Map map, SmudgeType type, Point location, IDictionary<Point, Smudge> undo, IDictionary<Point, Smudge> redo)
        {
            if (type == null)
            {
                return;
            }
            bool multiCell = type.IsMultiCell;
            Size size = type.Size;
            Point scan = location;
            for (int y = 0; y < size.Height; ++y)
            {
                for (int x = 0; x < size.Width; ++x)
                {
                    scan.X = location.X + x;
                    if (undo.ContainsKey(scan))
                    {
                        return;
                    }
                }
                scan.Y++;
            }
            // A loose smudge based on this exact origin is replaced whole.
            foreach (Point p in FindSmudgesBasedAt(map, location))
            {
                Smudge existing = map.Smudge[p];
                if (existing != null && existing.IsAutoBib)
                {
                    continue;
                }
                if (!undo.ContainsKey(p)) undo[p] = map.Smudge[p];
                redo[p] = null;
                map.Smudge[p] = null;
            }
            RestoreNearbySmudge(map, undo.Keys.ToList(), redo);
            Smudge basicSmudge = new Smudge(type, 0, null);
            int icon = 0;
            Point placeLocation = location;
            for (int y = 0; y < size.Height; ++y)
            {
                for (int x = 0; x < size.Width; ++x)
                {
                    placeLocation.X = location.X + x;
                    if (!map.Metrics.Contains(placeLocation))
                    {
                        icon++;
                        continue;
                    }
                    Smudge existingBib = map.Smudge[placeLocation];
                    if (existingBib != null && existingBib.AttachedTo != null)
                    {
                        icon++;
                        continue;
                    }
                    Smudge smudge = basicSmudge.Clone();
                    if (multiCell)
                    {
                        smudge.Icon = icon++;
                    }
                    if (!undo.ContainsKey(placeLocation)) undo[placeLocation] = map.Smudge[placeLocation];
                    redo[placeLocation] = smudge;
                    map.Smudge[placeLocation] = smudge;
                }
                placeLocation.Y++;
            }
        }

        /// <summary>Erases the smudges under the selected type's footprint at location (any of their cells).</summary>
        public static void Erase(Map map, SmudgeType selected, Point location, IDictionary<Point, Smudge> undo, IDictionary<Point, Smudge> redo)
        {
            Rectangle toClear = new Rectangle(location, selected == null ? new Size(1, 1) : selected.Size);
            RemoveSmudgePoints(map, toClear, undo, redo);
            RestoreNearbySmudge(map, undo.Keys.ToList(), redo);
        }

        private static void RemoveSmudgePoints(Map map, Rectangle toClear, IDictionary<Point, Smudge> undo, IDictionary<Point, Smudge> redo)
        {
            foreach (Point loc in toClear.Points())
            {
                if (!map.Metrics.Contains(loc))
                {
                    continue;
                }
                if (!(map.Smudge[loc] is Smudge smudge) || smudge.IsAutoBib)
                {
                    continue;
                }
                SmudgeType smudgeType = smudge.Type;
                Point baseLocation = smudge.GetPlacementOrigin(loc);
                int curIcon = 0;
                Point removeLocation = baseLocation;
                Size size = smudgeType.Size;
                bool isMultiCell = smudgeType.IsMultiCell;
                for (int y = 0; y < size.Height; ++y)
                {
                    for (int x = 0; x < size.Width; ++x)
                    {
                        removeLocation.X = baseLocation.X + x;
                        if (!map.Metrics.Contains(removeLocation))
                        {
                            curIcon++;
                            continue;
                        }
                        Smudge foundSmudge = map.Smudge[removeLocation];
                        if (isMultiCell && foundSmudge != null && (!smudgeType.Equals(foundSmudge.Type) || foundSmudge.Icon != curIcon))
                        {
                            curIcon++;
                            continue;
                        }
                        curIcon++;
                        undo[removeLocation] = foundSmudge;
                        redo[removeLocation] = null;
                        map.Smudge[removeLocation] = null;
                    }
                    removeLocation.Y++;
                }
            }
        }

        /// <summary>Re-covers the given cells with the cell an overlapping multi-cell smudge should still put there.</summary>
        public static void RestoreNearbySmudge(Map map, IEnumerable<Point> points, IDictionary<Point, Smudge> redo)
        {
            if (points == null)
            {
                return;
            }
            int maxW = map.SmudgeTypes.Where(sm => sm.IsMultiCell).Select(sm => sm.Size.Width).DefaultIfEmpty(1).Max();
            int maxH = map.SmudgeTypes.Where(sm => sm.IsMultiCell).Select(sm => sm.Size.Height).DefaultIfEmpty(1).Max();
            foreach (Point loc in points.OrderBy(p => p.X).ThenByDescending(p => p.Y))
            {
                // Bottom-to-top, right-to-left, so the closest overlapping smudge wins.
                Rectangle scanRect = new Rectangle(loc.X - maxW + 1, loc.Y - maxH + 1, maxW * 2 - 1, maxH * 2 - 1);
                foreach (Point p in scanRect.Points().Where(p => map.Metrics.Contains(p)).OrderByDescending(p => p.X).ThenByDescending(p => p.Y))
                {
                    Smudge toFix = map.Smudge[p];
                    SmudgeType toFixType = toFix?.Type;
                    if (toFixType == null || toFix.IsAutoBib || !toFixType.IsMultiCell)
                    {
                        continue;
                    }
                    Point fixBase = toFix.GetPlacementOrigin(p);
                    if (new Rectangle(fixBase, toFixType.Size).Contains(loc))
                    {
                        Smudge fixSmudge = toFix.Clone();
                        fixSmudge.Icon = (loc.Y - fixBase.Y) * toFixType.Size.Width + loc.X - fixBase.X;
                        if (redo != null)
                        {
                            redo[loc] = fixSmudge;
                        }
                        map.Smudge[loc] = fixSmudge;
                        break;
                    }
                }
            }
        }

        /// <summary>Every cell of any smudge whose placement origin is exactly the given location.</summary>
        private static List<Point> FindSmudgesBasedAt(Map map, Point loc)
        {
            int maxW = map.SmudgeTypes.Where(sm => sm.IsMultiCell).Select(sm => sm.Size.Width).DefaultIfEmpty(1).Max();
            int maxH = map.SmudgeTypes.Where(sm => sm.IsMultiCell).Select(sm => sm.Size.Height).DefaultIfEmpty(1).Max();
            Rectangle scanRect = new Rectangle(loc.X, loc.Y, maxW, maxH);
            List<Point> found = new List<Point>();
            foreach (Point p in scanRect.Points())
            {
                if (!map.Metrics.Contains(p))
                {
                    continue;
                }
                Smudge toCheck = map.Smudge[p];
                if (toCheck?.Type == null)
                {
                    continue;
                }
                if (loc == toCheck.GetPlacementOrigin(p))
                {
                    found.Add(p);
                }
            }
            return found;
        }
    }
}
