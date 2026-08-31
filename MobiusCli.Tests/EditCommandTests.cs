using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCli;
using MobiusCore.Tests;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCli.Tests
{
    /// <summary>
    /// `edit` applies ordered place/erase operations headlessly and writes the result to a
    /// new file — the same operations the GUI brushes use, so anything scriptable here
    /// behaves identically to hand-editing. It refuses to overwrite its input, and it
    /// refuses a template with no art in the map's theater instead of silently no-opping.
    /// </summary>
    public class EditCommandTests
    {
        private static (int code, string stdout, string stderr) Run(params string[] args)
        {
            StringWriter o = new StringWriter(), e = new StringWriter();
            int code = Cli.Run(args, o, e);
            return (code, o.ToString(), e.ToString());
        }

        private static readonly string Source = Path.Combine(TestPaths.MapEdits, "scm05ea.ini");

        [Fact]
        public void PlacesTemplatesAndOverlayInOrder()
        {
            string outPath = TestPaths.Output("cli-edit-place.ini");
            (int code, string stdout, string stderr) = Run("edit", Source, "--out", outPath,
                "--place", "tdsh1@10,10", "--erase", "11,11", "--place-overlay", "brik@20,20",
                "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir);
            Assert.True(code == 0, stderr);
            IGamePlugin plugin = EditorHost.Shared.Load(outPath, out _);
            Assert.Equal("tdsh1", plugin.Map.Templates[10, 10].Type.Name);
            Assert.Equal(0, plugin.Map.Templates[10, 10].Icon);
            Assert.Null(plugin.Map.Templates[11, 11]);
            Assert.Equal("brik", plugin.Map.Overlay[20, 20].Type.Name);
            Assert.Contains("3 operation(s)", stdout);
        }

        [Fact]
        public void RefusesToOverwriteTheInput()
        {
            (int code, string _, string stderr) = Run("edit", Source, "--out", Source,
                "--place", "tdsh1@10,10", "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir);
            Assert.NotEqual(0, code);
            Assert.Contains("input", stderr);
        }

        [Fact]
        public void UnknownTemplateNameIsAnError()
        {
            (int code, string _, string stderr) = Run("edit", Source, "--out", TestPaths.Output("cli-edit-unknown.ini"),
                "--place", "nosuchtile@10,10", "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir);
            Assert.NotEqual(0, code);
            Assert.Contains("nosuchtile", stderr);
        }

        [Fact]
        public void TemplateWithoutArtInTheTheaterIsAnErrorNotASilentNoOp()
        {
            // scm05ea is Temperate; tdsh51 is a TD desert-only tile, hosted in the Interior slot.
            (int code, string _, string stderr) = Run("edit", Source, "--out", TestPaths.Output("cli-edit-theater.ini"),
                "--place", "tdsh51@10,10", "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir);
            Assert.NotEqual(0, code);
            Assert.Contains("theater", stderr);
        }
    }
}
