//
// mapeditor.json: a mod's editor type tables, shipped beside its ccmod.json. The schema is
// documented in docs/mapeditor-json.md. Parsing never throws — an unsupported format skips
// the whole file, a malformed entry or unknown flag name skips just that entry, and every
// skip leaves a warning so nothing v2-shaped can silently half-apply.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor.Model;
using Newtonsoft.Json.Linq;

namespace MobiusEditor.Headless
{
    public enum ManifestUnitKind { Vehicle, Aircraft, Vessel }

    public sealed class ManifestBuilding
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string TextId { get; set; }
        /// <summary>Literal display name for types with no game-text entry; overrides TextId when set.</summary>
        public string DisplayName { get; set; }
        public int PowerProduction { get; set; }
        public int PowerUsage { get; set; }
        public int Storage { get; set; }
        public bool Capturable { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        /// <summary>Occupancy rows separated by spaces, '0' = free cell; null = full footprint.</summary>
        public string OccupyMask { get; set; }
        public string Owner { get; set; }
        public string FactoryOverlay { get; set; }
        public int FrameOffset { get; set; }
        /// <summary>Art name when it differs from Name; null falls back to Name.</summary>
        public string GraphicsSource { get; set; }
        public int ZOrder { get; set; }
        public BuildingTypeFlag Flags { get; set; }
    }

    public sealed class ManifestUnit
    {
        public int Id { get; set; }
        public ManifestUnitKind Kind { get; set; }
        public string Name { get; set; }
        public string TextId { get; set; }
        /// <summary>Literal display name for types with no game-text entry; overrides TextId when set.</summary>
        public string DisplayName { get; set; }
        public string Owner { get; set; }
        public FrameUsage BodyFrames { get; set; }
        public FrameUsage TurretFrames { get; set; }
        public string Turret { get; set; }
        public string Turret2 { get; set; }
        public int TurretOffset { get; set; }
        public int TurretY { get; set; }
        public UnitTypeFlag Flags { get; set; }
    }

    public sealed class ManifestInfantry
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string TextId { get; set; }
        /// <summary>Literal display name for types with no game-text entry; overrides TextId when set.</summary>
        public string DisplayName { get; set; }
        public string Owner { get; set; }
        public UnitTypeFlag Flags { get; set; }
    }

    public sealed class ManifestTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        /// <summary>Per-icon land type rows (chars X,C,B,I,R,W,V,H), rows separated by spaces.</summary>
        public string Lands { get; set; }
        /// <summary>Icon usage mask, '0' removes an icon; null = all icons in use.</summary>
        public string Mask { get; set; }
    }

    public sealed class ModManifest
    {
        public const string FileName = "mapeditor.json";
        public const int SupportedFormat = 1;

        public string ModName { get; private set; }
        public string GameType { get; private set; }
        public List<ManifestBuilding> Buildings { get; } = new List<ManifestBuilding>();
        public List<ManifestUnit> Units { get; } = new List<ManifestUnit>();
        public List<ManifestInfantry> Infantry { get; } = new List<ManifestInfantry>();
        public List<ManifestTemplate> Templates { get; } = new List<ManifestTemplate>();

        /// <summary>Reads a manifest file; null (with a warning) when it cannot be used at all.</summary>
        public static ModManifest Load(string path, string modName, List<string> warnings)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception e) { warnings.Add($"{modName}: cannot read {FileName}: {e.Message}"); return null; }
            return Parse(json, modName, warnings);
        }

        /// <summary>Parses manifest JSON; null (with a warning) when the whole file must be skipped.</summary>
        public static ModManifest Parse(string json, string modName, List<string> warnings)
        {
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e) { warnings.Add($"{modName}: unparseable {FileName}: {e.Message}"); return null; }
            int format = (int?)root["format"] ?? 0;
            if (format != SupportedFormat)
            {
                warnings.Add($"{modName}: {FileName} format {format} is not supported (this editor reads format {SupportedFormat}); ignoring the file.");
                return null;
            }
            ModManifest manifest = new ModManifest
            {
                ModName = modName,
                GameType = (string)root["game_type"],
            };
            ParseEntries(root["buildings"], "building", modName, warnings, ParseBuilding, manifest.Buildings);
            ParseEntries(root["units"], "unit", modName, warnings, ParseUnit, manifest.Units);
            ParseEntries(root["infantry"], "infantry", modName, warnings, ParseInfantry, manifest.Infantry);
            ParseEntries(root["templates"], "template", modName, warnings, ParseTemplate, manifest.Templates);
            return manifest;
        }

        private static void ParseEntries<T>(JToken array, string category, string modName, List<string> warnings, Func<JObject, T> parse, List<T> into)
        {
            if (array == null) return;
            foreach (JToken token in array)
            {
                string name = (token as JObject)?["name"]?.ToString() ?? token.ToString(Newtonsoft.Json.Formatting.None);
                try { into.Add(parse((JObject)token)); }
                catch (Exception e) { warnings.Add($"{modName}: skipping {category} '{name}': {e.Message}"); }
            }
        }

        private static ManifestBuilding ParseBuilding(JObject o) => new ManifestBuilding
        {
            Id = Required<int>(o, "id"),
            Name = Required<string>(o, "name"),
            TextId = TextIdOrDisplayName(o),
            DisplayName = (string)o["display_name"],
            PowerProduction = (int?)o["power_production"] ?? 0,
            PowerUsage = (int?)o["power_usage"] ?? 0,
            Storage = (int?)o["storage"] ?? 0,
            Capturable = (bool?)o["capturable"] ?? false,
            Width = Required<int>(o, "width"),
            Height = Required<int>(o, "height"),
            OccupyMask = (string)o["occupy_mask"],
            Owner = Required<string>(o, "owner"),
            FactoryOverlay = (string)o["factory_overlay"],
            FrameOffset = (int?)o["frame_offset"] ?? 0,
            GraphicsSource = (string)o["graphics_source"],
            ZOrder = ParseZOrder((string)o["z_order"]),
            Flags = ParseFlags<BuildingTypeFlag>(o["flags"]),
        };

        private static ManifestUnit ParseUnit(JObject o) => new ManifestUnit
        {
            Id = Required<int>(o, "id"),
            Kind = ParseKind(Required<string>(o, "kind")),
            Name = Required<string>(o, "name"),
            TextId = TextIdOrDisplayName(o),
            DisplayName = (string)o["display_name"],
            Owner = Required<string>(o, "owner"),
            BodyFrames = ParseFlags<FrameUsage>(Require(o, "body_frames")),
            TurretFrames = ParseFlags<FrameUsage>(o["turret_frames"]),
            Turret = (string)o["turret"],
            Turret2 = (string)o["turret2"],
            TurretOffset = (int?)o["turret_offset"] ?? 0,
            TurretY = (int?)o["turret_y"] ?? 0,
            Flags = ParseFlags<UnitTypeFlag>(o["flags"]),
        };

        private static ManifestInfantry ParseInfantry(JObject o) => new ManifestInfantry
        {
            Id = Required<int>(o, "id"),
            Name = Required<string>(o, "name"),
            TextId = TextIdOrDisplayName(o),
            DisplayName = (string)o["display_name"],
            Owner = Required<string>(o, "owner"),
            Flags = ParseFlags<UnitTypeFlag>(o["flags"]),
        };

        private static ManifestTemplate ParseTemplate(JObject o) => new ManifestTemplate
        {
            Id = Required<int>(o, "id"),
            Name = Required<string>(o, "name"),
            Width = Required<int>(o, "width"),
            Height = Required<int>(o, "height"),
            Lands = Required<string>(o, "lands"),
            Mask = (string)o["mask"],
        };

        /// <summary>Every named entry carries text_id, display_name, or both; neither is an error.</summary>
        private static string TextIdOrDisplayName(JObject o)
        {
            string textId = (string)o["text_id"];
            if (textId == null && (string)o["display_name"] == null)
            {
                throw new FormatException("missing 'text_id' (or 'display_name')");
            }
            return textId;
        }

        private static T Required<T>(JObject o, string key)
        {
            JToken token = Require(o, key);
            return token.ToObject<T>() ?? throw new FormatException($"'{key}' is null");
        }

        private static JToken Require(JObject o, string key)
        {
            JToken token = o[key];
            if (token == null || token.Type == JTokenType.Null) throw new FormatException($"missing '{key}'");
            return token;
        }

        private static ManifestUnitKind ParseKind(string kind) => kind switch
        {
            "vehicle" => ManifestUnitKind.Vehicle,
            "aircraft" => ManifestUnitKind.Aircraft,
            "vessel" => ManifestUnitKind.Vessel,
            _ => throw new FormatException($"unknown kind '{kind}'"),
        };

        private static int ParseZOrder(string zOrder) => zOrder switch
        {
            null or "default" => Globals.ZOrderDefault,
            "paved" => Globals.ZOrderPaved,
            "flat" => Globals.ZOrderFlat,
            _ => throw new FormatException($"unknown z_order '{zOrder}'"),
        };

        /// <summary>Flag names combine by OR; matching is exact against the enum member names.</summary>
        private static T ParseFlags<T>(JToken array) where T : struct, Enum
        {
            int combined = 0;
            if (array == null) return default;
            foreach (JToken token in array)
            {
                string name = (string)token;
                if (!Enum.TryParse(name, ignoreCase: false, out T flag) || !Enum.GetNames<T>().Contains(name))
                {
                    throw new FormatException($"unknown {typeof(T).Name} '{name}'");
                }
                combined |= Convert.ToInt32(flag);
            }
            return (T)Enum.ToObject(typeof(T), combined);
        }
    }
}
