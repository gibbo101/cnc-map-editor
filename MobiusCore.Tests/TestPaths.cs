using System;
using System.IO;

namespace MobiusCore.Tests
{
    /// <summary>Where the game, the mod and the oracle renders live on the machine running the tests.</summary>
    public static class TestPaths
    {
        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string GameDir => Environment.GetEnvironmentVariable("CNC_GAME_DIR") ?? Path.Combine(Home, ".steam/steam/steamapps/common/CnCRemastered");
        public static string ModDir => Environment.GetEnvironmentVariable("CNC_TF_MOD_DIR") ?? Path.Combine(Home, "Documents/development/cnc-remastered-mods/cnc-ra-tiberian-factions/build/remaster/Vanilla_RA");
        public static string MapEdits => Path.Combine(Home, "Documents/development/cnc-remastered-mods/map-edits");
        public static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        public static string Oracle(string name) => Path.Combine(RepoRoot, "oracle", name);
        public static string Output(string name)
        {
            string dir = Path.Combine(RepoRoot, "artifacts", "test-output");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }
    }
}
