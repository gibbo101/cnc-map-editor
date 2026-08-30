using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using MobiusEditor;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Render;

namespace MobiusCli
{
    public static class Commands
    {
        private static string MapArg(Invocation inv)
        {
            if (inv.Positional.Count < 1) throw new ArgumentException("a map path is required");
            return inv.Positional[0];
        }

        public static int Info(Invocation inv, EditorSession session, TextWriter o)
        {
            string mapPath = MapArg(inv);
            IGamePlugin plugin = session.Load(mapPath, out string[] errors);
            Map map = plugin.Map;
            o.WriteLine("file: " + mapPath);
            o.WriteLine("game: " + plugin.GameInfo.GameType);
            o.WriteLine("name: " + map.BasicSection.Name);
            o.WriteLine("theater: " + map.Theater.Name);
            o.WriteLine($"size: {map.Metrics.Width}x{map.Metrics.Height}");
            o.WriteLine($"bounds: {map.Bounds.X},{map.Bounds.Y} {map.Bounds.Width}x{map.Bounds.Height}");
            o.WriteLine("templates: " + map.Templates.Count());
            o.WriteLine("overlay: " + map.Overlay.Count());
            o.WriteLine("smudge: " + map.Smudge.Count());
            o.WriteLine("terrain: " + map.Technos.Select(t => t.Occupier).OfType<Terrain>().Count());
            o.WriteLine("buildings: " + map.Buildings.Select(b => b.Occupier).OfType<Building>().Count());
            o.WriteLine("units: " + map.Technos.Select(t => t.Occupier).OfType<Unit>().Count());
            o.WriteLine("infantry: " + map.Technos.Select(t => t.Occupier).OfType<InfantryGroup>().Sum(g => g.Infantry.Count(i => i != null)));
            o.WriteLine("triggers: " + map.Triggers.Count);
            o.WriteLine("teamtypes: " + map.TeamTypes.Count);
            o.WriteLine("waypoints: " + map.Waypoints.Count(w => w.Cell.HasValue));
            o.WriteLine("unknown entries: " + map.UnknownEntries.Count);
            foreach (UnknownEntry u in map.UnknownEntries) o.WriteLine("  " + u);
            o.WriteLine("load errors: " + errors.Length);
            foreach (string e in errors) o.WriteLine("  " + e);
            return 0;
        }

        /// <summary>Exit 0 when the map would save cleanly with no unknown entries; 1 otherwise, with the reasons on stdout.</summary>
        public static int Validate(Invocation inv, EditorSession session, TextWriter o)
        {
            string mapPath = MapArg(inv);
            IGamePlugin plugin = session.Load(mapPath, out string[] errors);
            int problems = 0;
            foreach (string e in errors) { o.WriteLine("load: " + e); problems++; }
            foreach (UnknownEntry u in plugin.Map.UnknownEntries) { o.WriteLine("unknown: " + u); problems++; }
            string blocking = plugin.Validate(FileType.INI, false, true);
            if (!string.IsNullOrWhiteSpace(blocking)) { o.WriteLine("validate: " + blocking.Trim()); problems++; }
            o.WriteLine(problems == 0 ? "ok" : problems + " problem(s)");
            return problems == 0 ? 0 : 1;
        }

        public static int Mods(Invocation inv, TextWriter o)
        {
            string gameType = inv.Option("game-type", "RA");
            string root = inv.Option("mods-root") ?? ModDiscovery.DefaultModsRoot(MobiusEditor.Program.RemasterSteamId);
            int n = 0;
            if (root != null && Directory.Exists(root))
            {
                foreach (ModInfo m in ModDiscovery.Scan(root, gameType)) { o.WriteLine(m.ToString()); n++; }
            }
            if (inv.Option("no-workshop", "false") != "true")
            {
                foreach (ModInfo m in ModDiscovery.ScanWorkshop(MobiusEditor.Program.RemasterSteamId, gameType)) { o.WriteLine(m.ToString()); n++; }
            }
            o.WriteLine(n + " mod(s)");
            return 0;
        }

        public static int Render(Invocation inv, EditorSession session, TextWriter o)
        {
            string mapPath = MapArg(inv);
            if (inv.Positional.Count < 2) throw new ArgumentException("render needs <map> <out.png>");
            string outPath = inv.Positional[1];
            double scale = double.Parse(inv.Option("scale", "0.25"), System.Globalization.CultureInfo.InvariantCulture);
            bool boundsOnly = inv.Option("bounds-only", "false") == "true";
            IGamePlugin plugin = session.Load(mapPath, out _);
            Size tileSize = new Size(Math.Max(1, (int)Math.Round(Globals.OriginalTileWidth * scale)), Math.Max(1, (int)Math.Round(Globals.OriginalTileHeight * scale)));
            Map map = plugin.Map;
            using (Bitmap full = new Bitmap(map.Metrics.Width * tileSize.Width, map.Metrics.Height * tileSize.Height, PixelFormat.Format32bppArgb))
            {
                full.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(full))
                {
                    MapRenderer.Render(plugin.GameInfo, map, g, null, MapLayerFlag.MapLayers, scale, false, Globals.TheShapeCacheManager);
                }
                if (boundsOnly)
                {
                    Rectangle crop = new Rectangle(map.Bounds.X * tileSize.Width, map.Bounds.Y * tileSize.Height, map.Bounds.Width * tileSize.Width, map.Bounds.Height * tileSize.Height);
                    using (Bitmap cropped = full.Clone(crop, PixelFormat.Format32bppArgb)) cropped.Save(outPath, ImageFormat.Png);
                }
                else full.Save(outPath, ImageFormat.Png);
            }
            o.WriteLine("rendered " + outPath);
            return 0;
        }

        public static int Save(Invocation inv, EditorSession session, TextWriter o)
        {
            string mapPath = MapArg(inv);
            if (inv.Positional.Count < 2) throw new ArgumentException("save needs <map> <out>");
            string outPath = inv.Positional[1];
            if (Path.GetFullPath(outPath) == Path.GetFullPath(mapPath)) throw new ArgumentException("refusing to overwrite the input map; save to a new path");
            IGamePlugin plugin = session.Load(mapPath, out _);
            plugin.Save(outPath, FileType.INI);
            o.WriteLine("saved " + outPath);
            return 0;
        }
    }
}
