using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// TS walker sprites pack the body as facing blocks of walk_frames (the DLL's gait
    /// contract): the standing shape of a facing is the first frame of its block, and a
    /// shared-image turret block starts right after the whole body run. The renderer must
    /// pick those shapes, not the plain facing index.
    /// </summary>
    public class WalkerFrameTests
    {
        private static int RenderedFrame(MapDocument doc, string typeName)
        {
            UnitType type = doc.AvailableUnits().First(u => u.Name == typeName);
            Unit unit = doc.PlaceUnit(new Point(20, 20), type);
            Assert.NotNull(unit);
            unit.Direction = doc.Map.UnitDirectionTypes.First(d => d.Facing == FacingType.East);
            using (doc.Render()) { }
            int frame = unit.DrawFrameCache;
            doc.EraseUnitAt(new Point(20, 20));
            return frame;
        }

        [Fact]
        public void WalkersStandOnTheFirstFrameOfTheirFacingBlock()
        {
            using (MapDocument doc = new MapDocument(EditorHost.Shared))
            {
                doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
                // East is CCW facing index 6 of 8 (Titan: 8 facings x 12 walk frames),
                // and CCW index 24 of 32 (Mammoth Mk. II: 32 facings x 8 walk frames).
                Assert.Equal(6 * 12, RenderedFrame(doc, "tstitn"));
                Assert.Equal(24 * 8, RenderedFrame(doc, "tshmec"));
                // A plain 32-frame unit is unaffected.
                Assert.Equal(24, RenderedFrame(doc, "tshvr"));
            }
        }
    }
}
