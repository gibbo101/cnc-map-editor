//
// Civilian dressing for generated maps: villages (clusters of neutral civilian buildings)
// and the dirt roads that link them. Roads are forgiving — road art sits on grass with no
// hard seam — so paths are simple L-shaped runs of the game's road templates: the
// north-south and east-west straight groups, with a bend piece dropped at the turn.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.Headless
{
    public static class SettlementBuilder
    {
        /// <summary>
        /// Places a village: a loose cluster of neutral civilian buildings around a center,
        /// on clear buildable ground. Returns the buildings actually placed.
        /// </summary>
        public static List<Point> PlaceVillage(Map map, Point center, int size, DeterministicRandom random)
        {
            List<BuildingType> houses = map.BuildingTypes
                .Where(t => t.ExistsInTheater && string.IsNullOrEmpty(t.ModSource)
                    && t.Name.Length == 3 && t.Name[0] == 'v' && char.IsDigit(t.Name[1]) && char.IsDigit(t.Name[2]))
                .OrderBy(t => t.ID)
                .ToList();
            HouseType neutral = map.HouseTypes.FirstOrDefault(h => h.Name.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
                ?? map.HouseTypes.First();
            List<Point> placed = new List<Point>();
            if (houses.Count == 0) return placed;
            for (int attempt = 0; attempt < size * 6 && placed.Count < size; attempt++)
            {
                BuildingType type = houses[random.Next(houses.Count)];
                Point at = new Point(center.X + random.Next(9) - 4, center.Y + random.Next(9) - 4);
                if (!map.Bounds.Contains(at)) continue;
                bool clear = true;
                for (int y = 0; y < type.Size.Height && clear; y++)
                    for (int x = 0; x < type.Size.Width && clear; x++)
                        clear = map.Bounds.Contains(new Point(at.X + x, at.Y + y))
                            && LakeBuilder.LandAt(map, new Point(at.X + x, at.Y + y)) == LandType.Clear;
                if (!clear) continue;
                Building building = new Building
                {
                    Type = type,
                    House = neutral,
                    Strength = 256,
                    IsPrebuilt = true,
                    Direction = map.BuildingDirectionTypes.First(d => d.Facing == FacingType.North),
                };
                if (map.Buildings.Add(at, building)) placed.Add(at);
            }
            return placed;
        }

        /// <summary>
        /// An L-shaped dirt road between two points: straight runs from the game's road
        /// groups, a bend piece at the turn. Road art tolerates grass around it, so gaps
        /// where water or objects block a stamp just read as a track fading out.
        /// </summary>
        public static void PlaceRoad(Map map, Point from, Point to, DeterministicRandom random)
        {
            List<TemplateType> northSouth = RoadFamily(map, "d07", "d08");
            List<TemplateType> eastWest = RoadFamily(map, "d11", "d12", "d09", "d10");
            if (northSouth.Count == 0 || eastWest.Count == 0) return;
            Dictionary<int, Template> undo = new Dictionary<int, Template>(), redo = new Dictionary<int, Template>();
            void Stamp(TemplateType piece, Point at)
            {
                // Roads never overwrite shores or water — a track stops at the coast.
                // The whole footprint must be plain ground or existing road (legs and
                // bends overlap on purpose); one shore or water cell rejects the stamp.
                for (int y = 0; y < piece.IconHeight; y++)
                {
                    for (int x = 0; x < piece.IconWidth; x++)
                    {
                        Point cell = new Point(at.X + x, at.Y + y);
                        if (!map.Bounds.Contains(cell)) return;
                        LandType land = LakeBuilder.LandAt(map, cell);
                        if (land != LandType.Clear && land != LandType.Road) return;
                    }
                }
                TemplateEdit.Place(map.TemplateTypes, map.Templates, piece, at, null, random, undo, redo);
            }
            Point corner = new Point(to.X, from.Y);
            int stepX = Math.Sign(to.X - from.X), stepY = Math.Sign(to.Y - from.Y);
            for (int x = from.X; stepX != 0 && Math.Sign(corner.X - x) == stepX; x += stepX * 2)
            {
                Stamp(eastWest[random.Next(eastWest.Count)], new Point(x, from.Y));
            }
            for (int y = corner.Y; stepY != 0 && (Math.Sign(to.Y - y) == stepY || y == to.Y); y += stepY * 2)
            {
                Stamp(northSouth[random.Next(northSouth.Count)], new Point(corner.X, y));
            }
            if (stepX != 0 && stepY != 0)
            {
                // A real bend from the tileset over the elbow, picked by which two edges the
                // legs enter from; its curve cell lands on the crossing of the legs' road
                // lines (the EW pieces carry their road on row 1, the NS pieces on column 1).
                (string name, Point elbow) = (stepX > 0, stepY > 0) switch
                {
                    (true, true) => ("d22", new Point(1, 1)),   // west leg in, south leg out
                    (false, true) => ("d31", new Point(0, 0)),  // east leg in, south leg out
                    (true, false) => ("d14", new Point(2, 2)),  // west leg in, north leg out
                    _ => ("d16", new Point(1, 1)),              // east leg in, north leg out
                };
                TemplateType bend = map.TemplateTypes.FirstOrDefault(t => t.Name == name && t.ExistsInTheater);
                if (bend != null)
                {
                    Stamp(bend, new Point(corner.X + 1 - elbow.X, corner.Y + 1 - elbow.Y));
                }
            }
        }

        private static List<TemplateType> RoadFamily(Map map, params string[] names) => map.TemplateTypes
            .Where(t => names.Contains(t.Name, StringComparer.OrdinalIgnoreCase) && t.ExistsInTheater)
            .ToList();
    }
}
