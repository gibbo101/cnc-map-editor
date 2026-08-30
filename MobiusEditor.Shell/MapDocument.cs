using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Render;

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
            Changed?.Invoke(this, EventArgs.Empty);
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
