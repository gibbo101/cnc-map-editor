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
        public string Title => Plugin == null ? "No map" : (string.IsNullOrEmpty(Map.BasicSection.Name) ? System.IO.Path.GetFileName(Path) : Map.BasicSection.Name);

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
            if (!inStroke) CommitStroke();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void CommitStroke()
        {
            if (templateRedo.Count == 0 && overlayRedo.Count == 0 && cellTriggerRedo.Count == 0)
            {
                ResetStroke();
                return;
            }
            Dictionary<int, Template> tUndo = templateUndo, tRedo = templateRedo;
            Dictionary<int, Overlay> oUndo = overlayUndo, oRedo = overlayRedo;
            Dictionary<int, CellTrigger> cUndo = cellTriggerUndo, cRedo = cellTriggerRedo;
            Map map = Map;
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            void Replay(Dictionary<int, Template> t, Dictionary<int, Overlay> o, Dictionary<int, CellTrigger> c)
            {
                foreach (KeyValuePair<int, Template> kv in t) map.Templates[kv.Key] = kv.Value;
                foreach (KeyValuePair<int, Overlay> kv in o) map.Overlay[kv.Key] = kv.Value;
                foreach (KeyValuePair<int, CellTrigger> kv in c) map.CellTriggers[kv.Key] = kv.Value;
                plugin.Dirty = true;
                MarkDirty(t.Keys);
                MarkDirty(o.Keys);
                MarkDirty(c.Keys);
            }
            undoRedo.Track(_ => Replay(tUndo, oUndo, cUndo), _ => Replay(tRedo, oRedo, cRedo), this);
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
            }
            else if (dirtyCells.Count > 0)
            {
                using (Graphics g = Graphics.FromImage(renderCache))
                {
                    MapRenderer.Render(Plugin.GameInfo, Map, g, ExpandedDirtyCells(), Layers, Scale, false, Globals.TheShapeCacheManager);
                }
                dirtyCells.Clear();
            }
            return CopyBitmap(renderCache);
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
