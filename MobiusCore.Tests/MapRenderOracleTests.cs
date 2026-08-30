using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using MobiusEditor;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Render;
using Xunit;
using Xunit.Abstractions;

namespace MobiusCore.Tests
{
    /// <summary>
    /// The renderer spike: the Skia-backed core must reproduce the mono/GDI+ editor's map-layer
    /// render at scale 1.0. Oracles come from oracle/regen.sh.
    ///
    /// Pixels under a partially transparent sprite pixel may differ by at most 3: libgdiplus
    /// stores drawn bitmaps as premultiplied cairo surfaces, and two truncating premultiply /
    /// unpremultiply round trips on the sprite plus pixman's OVER reproduce 99.3% of the oracle's
    /// values there (the rest are one-off at very low alpha). Our pipeline blends the source art
    /// directly, so those pixels are more faithful than the oracle's. Everything else is exact.
    /// </summary>
    public class MapRenderOracleTests
    {
        private readonly ITestOutputHelper output;
        public MapRenderOracleTests(ITestOutputHelper output) { this.output = output; }

        [Theory]
        [InlineData("scm05ea.ini", "scm05ea-maplayers-1.0.png", false)]
        [InlineData("UGC_F1BE000000000006_0000000000000006_MAPDATA.MPR", "tf-map06-maplayers-1.0.png", true)]
        public void MapLayersMatchOraclePixelForPixel(string map, string oracleName, bool tfMap)
        {
            string mapPath = tfMap ? Path.Combine(TestPaths.ModDir, "CustomMaps", map) : Path.Combine(TestPaths.MapEdits, map);
            string oraclePath = TestPaths.Oracle(oracleName);
            Assert.True(File.Exists(oraclePath), "Oracle missing; run oracle/regen.sh: " + oraclePath);
            EditorHost host = new EditorHost(TestPaths.GameDir, TestPaths.ModDir);
            IGamePlugin plugin = host.Load(mapPath, out string[] errors);
            output.WriteLine("load errors: " + errors.Length);

            const double tileScale = 1.0;
            Size tileSize = new Size((int)Math.Round(Globals.OriginalTileWidth * tileScale), (int)Math.Round(Globals.OriginalTileHeight * tileScale));
            int w = plugin.Map.Metrics.Width, h = plugin.Map.Metrics.Height;
            using (Bitmap rendered = new Bitmap(w * tileSize.Width, h * tileSize.Height, PixelFormat.Format32bppArgb))
            {
                rendered.SetResolution(96, 96);
                PartialCoverageTracker tracker = new PartialCoverageTracker(rendered);
                Graphics.CoverageTracker = tracker;
                try
                {
                    using (Graphics g = Graphics.FromImage(rendered))
                    {
                        MapRenderer.Render(host.GameInfo, plugin.Map, g, null, MapLayerFlag.MapLayers, tileScale, false, Globals.TheShapeCacheManager);
                    }
                }
                finally { Graphics.CoverageTracker = null; }
                string outPath = TestPaths.Output(oracleName);
                rendered.Save(outPath, ImageFormat.Png);
                output.WriteLine("rendered to " + outPath);
                using (Bitmap oracle = new Bitmap(oraclePath))
                {
                    ImageDiff exact = ImageCompare.Compare(oracle, rendered, 0, tracker.Marks);
                    ImageDiff bounded = ImageCompare.Compare(oracle, rendered, 3);
                    output.WriteLine("outside partial-alpha coverage: " + exact);
                    output.WriteLine("anywhere, beyond tolerance 3: " + bounded);
                    Assert.True(exact.Differing == 0, "Pixels differ outside partial-alpha sprite coverage: " + exact);
                    Assert.True(bounded.Differing == 0, "Pixels differ by more than the blend-rounding bound: " + bounded);
                }
            }
        }

    }
}
