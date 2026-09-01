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

        /// <summary>
        /// Applies the command line's place/erase options in order and saves to --out (never the
        /// input). A template with no art in the map's theater is an error, not a silent no-op.
        /// </summary>
        public static int Edit(Invocation inv, EditorSession session, TextWriter o)
        {
            string mapPath = MapArg(inv);
            string outPath = inv.Option("out") ?? throw new ArgumentException("edit needs --out <path>");
            if (string.Equals(System.IO.Path.GetFullPath(outPath), System.IO.Path.GetFullPath(mapPath), StringComparison.Ordinal))
            {
                throw new ArgumentException("--out must differ from the input map; edit never overwrites its input.");
            }
            IGamePlugin plugin = session.Load(mapPath, out _);
            Map map = plugin.Map;
            MobiusEditor.Utility.DeterministicRandom random = new MobiusEditor.Utility.DeterministicRandom(0x5EED);
            System.Collections.Generic.Dictionary<int, Template> tUndo = new System.Collections.Generic.Dictionary<int, Template>(), tRedo = new System.Collections.Generic.Dictionary<int, Template>();
            System.Collections.Generic.Dictionary<int, Overlay> oUndo = new System.Collections.Generic.Dictionary<int, Overlay>(), oRedo = new System.Collections.Generic.Dictionary<int, Overlay>();
            int ops = 0;
            foreach (System.Collections.Generic.KeyValuePair<string, string> op in inv.Sequence)
            {
                switch (op.Key)
                {
                    case "place":
                        (string tileName, Point tileAt) = ParseNameAt(op.Value);
                        TemplateType template = map.TemplateTypes.FirstOrDefault(t => string.Equals(t.Name, tileName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new ArgumentException($"unknown template '{tileName}'");
                        if (!template.ExistsInTheater) throw new ArgumentException($"template '{tileName}' has no art in theater {map.Theater.Name}");
                        TemplateEdit.Place(map.TemplateTypes, map.Templates, template, tileAt, null, random, tUndo, tRedo);
                        ops++;
                        break;
                    case "erase":
                        TemplateEdit.Erase(map.Templates, null, null, ParsePoint(op.Value), tUndo, tRedo);
                        ops++;
                        break;
                    case "place-overlay":
                        (string overlayName, Point overlayAt) = ParseNameAt(op.Value);
                        OverlayType overlayType = map.OverlayTypes.FirstOrDefault(t => string.Equals(t.Name, overlayName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new ArgumentException($"unknown overlay '{overlayName}'");
                        OverlayEdit.Place(map, overlayType, overlayAt, oUndo, oRedo);
                        ops++;
                        break;
                    case "erase-overlay":
                        Point at = ParsePoint(op.Value);
                        if (map.Metrics.GetCell(at, out int cell) && map.Overlay[cell] != null)
                        {
                            OverlayEdit.Erase(map, map.Overlay[cell].Type, at, oUndo, oRedo);
                        }
                        ops++;
                        break;
                }
            }
            plugin.Save(outPath, FileType.INI);
            o.WriteLine($"{ops} operation(s) applied; wrote {outPath}");
            return 0;
        }

        /// <summary>
        /// Creates a fresh empty map and writes it to the given path (refusing to overwrite).
        /// Two player-start waypoints are scaffolded at opposite corners of the bounds so the
        /// map satisfies the game's save rules immediately; move them in the editor.
        /// </summary>
        public static int New(Invocation inv, EditorSession session, TextWriter o)
        {
            if (inv.Positional.Count < 1) throw new ArgumentException("new needs an output path: cncmap new <out> [--theater <name>]");
            string outPath = inv.Positional[0];
            if (File.Exists(outPath)) throw new ArgumentException("refusing to overwrite existing file " + outPath);
            IGamePlugin plugin = session.New(inv.Option("theater"), out string[] notes);
            foreach (string n in notes) o.WriteLine("note: " + n);
            Map map = plugin.Map;
            Waypoint[] starts = map.Waypoints.Where(w => w.Flags.HasFlag(WaypointFlag.PlayerStart)).Take(2).ToArray();
            map.Metrics.GetCell(new Point(map.Bounds.Left + 2, map.Bounds.Top + 2), out int first);
            map.Metrics.GetCell(new Point(map.Bounds.Right - 2, map.Bounds.Bottom - 2), out int second);
            starts[0].Cell = first;
            starts[1].Cell = second;
            plugin.Save(outPath, FileType.INI);
            o.WriteLine($"new {plugin.GameInfo.GameType} map, theater {map.Theater.Name}, bounds {map.Bounds.Width}x{map.Bounds.Height}; wrote {outPath}");
            return 0;
        }

        /// <summary>
        /// Generates a random skirmish map: spaced player starts, tree cover, ore by every
        /// start and gems between them. Deterministic — the same seed and dials always
        /// produce the same map.
        /// </summary>
        public static int Generate(Invocation inv, EditorSession session, TextWriter o)
        {
            if (inv.Positional.Count < 1) throw new ArgumentException("generate needs an output path: cncmap generate <out> [--theater <name>] [--seed <n>] [--players <n>] [--trees 0..1] [--ore 0..1] [--water 0..1] [--water-style lakes|river|ocean|islands]");
            string outPath = inv.Positional[0];
            if (File.Exists(outPath)) throw new ArgumentException("refusing to overwrite existing file " + outPath);
            MapGeneratorOptions options = new MapGeneratorOptions
            {
                Seed = int.Parse(inv.Option("seed", "1")),
                Players = int.Parse(inv.Option("players", "4")),
                Trees = double.Parse(inv.Option("trees", "0.5"), System.Globalization.CultureInfo.InvariantCulture),
                Ore = double.Parse(inv.Option("ore", "0.5"), System.Globalization.CultureInfo.InvariantCulture),
                Water = double.Parse(inv.Option("water", "0.5"), System.Globalization.CultureInfo.InvariantCulture),
                Style = Enum.TryParse(inv.Option("water-style", "lakes"), true, out WaterStyle style) ? style
                    : throw new ArgumentException("unknown --water-style (none|lakes|river|ocean|islands)"),
                Lakes = OptInt(inv, "lakes"),
                Islands = OptInt(inv, "islands"),
                RiverWidth = OptInt(inv, "river-width"),
                Fords = OptInt(inv, "fords"),
                OceanDepth = OptInt(inv, "ocean-depth"),
                OceanEdge = inv.Option("ocean-edge") is string oe
                    ? Array.IndexOf(new[] { "north", "south", "east", "west" }, oe.ToLowerInvariant()) is int idx && idx >= 0 ? idx
                        : throw new ArgumentException("unknown --ocean-edge (north|south|east|west)")
                    : (int?)null,
                Causeways = OptBool(inv, "causeways"),
                Villages = OptInt(inv, "villages"),
                Roads = OptBool(inv, "roads"),
                Tiberium = double.Parse(inv.Option("tiberium", "0"), System.Globalization.CultureInfo.InvariantCulture),
                Coast = Enum.TryParse(inv.Option("coast", "mixed"), true, out CoastFlavor coast) ? coast
                    : throw new ArgumentException("unknown --coast (mixed|beach|cliff)"),
            };
            IGamePlugin plugin = session.New(inv.Option("theater"), out string[] notes);
            foreach (string n in notes) o.WriteLine("note: " + n);
            foreach (string w in MapGenerator.Generate(plugin, options)) o.WriteLine("note: " + w);
            string invalid = plugin.Validate(FileType.INI, false, false);
            if (invalid != null) throw new InvalidOperationException("generated map failed validation: " + invalid);
            plugin.Save(outPath, FileType.INI);
            Map generated = plugin.Map;
            int starts = generated.Waypoints.Count(w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue);
            o.WriteLine($"generated {plugin.GameInfo.GameType} map, theater {generated.Theater.Name}, seed {options.Seed}, {starts} starts; wrote {outPath}");
            return 0;
        }

        /// <summary>
        /// Expands a JSON mission spec (patterns + raw rows) into the map's triggers and
        /// teamtypes, and saves to --out (never the input). Refused entirely — nothing
        /// written — when any pattern fails to build or the expanded triggers fail the
        /// game's trigger check with fatals.
        /// </summary>
        public static int ExpandMission(Invocation inv, EditorSession session, TextWriter o)
        {
            string mapPath = MapArg(inv);
            if (inv.Positional.Count < 2) throw new ArgumentException("expand-mission needs a spec: cncmap expand-mission <map> <spec.json> --out <path>");
            string specPath = inv.Positional[1];
            string outPath = inv.Option("out") ?? throw new ArgumentException("expand-mission needs --out <path>");
            if (string.Equals(System.IO.Path.GetFullPath(outPath), System.IO.Path.GetFullPath(mapPath), StringComparison.Ordinal))
            {
                throw new ArgumentException("--out must differ from the input map; expand-mission never overwrites its input.");
            }
            MissionSpec spec = MissionSpec.Parse(File.ReadAllText(specPath), out string[] specErrors);
            if (spec == null || specErrors.Length > 0)
            {
                throw new ArgumentException("spec problems:\n  " + string.Join("\n  ", specErrors));
            }
            IGamePlugin plugin = session.Load(mapPath, out _);
            if (!MissionExpander.Expand(plugin, spec, out string[] errors, out string[] warnings))
            {
                throw new ArgumentException("expansion refused:\n  " + string.Join("\n  ", errors));
            }
            foreach (string w in warnings) o.WriteLine("warning: " + w);
            plugin.Save(outPath, FileType.INI);
            o.WriteLine($"expanded {spec.Patterns.Count} pattern(s): map now has {plugin.Map.Triggers.Count} trigger(s), {plugin.Map.TeamTypes.Count} teamtype(s); wrote {outPath}");
            return 0;
        }

        private static (string name, Point at) ParseNameAt(string value)
        {
            int split = value.LastIndexOf('@');
            if (split <= 0) throw new ArgumentException($"expected <name>@<x>,<y>, got '{value}'");
            return (value.Substring(0, split), ParsePoint(value.Substring(split + 1)));
        }

        private static Point ParsePoint(string value)
        {
            string[] parts = value.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
            {
                throw new ArgumentException($"expected <x>,<y>, got '{value}'");
            }
            return new Point(x, y);
        }

        /// <summary>Distinct mods that supplied the types the map actually places; empty = vanilla-safe.</summary>
        private static int? OptInt(Invocation inv, string name) =>
            inv.Option(name) is string v ? int.Parse(v) : (int?)null;

        private static bool? OptBool(Invocation inv, string name) =>
            inv.Option(name) is string v ? !v.Equals("false", StringComparison.OrdinalIgnoreCase) && !v.Equals("off", StringComparison.OrdinalIgnoreCase) : (bool?)null;

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
