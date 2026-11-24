using System.Drawing;
using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Render;
using MobiusEditor.Utility;
using MobiusEditor;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// The launcher draws a map's TILEPATCHES (an index keyed by the map's file name, 8x8-cell
    /// patches at bounds-relative positions, one texture per cell) over its terrain. The editor
    /// reads them from the game's archives, a mod's loose files and a mod's own MEGs.
    /// </summary>
    public class TilePatchTests
    {
        private static string TsTheatres => TestPaths.Oracle(Path.Combine("inputs", "ts-theatres"));
        private static string TsMap => Path.Combine(TsTheatres, "CCDATA", "TFTS1036.INI");

        private static EditorSession TsSession() => new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { TestPaths.ModDir, TsTheatres });

        [Fact]
        public void AModsOwnMegIsMountedBesideTheGamesArchives()
        {
            EditorSession session = TsSession();
            Assert.True(session.Archives.FileExists("DATA\\XML\\TILEPATCHES\\MAPS\\RA\\TFTS1036_MAPPATCHES.XML"));
        }

        [Fact]
        public void AModGivenByARelativePathStillMountsItsMeg()
        {
            string relative = Path.GetRelativePath(System.Environment.CurrentDirectory, TsTheatres);
            EditorSession session = new EditorSession(TestPaths.GameDir, GameType.RedAlert, new[] { relative });
            Assert.True(session.Archives.FileExists("DATA\\XML\\TILEPATCHES\\MAPS\\RA\\TFTS1036_MAPPATCHES.XML"));
        }

        [Fact]
        public void AConvertedMapsPatchesResolveToOneTexturePerCell()
        {
            EditorSession session = TsSession();
            var patches = TilePatchIndex.Load(session.Archives, "RA", "TFTS1036");
            Assert.Equal(3010, patches.Count);
            Assert.All(patches.Values, path => Assert.True(session.Archives.FileExists(path), path));
            // Patch 0002 sits at bounds-relative (8,0); its tile 3_2 is cell (11,2).
            Assert.Equal("DATA\\ART\\TEXTURES\\SRGB\\PATCHES\\RA_TFTS1036_0002\\PA0002.TEM-3_2-00.DDS",
                patches[new Point(11, 2)].ToUpperInvariant());
        }

        [Fact]
        public void TheGamesOwnSkirmishMapsHavePatchesToo()
        {
            var patches = TilePatchIndex.Load(EditorHost.Shared.Archives, "RA", "scm01ea");
            Assert.NotEmpty(patches);
            Assert.All(patches.Values, path => Assert.True(EditorHost.Shared.Archives.FileExists(path), path));
        }

        [Fact]
        public void AMapWithoutAnIndexHasNoPatches()
        {
            Assert.Empty(TilePatchIndex.Load(EditorHost.Shared.Archives, "RA", "UGC_F1BE000000000006_0000000000000006_MAPDATA"));
        }

        [Fact]
        public void TheLoadedMapCarriesItsPatchesAndTheLayerDrawsThem()
        {
            EditorSession session = TsSession();
            IGamePlugin plugin = session.Load(TsMap, out _);
            Map map = plugin.Map;
            Assert.Equal(3010, map.TilePatches.Count);
            Point cell = new Point(map.Bounds.X + 11, map.Bounds.Y + 2);
            using (Bitmap plain = Render(plugin, MapLayerFlag.Template))
            using (Bitmap patched = Render(plugin, MapLayerFlag.Template | MapLayerFlag.TilePatches))
            {
                // The cell's centre pixel at 32 px a cell: the patch replaces the land-type template.
                int px = cell.X * 32 + 16, py = cell.Y * 32 + 16;
                Assert.NotEqual(plain.GetPixel(px, py).ToArgb(), patched.GetPixel(px, py).ToArgb());
                // Outside the bounds there are no patches.
                Assert.Equal(plain.GetPixel(16, 16).ToArgb(), patched.GetPixel(16, 16).ToArgb());
            }
        }

        private static Bitmap Render(IGamePlugin plugin, MapLayerFlag layers)
        {
            Map map = plugin.Map;
            Bitmap bm = new Bitmap(map.Metrics.Width * 32, map.Metrics.Height * 32);
            using (Graphics g = Graphics.FromImage(bm))
            {
                MapRenderer.Render(plugin.GameInfo, map, g, null, layers, 0.25, false, Globals.TheShapeCacheManager);
            }
            return bm;
        }
    }
}
