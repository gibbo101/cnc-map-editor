using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.RedAlert;
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
        public IReadOnlyList<string> ModDirs { get; }
        public MegafileManager Archives { get; }
        public GameInfo GameInfo { get; } = new GameInfoRedAlert();
        private readonly Dictionary<TheaterType, TilesetManager> tilesets = new Dictionary<TheaterType, TilesetManager>();

        public EditorSession(string gameDir, IEnumerable<string> modDirs)
        {
            GameDir = gameDir ?? throw new ArgumentNullException(nameof(gameDir));
            bool hasData = Directory.Exists(gameDir) && Directory.EnumerateDirectories(gameDir).Any(d => string.Equals(Path.GetFileName(d), "DATA", StringComparison.OrdinalIgnoreCase));
            if (!hasData) throw new DirectoryNotFoundException("Not a C&C Remastered install (no DATA folder): " + gameDir);
            ModDirs = (modDirs ?? Enumerable.Empty<string>()).ToList();
            Dictionary<GameType, string[]> modPaths = new Dictionary<GameType, string[]> { { GameType.RedAlert, ModDirs.ToArray() } };
            Dictionary<GameType, string> gameFolders = new Dictionary<GameType, string>();
            GameInfo[] infos = GameTypeFactory.GetGameInfos();
            foreach (GameInfo gi in infos.Where(g => g != null)) gameFolders[gi.GameType] = gi.ClassicFolderRemasterData;
            Archives = new MegafileManager(Path.Combine(gameDir, "DATA"), gameDir, modPaths, null, gameFolders);
            foreach (string meg in new[] { "CONFIG.MEG", "TEXTURES_COMMON_SRGB.MEG", "TEXTURES_SRGB.MEG", "TEXTURES_RA_SRGB.MEG" }) Archives.LoadArchive(meg);
            List<string> loadErrors = new List<string>(), fileLoadErrors = new List<string>();
            foreach (GameInfo gi in infos.Where(g => g != null)) gi.InitClassicFiles(Archives.ClassicFileManager, loadErrors, fileLoadErrors, true);
            Globals.TheArchiveManager = Archives;
            Globals.TheShapeCacheManager = new ShapeCacheManager();
            Globals.TheTeamColorManager = new TeamColorManager(Archives);
            Globals.TheGameTextManager = new GameTextManager(Archives, String.Format(Globals.GameTextFilenameFormat, "EN-US"));
        }

        /// <summary>The theater a map file declares, read before the plugin exists because the managers must be pointed at it first.</summary>
        public static TheaterType PeekTheater(string mapPath)
        {
            string text = File.ReadAllText(mapPath);
            return text.Contains("Theater=Interior") ? TheaterTypes.Interior : text.Contains("Theater=Snow") ? TheaterTypes.Snow : TheaterTypes.Temperate;
        }

        /// <summary>Loads a map, pointing the archive and tileset managers at its theater first.</summary>
        public IGamePlugin Load(string mapPath, out string[] errors)
        {
            if (!File.Exists(mapPath)) throw new FileNotFoundException("Map not found: " + mapPath, mapPath);
            TheaterType theater = PeekTheater(mapPath);
            Archives.Reset(GameType.RedAlert, theater);
            if (!tilesets.TryGetValue(theater, out TilesetManager manager))
            {
                manager = new TilesetManager(Archives, Globals.TilesetsXMLPath, Globals.TexturesPath);
                manager.Reset(GameType.RedAlert, theater);
                tilesets[theater] = manager;
            }
            Globals.TheTilesetManager = manager;
            foreach (TheaterType t in GameInfo.AllTheaters) t.IsRemasterTilesetFound = manager.TilesetExists(t.MainTileset);
            IGamePlugin plugin = GameInfo.CreatePlugin(false, true);
            FileType ft = FileType.INI;
            errors = plugin.Load(mapPath, mapPath, File.ReadAllBytes(mapPath), null, null, ref ft).ToArray();
            return plugin;
        }
    }
}
