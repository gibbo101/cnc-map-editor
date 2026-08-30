using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.Headless
{
    /// <summary>
    /// The editor core brought up without a display: one game install plus an ordered list of
    /// mod folders (last wins, as the game resolves them). Loads maps into plugins that can be
    /// queried, rendered and saved.
    /// </summary>
    public sealed class EditorSession
    {
        public string GameDir { get; }
        public GameType GameType { get; }
        public IReadOnlyList<string> ModDirs { get; }
        public MegafileManager Archives { get; }
        public GameInfo GameInfo { get; }
        private readonly Dictionary<TheaterType, TilesetManager> tilesets = new Dictionary<TheaterType, TilesetManager>();

        public EditorSession(string gameDir, IEnumerable<string> modDirs) : this(gameDir, GameType.RedAlert, modDirs) { }

        public EditorSession(string gameDir, GameType gameType, IEnumerable<string> modDirs)
        {
            GameDir = gameDir ?? throw new ArgumentNullException(nameof(gameDir));
            GameType = gameType;
            GameInfo = GameTypeFactory.GetGameInfos().FirstOrDefault(g => g != null && g.GameType == gameType) ?? throw new ArgumentException("Unsupported game " + gameType);
            bool hasData = Directory.Exists(gameDir) && Directory.EnumerateDirectories(gameDir).Any(d => string.Equals(Path.GetFileName(d), "DATA", StringComparison.OrdinalIgnoreCase));
            if (!hasData) throw new DirectoryNotFoundException("Not a C&C Remastered install (no DATA folder): " + gameDir);
            ModDirs = (modDirs ?? Enumerable.Empty<string>()).ToList();
            Dictionary<GameType, string[]> modPaths = new Dictionary<GameType, string[]> { { gameType, ModDirs.ToArray() } };
            Dictionary<GameType, string> gameFolders = new Dictionary<GameType, string>();
            GameInfo[] infos = GameTypeFactory.GetGameInfos();
            foreach (GameInfo gi in infos.Where(g => g != null)) gameFolders[gi.GameType] = gi.ClassicFolderRemasterData;
            Archives = new MegafileManager(Path.Combine(gameDir, "DATA"), gameDir, modPaths, null, gameFolders);
            foreach (string meg in GameInfo.RemasterMegFiles) Archives.LoadArchive(meg);
            List<string> loadErrors = new List<string>(), fileLoadErrors = new List<string>();
            foreach (GameInfo gi in infos.Where(g => g != null)) gi.InitClassicFiles(Archives.ClassicFileManager, loadErrors, fileLoadErrors, true);
            Globals.TheArchiveManager = Archives;
            Globals.TheShapeCacheManager = new ShapeCacheManager();
            Globals.TheTeamColorManager = new TeamColorManager(Archives);
            Globals.TheGameTextManager = new GameTextManager(Archives, String.Format(Globals.GameTextFilenameFormat, "EN-US"));
        }

        /// <summary>The theater a map file declares, read before the plugin exists because the managers must be pointed at it first.</summary>
        public TheaterType PeekTheater(string mapPath)
        {
            string text = File.ReadAllText(mapPath);
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, @"(?im)^\s*Theater\s*=\s*([A-Za-z]+)");
            TheaterType[] theaters = GameInfo.AllTheaters.ToArray();
            if (m.Success)
            {
                TheaterType found = theaters.FirstOrDefault(t => string.Equals(t.Name, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }
            return theaters[0];
        }

        /// <summary>The classic TD/RA .bin terrain file beside an .ini, whatever its case; null when absent.</summary>
        private static string SiblingBin(string mapPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(mapPath));
            string stem = Path.GetFileNameWithoutExtension(mapPath);
            return Directory.EnumerateFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetExtension(f), ".bin", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Loads a map, pointing the archive and tileset managers at its theater first.</summary>
        public IGamePlugin Load(string mapPath, out string[] errors)
        {
            if (!File.Exists(mapPath)) throw new FileNotFoundException("Map not found: " + mapPath, mapPath);
            TheaterType theater = PeekTheater(mapPath);
            Archives.Reset(GameType, theater);
            if (!tilesets.TryGetValue(theater, out TilesetManager manager))
            {
                manager = new TilesetManager(Archives, Globals.TilesetsXMLPath, Globals.TexturesPath);
                manager.Reset(GameType, theater);
                tilesets[theater] = manager;
            }
            Globals.TheTilesetManager = manager;
            foreach (TheaterType t in GameInfo.AllTheaters) t.IsRemasterTilesetFound = manager.TilesetExists(t.MainTileset);
            FileType ft = FileType.INI;
            string binPath = GameType == GameType.RedAlert ? null : SiblingBin(mapPath);
            byte[] binContent = binPath == null ? null : File.ReadAllBytes(binPath);
            // Classic TD terrain is 64x64 (8 KiB of cells); the editor's 128x128 "megamap" .bin is four times that.
            bool megaMap = GameType == GameType.RedAlert || (binContent != null && binContent.Length >= 128 * 128 * 2);
            IGamePlugin plugin = GameInfo.CreatePlugin(false, megaMap);
            errors = plugin.Load(mapPath, mapPath, File.ReadAllBytes(mapPath), binPath, binContent, ref ft).ToArray();
            return plugin;
        }
    }
}
