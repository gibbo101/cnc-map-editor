using System;
using System.IO;

namespace MobiusCore.Tests
{
    /// <summary>Where the game, the mod and the oracle renders live on the machine running the tests.</summary>
    public static class TestPaths
    {
        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string GameDir => Environment.GetEnvironmentVariable("CNC_GAME_DIR") ?? MobiusEditor.Utility.SteamAssist.TryGetSteamGameFolder(MobiusEditor.Program.RemasterSteamId) ?? Path.Combine(Home, ".steam/steam/steamapps/common/CnCRemastered");
        public static string ModDir => Environment.GetEnvironmentVariable("CNC_TF_MOD_DIR") ?? Path.Combine(Home, "Documents/development/cnc-remastered-mods/cnc-ra-tiberian-factions/build/remaster/Vanilla_RA");
        /// <summary>The Tiberian Factions map pack the save and render oracles were made from.</summary>
        public static string TfMaps => Environment.GetEnvironmentVariable("CNC_TF_MAPS_DIR") ?? Oracle(Path.Combine("inputs", "tf-maps"));
        /// <summary>The 230 official skirmish maps, pristine from this machine's game install.</summary>
        public static string OfficialMapsDir => officialMaps.Value;
        public static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        public static string Oracle(string name) => Path.Combine(RepoRoot, "oracle", name);
        public static string Output(string name)
        {
            string dir = Path.Combine(RepoRoot, "artifacts", "test-output");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }

        static readonly Lazy<string> officialMaps = new Lazy<string>(ExtractOfficialMaps);

        // Test projects can run side by side, so the folder appears whole or not at all.
        static string ExtractOfficialMaps()
        {
            string dir = Output("official-maps");
            string marker = Path.Combine(dir, ".complete");
            if (File.Exists(marker)) return dir;
            string staging = $"{dir}.staging-{Environment.ProcessId}";
            MobiusEditor.Headless.OfficialMaps.Extract(GameDir, staging);
            File.WriteAllText(Path.Combine(staging, ".complete"), "");
            try { Directory.Move(staging, dir); }
            catch (IOException) when (File.Exists(marker)) { Directory.Delete(staging, true); }
            return dir;
        }
    }
}
