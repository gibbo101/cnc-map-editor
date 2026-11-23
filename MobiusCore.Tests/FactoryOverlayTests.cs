using System.IO;
using System.Linq;
using MobiusEditor.Interface;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// A factory whose overlay art is listed in the mod's tileset XML but missing from its build
    /// still draws its body, and the building types initialised after it still get thumbnails.
    /// </summary>
    public class FactoryOverlayTests
    {
        [Fact]
        public void EveryModBuildingGetsAThumbnail()
        {
            IGamePlugin plugin = EditorHost.Shared.Load(Path.Combine(TestPaths.OfficialMapsDir, "scm01ea.ini"), out _);
            string[] missing = plugin.Map.BuildingTypes.Where(b => b.ModSource != null && b.Thumbnail == null).Select(b => b.Name).ToArray();
            Assert.Empty(missing);
        }
    }
}
