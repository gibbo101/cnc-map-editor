using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Render;
using MobiusEditor.Utility;

namespace MobiusEditor.Shell
{
    /// <summary>
    /// One open map as the GUI sees it: the loaded plugin, the current render scale and layer
    /// selection, and the operations the views call. No UI toolkit types, so it is testable headless.
    /// </summary>
    public sealed class MapDocument : IDisposable
    {
        public EditorSession Session { get; }
        public IGamePlugin Plugin { get; private set; }
        public string Path { get; private set; }
        public string[] LoadNotes { get; private set; } = Array.Empty<string>();
        public MapLayerFlag Layers { get; set; } = MapLayerFlag.MapLayers;
        public bool IsOpen => Plugin != null;
        public Map Map => Plugin?.Map;
        public string Title => Plugin == null ? "No map"
            : !string.IsNullOrEmpty(Map.BasicSection.Name) && Map.BasicSection.Name != "<none>" && Map.BasicSection.Name != "None" ? Map.BasicSection.Name
            : Path != null ? System.IO.Path.GetFileName(Path)
            : "Untitled";

        private double scale = 0.25;
        /// <summary>Render scale as a fraction of the original 128 px tile; clamped to what the renderer handles.</summary>
        public double Scale { get => scale; set => scale = Math.Clamp(value, 1.0 / 16, 2.0); }
        public Size TileSize => new Size(Math.Max(1, (int)Math.Round(Globals.OriginalTileWidth * Scale)), Math.Max(1, (int)Math.Round(Globals.OriginalTileHeight * Scale)));

        public event EventHandler Changed;

        public MapDocument(EditorSession session) { Session = session ?? throw new ArgumentNullException(nameof(session)); }

        public void Open(string path)
        {
            Plugin = Session.Load(path, out string[] notes);
            Path = path;
            LoadNotes = notes;
            undoRedo.Clear();
            ResetStroke();
            InvalidateRenderCache();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Creates a fresh empty map (the game's first theater when none given); it has no
        /// file until Save. The playable bounds default to the full playable area and can be
        /// given smaller — they are centered on the fixed cell grid the format dictates.
        /// </summary>
        public void NewMap(string theater = null, Size? playableSize = null)
        {
            Plugin = Session.New(theater, out string[] notes);
            if (playableSize.HasValue)
            {
                Size cells = new Size(Plugin.Map.Metrics.Width, Plugin.Map.Metrics.Height);
                int w = Math.Clamp(playableSize.Value.Width, 16, cells.Width - 2);
                int h = Math.Clamp(playableSize.Value.Height, 16, cells.Height - 2);
                Plugin.Map.TopLeft = new Point(Math.Max(1, (cells.Width - w) / 2), Math.Max(1, (cells.Height - h) / 2));
                Plugin.Map.Size = new Size(w, h);
            }
            Path = null;
            LoadNotes = notes;
            undoRedo.Clear();
            ResetStroke();
            InvalidateRenderCache();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private sealed class UndoRedoArgs : EventArgs, IUndoRedoEventArgs<MapDocument>
        {
            public bool Cancelled { get; set; }
            public bool NewStateIsClean { get; set; }
            public MapDocument Source { get; set; }
        }

        private readonly UndoRedoList<UndoRedoArgs, MapDocument> undoRedo = new UndoRedoList<UndoRedoArgs, MapDocument>(Globals.UndoRedoStackSize);
        private readonly DeterministicRandom random = new DeterministicRandom(0x5EED);
        private Dictionary<int, Template> templateUndo = new Dictionary<int, Template>(), templateRedo = new Dictionary<int, Template>();
        private Dictionary<int, Overlay> overlayUndo = new Dictionary<int, Overlay>(), overlayRedo = new Dictionary<int, Overlay>();
        private Dictionary<int, CellTrigger> cellTriggerUndo = new Dictionary<int, CellTrigger>(), cellTriggerRedo = new Dictionary<int, CellTrigger>();
        private Dictionary<Point, Smudge> smudgeUndo = new Dictionary<Point, Smudge>(), smudgeRedo = new Dictionary<Point, Smudge>();
        private bool inStroke;

        public bool CanUndo => undoRedo.CanUndo;
        public bool CanRedo => undoRedo.CanRedo;

        /// <summary>Starts batching operations (a mouse drag) into a single undo step; EndStroke commits it.</summary>
        public void BeginStroke() => inStroke = true;

        public void EndStroke()
        {
            inStroke = false;
            CommitStroke();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            undoRedo.Undo(new UndoRedoArgs { Source = this });
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            undoRedo.Redo(new UndoRedoArgs { Source = this });
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Templates a palette should offer: available in the map's theater, groups shown instead of their members.</summary>
        public IReadOnlyList<TemplateType> AvailableTemplates() =>
            Map == null ? (IReadOnlyList<TemplateType>)Array.Empty<TemplateType>()
                        : Map.TemplateTypes.Where(t => t.ExistsInTheater && !t.IsGrouped).ToList();

        /// <summary>Overlay types a palette should offer: walls, resources and decorations; never the unplaceable ones.</summary>
        public IReadOnlyList<OverlayType> AvailableOverlays() =>
            Map == null ? (IReadOnlyList<OverlayType>)Array.Empty<OverlayType>()
                        : Map.OverlayTypes.Where(t => !t.Flags.HasFlag(OverlayTypeFlag.Unplaceable)).ToList();

        /// <summary>Stamps a template at cell (or one picked icon of it); the Clear template erases, because null cells ARE clear terrain.</summary>
        public void PlaceTemplate(Point cell, TemplateType type, Point? icon = null)
        {
            RequireOpen();
            if (type != null && type.Flags.HasFlag(TemplateTypeFlag.Clear))
            {
                TemplateEdit.Erase(Map.Templates, null, null, cell, templateUndo, templateRedo);
            }
            else
            {
                TemplateEdit.Place(Map.TemplateTypes, Map.Templates, type, cell, icon, random, templateUndo, templateRedo);
            }
            AfterOperation();
        }

        /// <summary>Erases the footprint of the given template at cell; with no template (or a picked icon), one cell.</summary>
        public void EraseTemplate(Point cell, TemplateType footprint = null, Point? icon = null)
        {
            RequireOpen();
            TemplateEdit.Erase(Map.Templates, footprint, icon, cell, templateUndo, templateRedo);
            AfterOperation();
        }

        public void PlaceOverlay(Point cell, OverlayType type)
        {
            RequireOpen();
            OverlayEdit.Place(Map, type, cell, overlayUndo, overlayRedo);
            AfterOperation();
        }

        /// <summary>Erases the overlay at cell when it matches the category's kind (wall / resource / plain overlay).</summary>
        public void EraseOverlay(Point cell, OverlayType category = null)
        {
            RequireOpen();
            OverlayEdit.Erase(Map, category, cell, overlayUndo, overlayRedo);
            AfterOperation();
        }

        private HouseType placementHouse;
        /// <summary>The house newly placed units, infantry and buildings belong to; defaults to the map's first house.</summary>
        public HouseType PlacementHouse
        {
            get => placementHouse ?? Map?.HouseTypes.First();
            set => placementHouse = value;
        }

        public IReadOnlyList<UnitType> AvailableUnits() =>
            Map == null ? (IReadOnlyList<UnitType>)Array.Empty<UnitType>() : Map.UnitTypes.ToList();

        public IReadOnlyList<InfantryType> AvailableInfantry() =>
            Map == null ? (IReadOnlyList<InfantryType>)Array.Empty<InfantryType>() : Map.InfantryTypes.ToList();

        /// <summary>Places a unit with the fork's defaults: placement house, full strength, north facing, the type's default mission. One undo step; null when refused.</summary>
        public Unit PlaceUnit(Point location, UnitType type)
        {
            RequireOpen();
            if (type == null) return null;
            Unit unit = new Unit
            {
                Type = type,
                House = PlacementHouse,
                Strength = 256,
                Direction = Map.UnitDirectionTypes.First(d => d.Facing == FacingType.North),
                Mission = Map.GetDefaultMission(type),
            };
            if (!Map.Technos.Add(location, unit)) return null;
            TrackOccupier(unit, location, added: true);
            return unit;
        }

        /// <summary>Removes the unit occupying the cell. One undo step.</summary>
        public void EraseUnitAt(Point location)
        {
            RequireOpen();
            if (!(Map.Technos[location] is Unit unit)) return;
            Point actual = Map.Technos[unit].Value;
            Map.Technos.Remove(unit);
            TrackOccupier(unit, actual, added: false);
        }

        /// <summary>
        /// Places one infantryman; the shared InfantryGroup appears with the first man. The
        /// stop comes from the pointer's sub-cell position when given (closest free stop, as
        /// the fork picks it), otherwise the first free one. One undo step per man; null when
        /// the cell is full or held by something else.
        /// </summary>
        public Infantry PlaceInfantry(Point location, InfantryType type, Point? subPixel = null)
        {
            RequireOpen();
            if (type == null || !Map.Metrics.GetCell(location, out int cell)) return null;
            ICellOccupier techno = Map.Technos[cell];
            InfantryGroup group;
            if (techno == null)
            {
                group = new InfantryGroup();
                if (!Map.Technos.Add(location, group)) return null;
            }
            else if (techno is InfantryGroup existing)
            {
                group = existing;
            }
            else
            {
                return null;
            }
            int stop = subPixel.HasValue
                ? InfantryGroup.ClosestStoppingTypes(subPixel.Value).Cast<int>().Where(i => group.Infantry[i] == null).DefaultIfEmpty(-1).First()
                : Array.FindIndex(group.Infantry, i => i == null);
            if (stop < 0)
            {
                return null;
            }
            Infantry infantry = new Infantry(group)
            {
                Type = type,
                House = PlacementHouse,
                Strength = 256,
                Direction = Map.UnitDirectionTypes.First(d => d.Facing == FacingType.North),
                Mission = Map.GetDefaultMission(type),
            };
            group.Infantry[stop] = infantry;
            TrackInfantry(location, cell, stop, infantry, added: true);
            return infantry;
        }

        /// <summary>Removes one infantryman — the closest occupied stop to the pointer's sub-cell position, or the first occupied one. The group leaves with the last man. One undo step.</summary>
        public void EraseInfantryAt(Point location, Point? subPixel = null)
        {
            RequireOpen();
            if (!Map.Metrics.GetCell(location, out int cell)) return;
            if (!(Map.Technos[cell] is InfantryGroup group)) return;
            int stop = subPixel.HasValue
                ? InfantryGroup.ClosestStoppingTypes(subPixel.Value).Cast<int>().Where(i => group.Infantry[i] != null).DefaultIfEmpty(-1).First()
                : Array.FindIndex(group.Infantry, i => i != null);
            if (stop < 0) return;
            Infantry infantry = group.Infantry[stop];
            group.Infantry[stop] = null;
            if (group.Infantry.All(i => i == null))
            {
                Map.Technos.Remove(group);
            }
            TrackInfantry(location, cell, stop, infantry, added: false);
        }

        private void TrackInfantry(Point location, int cell, int stop, Infantry infantry, bool added)
        {
            Map map = Map;
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            dirtyCells.Add(location);
            void Put()
            {
                if (!(map.Technos[cell] is InfantryGroup group))
                {
                    group = new InfantryGroup();
                    map.Technos.Add(location, group);
                }
                group.Infantry[stop] = infantry;
                infantry.InfantryGroup = group;
                plugin.Dirty = true;
                dirtyCells.Add(location);
            }
            void Take()
            {
                if (map.Technos[cell] is InfantryGroup group)
                {
                    group.Infantry[stop] = null;
                    if (group.Infantry.All(i => i == null))
                    {
                        map.Technos.Remove(group);
                    }
                }
                plugin.Dirty = true;
                dirtyCells.Add(location);
            }
            undoRedo.Track(_ => { if (added) Take(); else Put(); }, _ => { if (added) Put(); else Take(); }, this);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The placed object under a cell, for selection: a Building, Unit, Terrain, or an
        /// infantryman of the cell's group — the closest occupied stop to the pointer's
        /// sub-cell position when given, else the first. Null on an empty cell.
        /// </summary>
        public object ObjectAt(Point location, Point? subPixel = null)
        {
            RequireOpen();
            if (Map.Buildings[location] is Building building) return building;
            switch (Map.Technos[location])
            {
                case InfantryGroup group:
                    return subPixel.HasValue
                        ? InfantryGroup.ClosestStoppingTypes(subPixel.Value).Cast<int>().Select(i => group.Infantry[i]).FirstOrDefault(i => i != null)
                        : group.Infantry.FirstOrDefault(i => i != null);
                case ICellOccupier occupier:
                    return occupier;
                default:
                    return null;
            }
        }

        /// <summary>The sub-cell position (the game's 24x24 in-cell pixels) under a pixel of the current render.</summary>
        public Point? SubPixelAt(int pixelX, int pixelY)
        {
            if (Plugin == null) return null;
            Size tile = TileSize;
            if (pixelX < 0 || pixelY < 0) return null;
            return new Point(
                (pixelX % tile.Width) * Globals.PixelWidth / tile.Width,
                (pixelY % tile.Height) * Globals.PixelHeight / tile.Height);
        }

        /// <summary>
        /// Applies an edit to a placed object's properties as one undo step. Buildings are
        /// normalized to the fork's prebuilt invariants after the edit, and the rebuild
        /// base's priorities renumber around them — undo restores every affected building's
        /// priority, not just the edited one's.
        /// </summary>
        public void EditObjectProperties(object techno, Action edit)
        {
            RequireOpen();
            object before = SnapshotOf(techno);
            edit();
            Dictionary<Building, int> prioritiesBefore = null, prioritiesAfter = null;
            if (techno is Building building)
            {
                ObjectPropertiesPresenter.NormalizeBuilding(Plugin, building);
                prioritiesBefore = BasePriorities();
                if (building.BasePriority >= 0) ObjectPropertiesPresenter.AdjustBuildPriorities(Map, building);
                else ObjectPropertiesPresenter.CompactBuildPriorities(Map);
                prioritiesAfter = BasePriorities();
            }
            object after = SnapshotOf(techno);
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            MarkObjectDirty(techno);
            void Apply(object snapshot, Dictionary<Building, int> priorities)
            {
                RestoreInto(techno, snapshot);
                if (priorities != null)
                {
                    foreach (KeyValuePair<Building, int> kv in priorities)
                    {
                        if (!ReferenceEquals(kv.Key, techno)) kv.Key.BasePriority = kv.Value;
                    }
                }
                plugin.Dirty = true;
                MarkObjectDirty(techno);
            }
            undoRedo.Track(_ => Apply(before, prioritiesBefore), _ => Apply(after, prioritiesAfter), this);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Every building's base priority, captured for undoing base renumbering.</summary>
        private Dictionary<Building, int> BasePriorities() =>
            Map.Buildings.OfType<Building>().ToDictionary(b => b.Occupier, b => b.Occupier.BasePriority);

        private static object SnapshotOf(object techno)
        {
            switch (techno)
            {
                case Building b: return b.Clone();
                case Unit u: return u.Clone();
                case Infantry i: return i.Clone();
                case Terrain t: return t.Clone();
                default: throw new ArgumentException("Not an editable object: " + techno);
            }
        }

        private static void RestoreInto(object techno, object snapshot)
        {
            switch (techno)
            {
                case Building b: b.CloneDataFrom((Building)snapshot); break;
                case Unit u: u.CloneDataFrom((Unit)snapshot); break;
                case Infantry i: i.CloneDataFrom((Infantry)snapshot); break;
                case Terrain t: t.CloneDataFrom((Terrain)snapshot); break;
            }
        }

        /// <summary>
        /// Moves a placed object to a new cell as one undo step. The footprint validates at
        /// the target — a blocked move changes nothing and returns false, as the fork rolls
        /// back. Buildings carry their bib and give back any hand-placed smudge the new bib
        /// ate; an infantryman moves into the target cell's stop closest to the pointer, and
        /// a source group left empty retires.
        /// </summary>
        public bool MoveObject(object techno, Point newLocation, Point? subPixel = null)
        {
            RequireOpen();
            switch (techno)
            {
                case Building building:
                    return MoveBuilding(building, newLocation);
                case Infantry infantry:
                    return MoveInfantry(infantry, newLocation, subPixel);
                case ICellOccupier occupier when Map.Technos[occupier] is Point oldLocation:
                    if (newLocation == oldLocation) return false;
                    Map.Technos.Remove(occupier);
                    if (!Map.Technos.Add(newLocation, occupier))
                    {
                        Map.Technos.Add(oldLocation, occupier);
                        return false;
                    }
                    TrackMove(
                        () => { Map.Technos.Remove(occupier); Map.Technos.Add(oldLocation, occupier); MarkOverlapDirty(occupier, oldLocation); MarkOverlapDirty(occupier, newLocation); },
                        () => { Map.Technos.Remove(occupier); Map.Technos.Add(newLocation, occupier); MarkOverlapDirty(occupier, oldLocation); MarkOverlapDirty(occupier, newLocation); });
                    MarkOverlapDirty(occupier, oldLocation);
                    MarkOverlapDirty(occupier, newLocation);
                    return true;
                default:
                    return false;
            }
        }

        private bool MoveBuilding(Building building, Point newLocation)
        {
            Map map = Map;
            if (!(map.Buildings[building] is Point oldLocation) || newLocation == oldLocation) return false;
            map.Buildings.Remove(building);
            if (!map.Buildings.CanAdd(newLocation, building))
            {
                map.Buildings.Add(oldLocation, building);
                return false;
            }
            // As with placement: capture hand-placed smudge the new bib covers, for undo.
            Dictionary<Point, Smudge> eatenSmudge = new Dictionary<Point, Smudge>();
            Dictionary<Point, Smudge> bib = building.GetBib(newLocation, map.SmudgeTypes);
            if (bib != null)
            {
                foreach (Point p in bib.Keys)
                {
                    Smudge old = map.Smudge[p];
                    if (old != null && !old.IsAutoBib) eatenSmudge[p] = old;
                }
            }
            if (!map.Buildings.Add(newLocation, building))
            {
                map.Buildings.Add(oldLocation, building);
                return false;
            }
            void Undo()
            {
                map.Buildings.Remove(building);
                map.Buildings.Add(oldLocation, building);
                foreach (KeyValuePair<Point, Smudge> kv in eatenSmudge)
                {
                    Smudge current = map.Smudge[kv.Key];
                    if (current == null || !current.IsAutoBib) map.Smudge[kv.Key] = kv.Value;
                }
                MarkBuildingDirty(building, oldLocation);
                MarkBuildingDirty(building, newLocation);
            }
            void Redo()
            {
                map.Buildings.Remove(building);
                map.Buildings.Add(newLocation, building);
                MarkBuildingDirty(building, oldLocation);
                MarkBuildingDirty(building, newLocation);
            }
            TrackMove(Undo, Redo);
            MarkBuildingDirty(building, oldLocation);
            MarkBuildingDirty(building, newLocation);
            return true;
        }

        private bool MoveInfantry(Infantry infantry, Point newLocation, Point? subPixel)
        {
            Map map = Map;
            InfantryGroup sourceGroup = infantry.InfantryGroup;
            if (sourceGroup == null || !(map.Technos[sourceGroup] is Point sourceLocation)) return false;
            if (!map.Metrics.GetCell(newLocation, out int targetCell)) return false;
            int sourceStop = Array.IndexOf(sourceGroup.Infantry, infantry);
            if (sourceStop < 0) return false;
            ICellOccupier target = map.Technos[targetCell];
            InfantryGroup targetGroup;
            bool newGroup = false;
            if (ReferenceEquals(target, sourceGroup))
            {
                targetGroup = sourceGroup;
            }
            else if (target == null)
            {
                targetGroup = new InfantryGroup();
                if (!map.Technos.Add(newLocation, targetGroup)) return false;
                newGroup = true;
            }
            else if (target is InfantryGroup existing)
            {
                targetGroup = existing;
            }
            else
            {
                return false;
            }
            int targetStop = subPixel.HasValue
                ? InfantryGroup.ClosestStoppingTypes(subPixel.Value).Cast<int>().Where(i => targetGroup.Infantry[i] == null || (targetGroup == sourceGroup && i == sourceStop)).DefaultIfEmpty(-1).First()
                : Array.FindIndex(targetGroup.Infantry, i => i == null);
            if (targetStop < 0 || (targetGroup == sourceGroup && targetStop == sourceStop))
            {
                if (newGroup) map.Technos.Remove(targetGroup);
                return false;
            }
            void Apply(InfantryGroup from, int fromStop, Point fromLocation, InfantryGroup to, int toStop, Point toLocation)
            {
                from.Infantry[fromStop] = null;
                if (from != to && from.Infantry.All(i => i == null)) map.Technos.Remove(from);
                InfantryGroup landed = map.Technos[toLocation] as InfantryGroup;
                if (landed == null)
                {
                    landed = to;
                    map.Technos.Add(toLocation, landed);
                }
                landed.Infantry[toStop] = infantry;
                infantry.InfantryGroup = landed;
                dirtyCells.Add(fromLocation);
                dirtyCells.Add(toLocation);
            }
            Apply(sourceGroup, sourceStop, sourceLocation, targetGroup, targetStop, newLocation);
            TrackMove(
                () => Apply(infantry.InfantryGroup, targetStop, newLocation, sourceGroup, sourceStop, sourceLocation),
                () => Apply(infantry.InfantryGroup, sourceStop, sourceLocation, targetGroup, targetStop, newLocation));
            return true;
        }

        private void TrackMove(Action undo, Action redo)
        {
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            undoRedo.Track(_ => { undo(); plugin.Dirty = true; }, _ => { redo(); plugin.Dirty = true; }, this);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Whether a previously selected object still sits on the map (undo can retire it).</summary>
        public bool IsObjectOnMap(object techno)
        {
            if (Map == null) return false;
            switch (techno)
            {
                case Building building:
                    return Map.Buildings[building].HasValue;
                case Infantry infantry:
                    return infantry.InfantryGroup != null
                        && Map.Technos[infantry.InfantryGroup].HasValue
                        && infantry.InfantryGroup.Infantry.Contains(infantry);
                case ICellOccupier occupier:
                    return Map.Technos[occupier].HasValue;
                default:
                    return false;
            }
        }

        private void MarkObjectDirty(object techno)
        {
            switch (techno)
            {
                case Building building when Map.Buildings[building] is Point at:
                    MarkBuildingDirty(building, at);
                    break;
                case Infantry infantry when infantry.InfantryGroup != null && Map.Technos[infantry.InfantryGroup] is Point at:
                    MarkOverlapDirty(infantry.InfantryGroup, at);
                    break;
                case ICellOccupier occupier when Map.Technos[occupier] is Point at:
                    MarkOverlapDirty(occupier, at);
                    break;
            }
        }

        public IReadOnlyList<SmudgeType> AvailableSmudge() =>
            Map == null ? (IReadOnlyList<SmudgeType>)Array.Empty<SmudgeType>()
                        : Map.SmudgeTypes.Where(t => t.ExistsInTheater).ToList();

        /// <summary>Places a smudge (crater, scorch, or a loose multi-cell bib); building-attached bibs are never overwritten.</summary>
        public void PlaceSmudge(Point location, SmudgeType type)
        {
            RequireOpen();
            SmudgeEdit.Place(Map, type, location, smudgeUndo, smudgeRedo);
            AfterOperation();
        }

        /// <summary>Erases the smudge under the footprint; the whole smudge goes, from any of its cells.</summary>
        public void EraseSmudge(Point location, SmudgeType footprint = null)
        {
            RequireOpen();
            SmudgeEdit.Erase(Map, footprint, location, smudgeUndo, smudgeRedo);
            AfterOperation();
        }

        public IReadOnlyList<BuildingType> AvailableBuildings() =>
            Map == null ? (IReadOnlyList<BuildingType>)Array.Empty<BuildingType>()
                        : Map.BuildingTypes.Where(t => t.ExistsInTheater).ToList();

        /// <summary>
        /// Places a building with the fork's defaults (placement house, full strength,
        /// prebuilt, outside the rebuild base). The map auto-places the bib; a hand-placed
        /// smudge the bib covers is captured so undo can put it back, and a wall-type
        /// building eats the overlay on its cell the same way. One undo step; null when refused.
        /// </summary>
        public Building PlaceBuilding(Point location, BuildingType type)
        {
            RequireOpen();
            if (type == null) return null;
            Map map = Map;
            Building building = new Building
            {
                Type = type,
                House = PlacementHouse,
                Strength = 256,
                Direction = map.BuildingDirectionTypes.First(d => d.Facing == FacingType.North),
            };
            if (!map.Buildings.CanAdd(location, building)) return null;
            Dictionary<Point, Smudge> eatenSmudge = null;
            Dictionary<Point, Smudge> bib = building.GetBib(location, map.SmudgeTypes);
            if (bib != null)
            {
                eatenSmudge = new Dictionary<Point, Smudge>();
                foreach (Point p in bib.Keys)
                {
                    Smudge old = map.Smudge[p];
                    if (old != null && !old.IsAutoBib) eatenSmudge[p] = old;
                }
            }
            Overlay eatenOverlay = type.IsWall ? map.Overlay[location] : null;
            if (!map.Buildings.Add(location, building)) return null;
            if (eatenOverlay != null) map.Overlay[location] = null;
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            MarkBuildingDirty(building, location);
            void UndoPlace()
            {
                map.Buildings.Remove(building);
                if (eatenOverlay != null) map.Overlay[location] = eatenOverlay;
                if (eatenSmudge != null)
                {
                    foreach (KeyValuePair<Point, Smudge> kv in eatenSmudge)
                    {
                        Smudge current = map.Smudge[kv.Key];
                        if (current == null || !current.IsAutoBib) map.Smudge[kv.Key] = kv.Value;
                    }
                }
                plugin.Dirty = true;
                MarkBuildingDirty(building, location);
            }
            void RedoPlace()
            {
                map.Buildings.Add(location, building);
                if (eatenOverlay != null) map.Overlay[location] = null;
                plugin.Dirty = true;
                MarkBuildingDirty(building, location);
            }
            undoRedo.Track(_ => UndoPlace(), _ => RedoPlace(), this);
            Changed?.Invoke(this, EventArgs.Empty);
            return building;
        }

        /// <summary>
        /// Removes the building occupying the cell. A base building leaving closes the gap in
        /// the rebuild base's priorities. One undo step; undo re-adds it, bib, priorities and all.
        /// </summary>
        public void EraseBuildingAt(Point location)
        {
            RequireOpen();
            if (!(Map.Buildings[location] is Building building)) return;
            Map map = Map;
            Point actual = map.Buildings[building].Value;
            map.Buildings.Remove(building);
            Dictionary<Building, int> prioritiesBefore = null, prioritiesAfter = null;
            if (building.BasePriority >= 0)
            {
                prioritiesBefore = BasePriorities();
                ObjectPropertiesPresenter.CompactBuildPriorities(map);
                prioritiesAfter = BasePriorities();
            }
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            MarkBuildingDirty(building, actual);
            void Apply(bool add, Dictionary<Building, int> priorities)
            {
                if (add) map.Buildings.Add(actual, building);
                else map.Buildings.Remove(building);
                if (priorities != null)
                {
                    foreach (KeyValuePair<Building, int> kv in priorities) kv.Key.BasePriority = kv.Value;
                }
                plugin.Dirty = true;
                MarkBuildingDirty(building, actual);
            }
            undoRedo.Track(_ => Apply(true, prioritiesBefore), _ => Apply(false, prioritiesAfter), this);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The building's overlap rectangle plus its bib cells.</summary>
        private void MarkBuildingDirty(Building building, Point location)
        {
            MarkOverlapDirty(building, location);
            Dictionary<Point, Smudge> bib = building.GetBib(location, Map.SmudgeTypes);
            if (bib == null) return;
            foreach (Point p in bib.Keys)
            {
                if (p.X >= 0 && p.Y >= 0 && p.X < Map.Metrics.Width && p.Y < Map.Metrics.Height) dirtyCells.Add(p);
            }
        }

        /// <summary>Terrain objects (trees, rocks) with art in the map's theater.</summary>
        public IReadOnlyList<TerrainType> AvailableTerrain() =>
            Map == null ? (IReadOnlyList<TerrainType>)Array.Empty<TerrainType>()
                        : Map.TerrainTypes.Where(t => t.ExistsInTheater).ToList();

        /// <summary>Places a terrain object; the occupier set validates the footprint. One undo step. Null when refused.</summary>
        public Terrain PlaceTerrain(Point location, TerrainType type)
        {
            RequireOpen();
            if (type == null) return null;
            Terrain terrain = new Terrain { Type = type };
            if (!Map.Technos.Add(location, terrain)) return null;
            TrackOccupier(terrain, location, added: true);
            return terrain;
        }

        /// <summary>Removes the terrain object occupying the cell (any cell of its footprint). One undo step.</summary>
        public void EraseTerrainAt(Point location)
        {
            RequireOpen();
            if (!(Map.Technos[location] is Terrain terrain)) return;
            Point actual = Map.Technos[terrain].Value;
            Map.Technos.Remove(terrain);
            TrackOccupier(terrain, actual, added: false);
        }

        private void TrackOccupier(ICellOccupier occupier, Point location, bool added)
        {
            Map map = Map;
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            MarkOverlapDirty(occupier, location);
            void Apply(bool add)
            {
                if (add) map.Technos.Add(location, occupier);
                else map.Technos.Remove(occupier);
                plugin.Dirty = true;
                MarkOverlapDirty(occupier, location);
            }
            undoRedo.Track(_ => Apply(!added), _ => Apply(added), this);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Marks the object's whole overlap rectangle dirty; sprites reach beyond their occupied cells.</summary>
        private void MarkOverlapDirty(ICellOccupier occupier, Point location)
        {
            Rectangle bounds = occupier is ICellOverlapper overlapper ? overlapper.OverlapBounds : new Rectangle(0, 0, 1, 1);
            for (int y = 0; y < bounds.Height; y++)
            {
                for (int x = 0; x < bounds.Width; x++)
                {
                    Point p = new Point(location.X + bounds.X + x, location.Y + bounds.Y + y);
                    if (p.X >= 0 && p.Y >= 0 && p.X < Map.Metrics.Width && p.Y < Map.Metrics.Height) dirtyCells.Add(p);
                }
            }
        }

        /// <summary>Triggers a cell trigger may reference: only those whose event can fire from a cell.</summary>
        public IReadOnlyList<string> AvailableCellTriggers() =>
            Map == null ? (IReadOnlyList<string>)Array.Empty<string>()
                        : Map.FilterCellTriggers().Select(t => t.Name).ToList();

        /// <summary>Places a cell trigger; only into an empty cell, and only for an eligible trigger name.</summary>
        public void PlaceCellTrigger(Point location, string trigger)
        {
            RequireOpen();
            if (Trigger.IsEmpty(trigger)) return;
            if (!AvailableCellTriggers().Contains(trigger, StringComparer.OrdinalIgnoreCase)) return;
            if (!Map.Metrics.GetCell(location, out int cell) || Map.CellTriggers[cell] != null) return;
            if (!cellTriggerUndo.ContainsKey(cell)) cellTriggerUndo[cell] = Map.CellTriggers[cell];
            CellTrigger cellTrigger = new CellTrigger(trigger);
            Map.CellTriggers[cell] = cellTrigger;
            cellTriggerRedo[cell] = cellTrigger;
            AfterOperation();
        }

        public void EraseCellTrigger(Point location)
        {
            RequireOpen();
            if (!Map.Metrics.GetCell(location, out int cell) || Map.CellTriggers[cell] == null) return;
            if (!cellTriggerUndo.ContainsKey(cell)) cellTriggerUndo[cell] = Map.CellTriggers[cell];
            Map.CellTriggers[cell] = null;
            cellTriggerRedo[cell] = null;
            AfterOperation();
        }

        /// <summary>Places (or moves) a waypoint's flag; one cell per waypoint, one undo step per move.</summary>
        public void PlaceWaypoint(int index, Point location)
        {
            RequireOpen();
            if (index < 0 || index >= Map.Waypoints.Length || !Map.Metrics.GetCell(location, out int cell)) return;
            MoveWaypoint(index, cell);
        }

        /// <summary>Clears the first waypoint flag found on the cell.</summary>
        public void EraseWaypointAt(Point location)
        {
            RequireOpen();
            if (!Map.Metrics.GetCell(location, out int cell)) return;
            int index = Array.FindIndex(Map.Waypoints, w => w.Cell == cell);
            if (index < 0) return;
            MoveWaypoint(index, null);
        }

        private void MoveWaypoint(int index, int? cell)
        {
            Waypoint waypoint = Map.Waypoints[index];
            int? oldCell = waypoint.Cell;
            if (oldCell == cell) return;
            IGamePlugin plugin = Plugin;
            void Apply(int? value, int? dirtyToo)
            {
                waypoint.Cell = value;
                plugin.Dirty = true;
                if (value.HasValue) MarkDirty(value.Value.Yield());
                if (dirtyToo.HasValue) MarkDirty(dirtyToo.Value.Yield());
            }
            Apply(cell, oldCell);
            undoRedo.Track(_ => Apply(oldCell, cell), _ => Apply(cell, oldCell), this);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Applies an edit to the map settings ([Basic] plus the briefing) as one undo step.</summary>
        public void EditMapSettings(Action edit)
        {
            RequireOpen();
            Dictionary<string, object> before = SnapshotSettings();
            edit();
            Dictionary<string, object> after = SnapshotSettings();
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            void Apply(Dictionary<string, object> snapshot)
            {
                RestoreSettings(snapshot);
                plugin.Dirty = true;
                InvalidateRenderCache();
            }
            undoRedo.Track(_ => Apply(before), _ => Apply(after), this);
            InvalidateRenderCache();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private Dictionary<string, object> SnapshotSettings()
        {
            BasicSection basic = Map.BasicSection;
            return new Dictionary<string, object>
            {
                ["Name"] = basic.Name,
                ["Author"] = basic.Author,
                ["Player"] = basic.Player,
                ["BasePlayer"] = basic.BasePlayer,
                ["SoloMission"] = basic.SoloMission,
                ["ExpansionEnabled"] = basic.ExpansionEnabled,
                ["Percent"] = basic.Percent,
                ["Briefing"] = Map.BriefingSection.Briefing,
            };
        }

        private void RestoreSettings(Dictionary<string, object> snapshot)
        {
            BasicSection basic = Map.BasicSection;
            basic.Name = (string)snapshot["Name"];
            basic.Author = (string)snapshot["Author"];
            basic.Player = (string)snapshot["Player"];
            basic.BasePlayer = (string)snapshot["BasePlayer"];
            basic.SoloMission = (bool)snapshot["SoloMission"];
            basic.ExpansionEnabled = (bool)snapshot["ExpansionEnabled"];
            basic.Percent = (int)snapshot["Percent"];
            Map.BriefingSection.Briefing = (string)snapshot["Briefing"];
        }

        /// <summary>Runs one trigger editing session and commits it as a single undo step; undo restores the previous list and every rewired referrer.</summary>
        public void EditTriggers(Action<TriggerEditor> edit)
        {
            TriggerEditSession session = BeginTriggerEdit();
            edit(session.Editor);
            session.Commit();
        }

        /// <summary>Runs one teamtype editing session as a single undo step; undo also restores the triggers' team references the renames rewrote.</summary>
        public void EditTeamTypes(Action<TeamTypeEditor> edit)
        {
            TeamTypeEditSession session = BeginTeamTypeEdit();
            edit(session.Editor);
            session.Commit();
        }

        /// <summary>
        /// Starts an interactive trigger edit (a dialog): the working copy lives in Editor and
        /// the map is untouched until Commit, which lands everything as a single undo step.
        /// Cancel simply drops the working copy.
        /// </summary>
        public TriggerEditSession BeginTriggerEdit()
        {
            RequireOpen();
            return new TriggerEditSession(this);
        }

        /// <summary>The teamtype counterpart of BeginTriggerEdit.</summary>
        public TeamTypeEditSession BeginTeamTypeEdit()
        {
            RequireOpen();
            return new TeamTypeEditSession(this);
        }

        public sealed class TriggerEditSession
        {
            private readonly MapDocument doc;
            private readonly List<Trigger> before;
            public TriggerEditor Editor { get; }

            internal TriggerEditSession(MapDocument doc)
            {
                this.doc = doc;
                before = doc.Map.Triggers.Select(t => t.Clone()).ToList();
                Editor = TriggerEditor.Begin(doc.Plugin);
            }

            public void Commit()
            {
                Map map = doc.Map;
                IGamePlugin plugin = doc.Plugin;
                Editor.Commit(out Dictionary<object, string> undoRefs, out Dictionary<object, string> redoRefs, out Dictionary<CellTrigger, int> cellCells);
                List<Trigger> after = map.Triggers.Select(t => t.Clone()).ToList();
                void Restore(List<Trigger> triggers, Dictionary<object, string> refs)
                {
                    foreach (KeyValuePair<object, string> kv in refs)
                    {
                        switch (kv.Key)
                        {
                            case CellTrigger cellTrigger: cellTrigger.Trigger = kv.Value; break;
                            case TeamType team: team.Trigger = kv.Value; break;
                            case ITechno techno: techno.Trigger = kv.Value; break;
                        }
                    }
                    // Cell triggers cleaned off the grid come back with their trigger; emptied ones leave it.
                    foreach (KeyValuePair<CellTrigger, int> kv in cellCells)
                    {
                        map.CellTriggers[kv.Value] = Trigger.IsEmpty(kv.Key.Trigger) ? null : kv.Key;
                    }
                    map.Triggers = triggers.Select(t => t.Clone()).ToList();
                    plugin.Dirty = true;
                    // Cleanup sweeps can drop cell triggers off the grid, which are a rendered layer.
                    doc.InvalidateRenderCache();
                }
                List<Trigger> beforeSnapshot = before;
                doc.InvalidateRenderCache();
                doc.undoRedo.Track(_ => Restore(beforeSnapshot, undoRefs), _ => Restore(after, redoRefs), doc);
                doc.Changed?.Invoke(doc, EventArgs.Empty);
            }

            /// <summary>Nothing to roll back: the working copy never touched the map.</summary>
            public void Cancel()
            {
            }
        }

        public sealed class TeamTypeEditSession
        {
            private readonly MapDocument doc;
            private readonly List<TeamType> beforeTeams;
            private readonly List<Trigger> beforeTriggers;
            public TeamTypeEditor Editor { get; }

            internal TeamTypeEditSession(MapDocument doc)
            {
                this.doc = doc;
                beforeTeams = CloneTeams(doc.Map.TeamTypes);
                beforeTriggers = doc.Map.Triggers.Select(t => t.Clone()).ToList();
                Editor = TeamTypeEditor.Begin(doc.Plugin);
            }

            public void Commit()
            {
                Map map = doc.Map;
                IGamePlugin plugin = doc.Plugin;
                Editor.Commit();
                List<TeamType> afterTeams = CloneTeams(map.TeamTypes);
                List<Trigger> afterTriggers = map.Triggers.Select(t => t.Clone()).ToList();
                void Restore(List<TeamType> teams, List<Trigger> triggers)
                {
                    map.TeamTypes.Clear();
                    map.TeamTypes.AddRange(CloneTeams(teams));
                    map.Triggers = triggers.Select(t => t.Clone()).ToList();
                    plugin.Dirty = true;
                }
                List<TeamType> teamsSnapshot = beforeTeams;
                List<Trigger> triggersSnapshot = beforeTriggers;
                doc.undoRedo.Track(_ => Restore(teamsSnapshot, triggersSnapshot), _ => Restore(afterTeams, afterTriggers), doc);
                doc.Changed?.Invoke(doc, EventArgs.Empty);
            }

            public void Cancel()
            {
            }
        }

        private static List<TeamType> CloneTeams(IEnumerable<TeamType> teams) => teams.Select(TeamTypeEditor.CloneTeam).ToList();

        private void AfterOperation()
        {
            MarkDirty(templateRedo.Keys);
            MarkDirty(overlayRedo.Keys);
            MarkDirty(cellTriggerRedo.Keys);
            foreach (Point p in smudgeRedo.Keys) dirtyCells.Add(p);
            foreach (Point p in smudgeUndo.Keys) dirtyCells.Add(p);
            if (!inStroke) CommitStroke();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void CommitStroke()
        {
            if (templateRedo.Count == 0 && overlayRedo.Count == 0 && cellTriggerRedo.Count == 0 && smudgeRedo.Count == 0 && smudgeUndo.Count == 0)
            {
                ResetStroke();
                return;
            }
            Dictionary<int, Template> tUndo = templateUndo, tRedo = templateRedo;
            Dictionary<int, Overlay> oUndo = overlayUndo, oRedo = overlayRedo;
            Dictionary<int, CellTrigger> cUndo = cellTriggerUndo, cRedo = cellTriggerRedo;
            Dictionary<Point, Smudge> sUndo = smudgeUndo, sRedo = smudgeRedo;
            Map map = Map;
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            void Replay(Dictionary<int, Template> t, Dictionary<int, Overlay> o, Dictionary<int, CellTrigger> c, Dictionary<Point, Smudge> s)
            {
                foreach (KeyValuePair<int, Template> kv in t) map.Templates[kv.Key] = kv.Value;
                foreach (KeyValuePair<int, Overlay> kv in o) map.Overlay[kv.Key] = kv.Value;
                foreach (KeyValuePair<int, CellTrigger> kv in c) map.CellTriggers[kv.Key] = kv.Value;
                foreach (KeyValuePair<Point, Smudge> kv in s) map.Smudge[kv.Key] = kv.Value;
                plugin.Dirty = true;
                MarkDirty(t.Keys);
                MarkDirty(o.Keys);
                MarkDirty(c.Keys);
                foreach (Point p in s.Keys) dirtyCells.Add(p);
            }
            undoRedo.Track(_ => Replay(tUndo, oUndo, cUndo, sUndo), _ => Replay(tRedo, oRedo, cRedo, sRedo), this);
            ResetStroke();
        }

        private void ResetStroke()
        {
            templateUndo = new Dictionary<int, Template>();
            templateRedo = new Dictionary<int, Template>();
            overlayUndo = new Dictionary<int, Overlay>();
            overlayRedo = new Dictionary<int, Overlay>();
            cellTriggerUndo = new Dictionary<int, CellTrigger>();
            cellTriggerRedo = new Dictionary<int, CellTrigger>();
            smudgeUndo = new Dictionary<Point, Smudge>();
            smudgeRedo = new Dictionary<Point, Smudge>();
        }

        private void RequireOpen()
        {
            if (Plugin == null) throw new InvalidOperationException("No map is open.");
        }

        private Bitmap renderCache;
        private double renderCacheScale;
        private MapLayerFlag renderCacheLayers;
        private bool renderCacheValid;
        private readonly HashSet<Point> dirtyCells = new HashSet<Point>();

        /// <summary>
        /// Renders the selected layers of the whole map at the current scale. A cached bitmap
        /// is kept up to date by repainting only the cells the operations since the last render
        /// touched — expanded one cell outward, because the Overlay grid self-heals neighboring
        /// wall icons and resource density without journaling them. The caller owns the
        /// returned copy.
        /// </summary>
        public Bitmap Render()
        {
            UpdateRenderCache(out _);
            return CopyBitmap(renderCache);
        }

        /// <summary>
        /// Brings the cached render up to date and returns it, along with the pixel region
        /// that changed since the last call — the whole bitmap after a rebuild, empty when
        /// nothing changed. The returned bitmap IS the cache: never dispose or mutate it, and
        /// copy what you need out of it before the next document operation.
        /// </summary>
        public Bitmap UpdateRenderCache(out Rectangle dirtyPixels)
        {
            if (Plugin == null) throw new InvalidOperationException("No map is open.");
            Size tile = TileSize;
            int width = Map.Metrics.Width * tile.Width, height = Map.Metrics.Height * tile.Height;
            bool reusable = renderCacheValid && renderCache != null && renderCacheScale == Scale && renderCacheLayers == Layers
                && renderCache.Width == width && renderCache.Height == height;
            if (!reusable)
            {
                renderCache?.Dispose();
                renderCache = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                renderCache.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(renderCache))
                {
                    MapRenderer.Render(Plugin.GameInfo, Map, g, null, Layers, Scale, false, Globals.TheShapeCacheManager);
                }
                renderCacheScale = Scale;
                renderCacheLayers = Layers;
                renderCacheValid = true;
                dirtyCells.Clear();
                dirtyPixels = new Rectangle(0, 0, width, height);
            }
            else if (dirtyCells.Count > 0)
            {
                HashSet<Point> cells = ExpandedDirtyCells();
                using (Graphics g = Graphics.FromImage(renderCache))
                {
                    MapRenderer.Render(Plugin.GameInfo, Map, g, cells, Layers, Scale, false, Globals.TheShapeCacheManager);
                }
                dirtyCells.Clear();
                int minX = cells.Min(p => p.X), maxX = cells.Max(p => p.X);
                int minY = cells.Min(p => p.Y), maxY = cells.Max(p => p.Y);
                dirtyPixels = new Rectangle(minX * tile.Width, minY * tile.Height,
                    (maxX - minX + 1) * tile.Width, (maxY - minY + 1) * tile.Height);
            }
            else
            {
                dirtyPixels = Rectangle.Empty;
            }
            return renderCache;
        }

        /// <summary>Raw pixel copy — DrawImage would blend, which is not exact for translucent pixels.</summary>
        private static Bitmap CopyBitmap(Bitmap source)
        {
            Bitmap copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            copy.SetResolution(96, 96);
            Rectangle all = new Rectangle(0, 0, source.Width, source.Height);
            BitmapData src = source.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dst = copy.LockBits(all, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[source.Width * 4];
                for (int y = 0; y < source.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, row, 0, row.Length);
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, dst.Scan0 + y * dst.Stride, row.Length);
                }
            }
            finally
            {
                source.UnlockBits(src);
                copy.UnlockBits(dst);
            }
            return copy;
        }

        /// <summary>
        /// Renders just the given cell block into a bitmap of exactly that block, at the
        /// current scale — the viewport path for zoom levels where a whole-map surface would
        /// be gigabytes. The caller owns the bitmap. Pixels are identical to the matching
        /// crop of a full render (the same guarantee the dirty-cell repaints pin): the
        /// renderer includes anything whose sprite overlaps the block.
        /// </summary>
        public Bitmap RenderBlock(Rectangle cellBlock)
        {
            if (Plugin == null) throw new InvalidOperationException("No map is open.");
            cellBlock.Intersect(new Rectangle(0, 0, Map.Metrics.Width, Map.Metrics.Height));
            Size tile = TileSize;
            Bitmap block = new Bitmap(Math.Max(1, cellBlock.Width * tile.Width), Math.Max(1, cellBlock.Height * tile.Height), PixelFormat.Format32bppArgb);
            block.SetResolution(96, 96);
            HashSet<Point> cells = new HashSet<Point>(cellBlock.Points());
            using (Graphics g = Graphics.FromImage(block))
            {
                g.TranslateTransform(-cellBlock.X * tile.Width, -cellBlock.Y * tile.Height);
                MapRenderer.Render(Plugin.GameInfo, Map, g, cells, Layers, Scale, false, Globals.TheShapeCacheManager);
            }
            return block;
        }

        /// <summary>Forces the next Render to repaint from scratch.</summary>
        private void InvalidateRenderCache() => renderCacheValid = false;

        private void MarkDirty(IEnumerable<int> cells)
        {
            foreach (int cell in cells)
            {
                if (Map.Metrics.GetLocation(cell, out Point location)) dirtyCells.Add(location);
            }
        }

        private HashSet<Point> ExpandedDirtyCells()
        {
            HashSet<Point> expanded = new HashSet<Point>();
            foreach (Point p in dirtyCells)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Point n = new Point(p.X + dx, p.Y + dy);
                        if (n.X >= 0 && n.Y >= 0 && n.X < Map.Metrics.Width && n.Y < Map.Metrics.Height) expanded.Add(n);
                    }
                }
            }
            return expanded;
        }

        /// <summary>The map cell under a pixel of the current render, or null outside the map.</summary>
        public Point? CellAt(int pixelX, int pixelY)
        {
            if (Plugin == null) return null;
            Size tile = TileSize;
            int cx = pixelX / tile.Width, cy = pixelY / tile.Height;
            if (pixelX < 0 || pixelY < 0 || cx >= Map.Metrics.Width || cy >= Map.Metrics.Height) return null;
            return new Point(cx, cy);
        }

        /// <summary>What sits on a cell, for a status line: template, overlay, and any object.</summary>
        public string Describe(Point cell)
        {
            if (Plugin == null) return "";
            Template t = Map.Templates[cell];
            Overlay o = Map.Overlay[cell];
            ICellOccupier occ = Map.Technos[cell] ?? Map.Buildings[cell];
            string s = $"({cell.X},{cell.Y})";
            if (t != null) s += $" {t.Type.Name}:{t.Icon}"; else s += " clear";
            if (o != null) s += $" +{o.Type.Name}:{o.Icon}";
            if (occ != null) s += " " + occ.GetType().Name;
            return s;
        }

        public void Save(string path)
        {
            if (Plugin == null) throw new InvalidOperationException("No map is open.");
            // The plugin's Save silently writes nothing when validation fails; surface it instead.
            string problems = Plugin.Validate(FileType.INI, false, false);
            if (!string.IsNullOrWhiteSpace(problems)) throw new InvalidOperationException(problems.Trim());
            Plugin.Save(path, FileType.INI);
            Path = path;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Plugin?.Dispose();
            Plugin = null;
            renderCache?.Dispose();
            renderCache = null;
            renderCacheValid = false;
        }
    }
}
