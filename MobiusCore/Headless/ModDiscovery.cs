using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor.Utility;
using Newtonsoft.Json.Linq;

namespace MobiusEditor.Headless
{
    /// <summary>One installed mod, as described by its ccmod.json.</summary>
    public sealed class ModInfo
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string GameType { get; set; }
        public int LoadOrder { get; set; }
        public string Version { get; set; }
        /// <summary>Folder name, which is also how the game and the Workshop identify the mod.</summary>
        public string Folder { get; set; }
        public string Path { get; set; }
        public string Source { get; set; }
        /// <summary>Full path of the mod's mapeditor.json, when it ships one; null otherwise.</summary>
        public string EditorManifestPath { get; set; }
        public override string ToString() => $"{Name} ({Version}) [{GameType}, load {LoadOrder}] {Path}";
    }

    /// <summary>Finds installed mods: every folder holding a ccmod.json under a Mods root or in the Steam Workshop cache.</summary>
    public static class ModDiscovery
    {
        public const string ManifestName = "ccmod.json";
        private static readonly Dictionary<string, string> GameFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "RA", "Red_Alert" }, { "TD", "Tiberian_Dawn" } };

        /// <summary>Reads one mod folder; null when it holds no manifest.</summary>
        public static ModInfo Read(string modDir, string source = "folder")
        {
            string manifest = FindFile(modDir, ManifestName);
            if (manifest == null) return null;
            JObject json;
            try { json = JObject.Parse(File.ReadAllText(manifest)); }
            catch (Exception) { return null; }
            return new ModInfo
            {
                Name = (string)json["name"] ?? System.IO.Path.GetFileName(modDir),
                Author = (string)json["author"],
                GameType = (string)json["game_type"],
                LoadOrder = (int?)json["load_order"] ?? 0,
                Version = ((int?)json["version_high"] ?? 0) + "." + ((int?)json["version_low"] ?? 0),
                Folder = System.IO.Path.GetFileName(modDir.TrimEnd(System.IO.Path.DirectorySeparatorChar)),
                Path = System.IO.Path.GetFullPath(modDir),
                Source = source,
                EditorManifestPath = FindFile(modDir, ModManifest.FileName),
            };
        }

        private static string FindFile(string dir, string fileName) =>
            Directory.Exists(dir) ? Directory.EnumerateFiles(dir).FirstOrDefault(f => string.Equals(System.IO.Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase)) : null;

        /// <summary>Mods for one game under a Mods root (Mods/Red_Alert/*, Mods/Tiberian_Dawn/*), in load order.</summary>
        public static IEnumerable<ModInfo> Scan(string modsRoot, string gameType)
        {
            if (!GameFolders.TryGetValue(gameType, out string gameFolder)) throw new ArgumentException("Unknown game type " + gameType);
            string dir = Directory.Exists(modsRoot) ? Directory.EnumerateDirectories(modsRoot).FirstOrDefault(d => string.Equals(System.IO.Path.GetFileName(d), gameFolder, StringComparison.OrdinalIgnoreCase)) : null;
            if (dir == null) return Enumerable.Empty<ModInfo>();
            return Directory.EnumerateDirectories(dir).Select(d => Read(d, "mods")).Where(m => m != null).OrderBy(m => m.LoadOrder).ThenBy(m => m.Folder, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Subscribed Workshop items for one game from the Steam download cache.</summary>
        public static IEnumerable<ModInfo> ScanWorkshop(string steamAppId, string gameType)
        {
            string[] libraries = SteamAssist.GetLibraryFoldersForAppId(steamAppId);
            if (libraries == null) return Enumerable.Empty<ModInfo>();
            return ScanWorkshopContent(libraries.Select(l => System.IO.Path.Combine(l, "steamapps", "workshop", "content", steamAppId)), gameType);
        }

        /// <summary>Scans Workshop content folders (each holding one directory per item id).</summary>
        public static IEnumerable<ModInfo> ScanWorkshopContent(IEnumerable<string> contentFolders, string gameType)
        {
            List<ModInfo> found = new List<ModInfo>();
            foreach (string content in contentFolders)
            {
                if (!Directory.Exists(content)) continue;
                foreach (string item in Directory.EnumerateDirectories(content))
                {
                    // A Workshop item is <itemId>/<ModFolder>/ccmod.json; tolerate a manifest at the item root too.
                    string source = "workshop:" + System.IO.Path.GetFileName(item);
                    IEnumerable<string> candidates = new[] { item }.Concat(Directory.EnumerateDirectories(item));
                    foreach (string dir in candidates)
                    {
                        ModInfo mod = Read(dir, source);
                        if (mod != null && string.Equals(mod.GameType, gameType, StringComparison.OrdinalIgnoreCase)) { found.Add(mod); break; }
                    }
                }
            }
            return found.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Where the game keeps user mods: the Proton prefix's Documents on Linux, My Documents on Windows.</summary>
        public static string DefaultModsRoot(string steamAppId)
        {
            string documents;
            if (OperatingSystem.IsWindows()) documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            else
            {
                string steam = SteamAssist.GetSteamFolder();
                if (steam == null) return null;
                documents = System.IO.Path.Combine(steam, "steamapps", "compatdata", steamAppId, "pfx", "drive_c", "users", "steamuser", "Documents");
            }
            return System.IO.Path.Combine(documents, "CnCRemastered", "Mods");
        }
    }
}
