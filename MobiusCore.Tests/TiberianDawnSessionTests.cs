using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>The headless session serves Tiberian Dawn too: INI + BIN community maps from the game install.</summary>
    public class TiberianDawnSessionTests
    {
        public static string CommunityMap => Path.Combine(TestPaths.GameDir, "Data/CNCDATA/TIBERIAN_DAWN/COMMUNITY/SCMC0EA.INI");

        [Fact]
        public void LoadsACommunityMapWithItsBin()
        {
            EditorSession session = EditorHost.SharedFor(GameType.TiberianDawn);
            IGamePlugin plugin = session.Load(CommunityMap, out string[] errors);
            Assert.Equal(GameType.TiberianDawn, plugin.GameInfo.GameType);
            Assert.Empty(errors);
            Assert.True(plugin.Map.Templates.Count() > 100, "map has no terrain; BIN not loaded?");
            Assert.Equal(64, plugin.Map.Metrics.Width);
            Assert.Equal(64, plugin.Map.Metrics.Height);
            Assert.True(plugin.Map.Bounds.Width > 10);
        }
    }
}
