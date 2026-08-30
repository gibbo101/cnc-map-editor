using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCli;
using MobiusCore.Tests;
using Xunit;

namespace MobiusCli.Tests
{
    public class RenderAndSaveCommandTests
    {
        private static (int code, string stdout, string stderr) Run(params string[] args)
        {
            StringWriter o = new StringWriter(), e = new StringWriter();
            int code = Cli.Run(args, o, e);
            return (code, o.ToString(), e.ToString());
        }

        [Fact]
        public void RenderWritesAPngOfTheMapBounds()
        {
            string map = Path.Combine(TestPaths.MapEdits, "scm05ea.ini");
            (int infoCode, string info, string infoErr) = Run("info", map, "--game", TestPaths.GameDir);
            Assert.True(infoCode == 0, infoErr);
            System.Text.RegularExpressions.Match bounds = System.Text.RegularExpressions.Regex.Match(info, @"bounds: \d+,\d+ (\d+)x(\d+)");
            Assert.True(bounds.Success, info);
            string png = TestPaths.Output("cli-scm05ea-bounds.png");
            (int code, string _, string stderr) = Run("render", map, png, "--scale", "0.25", "--bounds-only", "--game", TestPaths.GameDir);
            Assert.True(code == 0, stderr);
            using (Bitmap bm = new Bitmap(png))
            {
                // At 0.25 scale a cell is 32 px; the image covers exactly the map bounds.
                Assert.Equal(32 * int.Parse(bounds.Groups[1].Value), bm.Width);
                Assert.Equal(32 * int.Parse(bounds.Groups[2].Value), bm.Height);
            }
        }

        [Fact]
        public void SaveReproducesTheMonoEditorsOutput()
        {
            string name = "UGC_F1BE000000000006_0000000000000006_MAPDATA.MPR";
            string outPath = TestPaths.Output("cli-" + name);
            (int code, string _, string stderr) = Run("save", Path.Combine(TestPaths.ModDir, "CustomMaps", name), outPath, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir);
            Assert.True(code == 0, stderr);
            Assert.True(File.ReadAllBytes(outPath).SequenceEqual(File.ReadAllBytes(TestPaths.Oracle(Path.Combine("saves", name)))));
        }

        [Fact]
        public void SaveRefusesToOverwriteTheInput()
        {
            string map = Path.Combine(TestPaths.ModDir, "CustomMaps", "UGC_F1BE000000000006_0000000000000006_MAPDATA.MPR");
            (int code, string _, string stderr) = Run("save", map, map, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir);
            Assert.NotEqual(0, code);
            Assert.Contains("refusing", stderr);
        }
    }
}
