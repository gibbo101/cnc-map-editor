using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// Map settings (the [Basic] section plus the briefing) edit as one undo step, and the
    /// solo-mission flag flips which save rules apply: a solo map wants the Home waypoint
    /// placed instead of two player starts.
    /// </summary>
    public class MapSettingsTests
    {
        [Fact]
        public void SettingsEditAsOneUndoStep()
        {
            using (MapDocument doc = new MapDocument(EditorHost.Shared))
            {
                doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
                string originalName = doc.Map.BasicSection.Name;
                doc.EditMapSettings(() =>
                {
                    doc.Map.BasicSection.Name = "My Mission";
                    doc.Map.BasicSection.Author = "Luke";
                    doc.Map.BasicSection.SoloMission = true;
                    doc.Map.BriefingSection.Briefing = "Destroy everything.";
                });
                Assert.Equal("My Mission", doc.Map.BasicSection.Name);
                Assert.True(doc.Map.BasicSection.SoloMission);
                doc.Undo();
                Assert.Equal(originalName, doc.Map.BasicSection.Name);
                Assert.False(doc.Map.BasicSection.SoloMission);
                Assert.NotEqual("Destroy everything.", doc.Map.BriefingSection.Briefing);
                doc.Redo();
                Assert.Equal("My Mission", doc.Map.BasicSection.Name);
                Assert.Equal("Destroy everything.", doc.Map.BriefingSection.Briefing);
            }
        }

        [Fact]
        public void SoloMissionSwitchesTheSaveRules()
        {
            using (MapDocument doc = new MapDocument(EditorHost.Shared))
            {
                doc.NewMap("Temperate");
                // Skirmish: needs two start waypoints.
                Assert.Contains("starting locations", doc.Plugin.Validate(FileType.INI, false, false) ?? "");
                doc.EditMapSettings(() => doc.Map.BasicSection.SoloMission = true);
                // Solo: needs the Home waypoint instead.
                Assert.Contains("Home waypoint", doc.Plugin.Validate(FileType.INI, false, false) ?? "");
                Waypoint home = doc.Map.Waypoints.First(w => w.Flags.HasFlag(WaypointFlag.Home));
                doc.Map.Metrics.GetCell(new System.Drawing.Point(doc.Map.Bounds.Left + 3, doc.Map.Bounds.Top + 3), out int cell);
                home.Cell = cell;
                Assert.Null(doc.Plugin.Validate(FileType.INI, false, false));
            }
        }
    }
}
