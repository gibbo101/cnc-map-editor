using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.RedAlert;
using MobiusEditor.Utility;

namespace MobiusCore.Tests
{
    /// <summary>Brings the editor core up headlessly for one game folder and one mod, like the mono RenderProbe does.</summary>
    public sealed class EditorHost
    {
        private static readonly object gate = new object();
        private static EditorHost shared;

        /// <summary>One host for the whole test run: loading the game archives costs seconds, and Load() re-points the managers per map.</summary>
        public static EditorHost Shared
        {
            get { lock (gate) { return shared ?? (shared = new EditorHost(TestPaths.GameDir, TestPaths.ModDir)); } }
        }

        public MegafileManager Archives { get; }
        public GameInfo GameInfo { get; } = new GameInfoRedAlert();

        public EditorHost(string gameDir, string modDir)
        {
            Dictionary<GameType, string[]> modPaths = new Dictionary<GameType, string[]> { { GameType.RedAlert, modDir == null ? new string[0] : new[] { modDir } } };
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

        /// <summary>Loads a map, pointing the archive and tileset managers at the map's theater first.</summary>
        public IGamePlugin Load(string mapPath, out string[] errors)
        {
            string text = File.ReadAllText(mapPath);
            TheaterType theater = text.Contains("Theater=Interior") ? TheaterTypes.Interior : text.Contains("Theater=Snow") ? TheaterTypes.Snow : TheaterTypes.Temperate;
            Archives.Reset(GameType.RedAlert, theater);
            TilesetManager tilesets = new TilesetManager(Archives, Globals.TilesetsXMLPath, Globals.TexturesPath);
            tilesets.Reset(GameType.RedAlert, theater);
            Globals.TheTilesetManager = tilesets;
            foreach (TheaterType t in GameInfo.AllTheaters) t.IsRemasterTilesetFound = tilesets.TilesetExists(t.MainTileset);
            IGamePlugin plugin = GameInfo.CreatePlugin(false, true);
            FileType ft = FileType.INI;
            errors = plugin.Load(mapPath, mapPath, File.ReadAllBytes(mapPath), null, null, ref ft).ToArray();
            return plugin;
        }
    }
}
