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

        /// <summary>Runs one trigger editing session and commits it as a single undo step; undo restores the previous list and every rewired referrer.</summary>
        public void EditTriggers(Action<TriggerEditor> edit)
        {
            RequireOpen();
            Map map = Map;
            IGamePlugin plugin = Plugin;
            List<Trigger> before = map.Triggers.Select(t => t.Clone()).ToList();
            TriggerEditor editor = TriggerEditor.Begin(plugin);
            edit(editor);
            editor.Commit(out Dictionary<object, string> undoRefs, out Dictionary<object, string> redoRefs, out Dictionary<CellTrigger, int> cellCells);
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
            }
            undoRedo.Track(_ => Restore(before, undoRefs), _ => Restore(after, redoRefs), this);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Runs one teamtype editing session as a single undo step; undo also restores the triggers' team references the renames rewrote.</summary>
        public void EditTeamTypes(Action<TeamTypeEditor> edit)
        {
            RequireOpen();
            Map map = Map;
            IGamePlugin plugin = Plugin;
            List<TeamType> beforeTeams = CloneTeams(map.TeamTypes);
            List<Trigger> beforeTriggers = map.Triggers.Select(t => t.Clone()).ToList();
            TeamTypeEditor editor = TeamTypeEditor.Begin(plugin);
            edit(editor);
            editor.Commit();
            List<TeamType> afterTeams = CloneTeams(map.TeamTypes);
            List<Trigger> afterTriggers = map.Triggers.Select(t => t.Clone()).ToList();
            void Restore(List<TeamType> teams, List<Trigger> triggers)
            {
                map.TeamTypes.Clear();
                map.TeamTypes.AddRange(CloneTeams(teams));
                map.Triggers = triggers.Select(t => t.Clone()).ToList();
                plugin.Dirty = true;
            }
            undoRedo.Track(_ => Restore(beforeTeams, beforeTriggers), _ => Restore(afterTeams, afterTriggers), this);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static List<TeamType> CloneTeams(IEnumerable<TeamType> teams) => teams.Select(TeamTypeEditor.CloneTeam).ToList();

        private void AfterOperation()
        {
            if (!inStroke) CommitStroke();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void CommitStroke()
        {
            if (templateRedo.Count == 0 && overlayRedo.Count == 0)
            {
                ResetStroke();
                return;
            }
            Dictionary<int, Template> tUndo = templateUndo, tRedo = templateRedo;
            Dictionary<int, Overlay> oUndo = overlayUndo, oRedo = overlayRedo;
            Map map = Map;
            IGamePlugin plugin = Plugin;
            plugin.Dirty = true;
            void Replay(Dictionary<int, Template> t, Dictionary<int, Overlay> o)
            {
                foreach (KeyValuePair<int, Template> kv in t) map.Templates[kv.Key] = kv.Value;
                foreach (KeyValuePair<int, Overlay> kv in o) map.Overlay[kv.Key] = kv.Value;
                plugin.Dirty = true;
            }
            undoRedo.Track(_ => Replay(tUndo, oUndo), _ => Replay(tRedo, oRedo), this);
            ResetStroke();
        }

        private void ResetStroke()
        {
            templateUndo = new Dictionary<int, Template>();
            templateRedo = new Dictionary<int, Template>();
            overlayUndo = new Dictionary<int, Overlay>();
            overlayRedo = new Dictionary<int, Overlay>();
        }

        private void RequireOpen()
        {
            if (Plugin == null) throw new InvalidOperationException("No map is open.");
        }

        /// <summary>Renders the selected layers of the whole map at the current scale.</summary>
        public Bitmap Render()
        {
            if (Plugin == null) throw new InvalidOperationException("No map is open.");
            Size tile = TileSize;
            Bitmap bm = new Bitmap(Map.Metrics.Width * tile.Width, Map.Metrics.Height * tile.Height, PixelFormat.Format32bppArgb);
            bm.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(bm))
            {
                MapRenderer.Render(Plugin.GameInfo, Map, g, null, Layers, Scale, false, Globals.TheShapeCacheManager);
            }
            return bm;
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

        public void Dispose() { Plugin?.Dispose(); Plugin = null; }
    }
}
