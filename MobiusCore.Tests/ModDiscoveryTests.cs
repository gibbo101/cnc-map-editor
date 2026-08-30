using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>Profiles are built from installed mods: every folder with a ccmod.json under a Mods root or the Workshop cache.</summary>
    public class ModDiscoveryTests
    {
        private static string FakeModsRoot()
        {
            string root = TestPaths.Output("fake-mods");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            void Mod(string game, string folder, string json)
            {
                string dir = Path.Combine(root, game, folder);
                Directory.CreateDirectory(Path.Combine(dir, "Data"));
                File.WriteAllText(Path.Combine(dir, "ccmod.json"), json);
            }
            Mod("Red_Alert", "Alpha", "{ \"name\": \"Alpha Mod\", \"author\": \"a\", \"load_order\": 2, \"version_high\": 1, \"version_low\": 3, \"game_type\": \"RA\" }");
            Mod("Red_Alert", "Beta", "{ \"name\": \"Beta Mod\", \"author\": \"b\", \"load_order\": 1, \"version_high\": 4, \"version_low\": 0, \"game_type\": \"RA\" }");
            Mod("Tiberian_Dawn", "Gamma", "{ \"name\": \"Gamma\", \"author\": \"g\", \"load_order\": 1, \"version_high\": 0, \"version_low\": 1, \"game_type\": \"TD\" }");
            Directory.CreateDirectory(Path.Combine(root, "Red_Alert", "NotAMod"));
            return root;
        }

        [Fact]
        public void FindsModsForOneGameOrderedByLoadOrder()
        {
            ModInfo[] mods = ModDiscovery.Scan(FakeModsRoot(), "RA").ToArray();
            Assert.Equal(new[] { "Beta Mod", "Alpha Mod" }, mods.Select(m => m.Name));
            Assert.Equal("4.0", mods[0].Version);
            Assert.Equal("1.3", mods[1].Version);
            Assert.EndsWith(Path.Combine("Red_Alert", "Beta"), mods[0].Path);
            Assert.Equal("Beta", mods[0].Folder);
        }

        [Fact]
        public void FindsWorkshopItemsOneLevelDown()
        {
            string content = TestPaths.Output("fake-workshop");
            if (Directory.Exists(content)) Directory.Delete(content, true);
            Directory.CreateDirectory(Path.Combine(content, "123456", "CoolMod", "Data"));
            File.WriteAllText(Path.Combine(content, "123456", "CoolMod", "ccmod.json"), "{ \"name\": \"Cool\", \"game_type\": \"RA\", \"version_high\": 1, \"version_low\": 0 }");
            Directory.CreateDirectory(Path.Combine(content, "777", "TdOnly"));
            File.WriteAllText(Path.Combine(content, "777", "TdOnly", "ccmod.json"), "{ \"name\": \"TD thing\", \"game_type\": \"TD\" }");
            ModInfo[] mods = ModDiscovery.ScanWorkshopContent(new[] { content }, "RA").ToArray();
            ModInfo cool = Assert.Single(mods);
            Assert.Equal("Cool", cool.Name);
            Assert.Equal("workshop:123456", cool.Source);
            Assert.Equal("CoolMod", cool.Folder);
        }

        [Fact]
        public void IgnoresFoldersWithoutAManifest()
        {
            Assert.DoesNotContain(ModDiscovery.Scan(FakeModsRoot(), "RA"), m => m.Folder == "NotAMod");
        }

        [Fact]
        public void FindsTheTiberianFactionsBuildWhenPointedAtIt()
        {
            ModInfo mod = ModDiscovery.Read(TestPaths.ModDir);
            Assert.NotNull(mod);
            Assert.Contains("Tiberian Factions", mod.Name);
            Assert.Equal("RA", mod.GameType);
        }
    }
}
