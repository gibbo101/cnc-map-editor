using System;
using System.Collections.Generic;
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
            List<string> requiredMods = RequiredMods(map);
            o.WriteLine(requiredMods.Count == 0 ? "requires mods: none (vanilla-safe)" : "requires mods: " + string.Join(", ", requiredMods));
            o.WriteLine("unknown entries: " + map.UnknownEntries.Count);
            foreach (UnknownEntry u in map.UnknownEntries) o.WriteLine("  " + u);
            o.WriteLine("load errors: " + errors.Length);
            foreach (string e in errors) o.WriteLine("  " + e);
            return 0;
        }

        /// <summary>
        /// Exit 0 when the map would save cleanly with nothing lost: unknown entries and blocking
        /// checks are problems (exit 1); load-time conversions of legacy content are notes only.
        /// </summary>
        public static int Validate(Invocation inv, EditorSession session, TextWriter o)
        {
            string mapPath = MapArg(inv);
            IGamePlugin plugin = session.Load(mapPath, out string[] notes);
            int problems = 0;
            foreach (string n in notes) o.WriteLine("note: " + n);
            List<string> requiredMods = RequiredMods(plugin.Map);
            // Needing a mod is not a problem while the mod is active; without it the map's
            // content would already fail as unknown entries below.
            if (requiredMods.Count > 0) o.WriteLine("note: requires mods: " + string.Join(", ", requiredMods));
            foreach (UnknownEntry u in plugin.Map.UnknownEntries) { o.WriteLine("unknown: " + u); problems++; }
            string blocking = plugin.Validate(FileType.INI, false, true);
            if (!string.IsNullOrWhiteSpace(blocking)) { o.WriteLine("blocking: " + blocking.Trim()); problems++; }
            string suffix = notes.Length == 0 ? "" : $" ({notes.Length} note(s))";
            o.WriteLine(problems == 0 ? "ok" + suffix : problems + " problem(s)" + suffix);
            return problems == 0 ? 0 : 1;
        }

        /// <summary>Distinct mods that supplied the types the map actually places; empty = vanilla-safe.</summary>
        private static List<string> RequiredMods(Map map) =>
            map.Templates.Select(t => t.Value?.Type?.ModSource)
                .Concat(map.Buildings.Select(b => b.Occupier).OfType<Building>().Select(b => b.Type?.ModSource))
                .Concat(map.Technos.Select(t => t.Occupier).OfType<Unit>().Select(u => u.Type?.ModSource))
                .Concat(map.Technos.Select(t => t.Occupier).OfType<InfantryGroup>()
                    .SelectMany(g => g.Infantry).Where(i => i != null).Select(i => i.Type?.ModSource))
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

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
