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
    /// `generate` writes a random skirmish map — assigned player starts, trees, resource
    /// fields — that loads back cleanly; the same seed reproduces it byte for byte.
    /// </summary>
    public class GenerateCommandTests
    {
        private static (int code, string stdout, string stderr) Run(params string[] args)
        {
            StringWriter o = new StringWriter(), e = new StringWriter();
            int code = Cli.Run(args, o, e);
            return (code, o.ToString(), e.ToString());
        }

        [Fact]
        public void WritesALoadableRandomMapReproducibleFromItsSeed()
        {
            string first = TestPaths.Output("cli-gen-a.mpr");
            string second = TestPaths.Output("cli-gen-b.mpr");
            File.Delete(first);
            File.Delete(second);
            (int code, string stdout, string stderr) = Run("generate", first, "--theater", "Temperate",
                "--seed", "42", "--players", "4", "--game", TestPaths.GameDir);
            Assert.True(code == 0, stderr);
            Assert.Contains("4 starts", stdout);
            (int code2, _, string stderr2) = Run("generate", second, "--theater", "Temperate",
                "--seed", "42", "--players", "4", "--game", TestPaths.GameDir);
            Assert.True(code2 == 0, stderr2);
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));

            IGamePlugin plugin = EditorHost.Shared.Load(first, out string[] errors);
            Assert.Empty(errors);
            Assert.Equal(4, plugin.Map.Waypoints.Count(w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue));
            Assert.NotEmpty(plugin.Map.Technos.Occupiers.OfType<Terrain>());
            Assert.Contains(plugin.Map.Overlay, c => c.Value?.Type.IsResource == true);
        }
    }
}
