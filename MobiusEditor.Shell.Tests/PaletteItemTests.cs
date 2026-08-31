using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// Palette items carry what a picker needs: a friendly label (display names for objects,
    /// the tile code plus its size for templates), the preview thumbnail the types render
    /// during theater init — present headlessly, since InitTheater runs on every load — and
    /// the footprint in cells for the placement ghost.
    /// </summary>
    public class PaletteItemTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        [Fact]
        public void TemplatesLabelWithTheirSizeAndCarryThumbnails()
        {
            using (MapDocument doc = Open())
            {
                TemplateType multi = doc.AvailableTemplates().First(t => t.IconWidth > 1 || t.IconHeight > 1);
                PaletteItem item = PaletteItem.From(multi);
                Assert.Contains($"({multi.IconWidth}×{multi.IconHeight})", item.Label);
                Assert.NotNull(item.Thumbnail);
                Assert.Equal(multi.IconWidth, item.FootprintCells.Width);
                Assert.Equal(multi.IconHeight, item.FootprintCells.Height);
            }
        }

        [Fact]
        public void ObjectsUseDisplayNamesAndFootprints()
        {
            using (MapDocument doc = Open())
            {
                UnitType tank = doc.AvailableUnits().First(t => t.IsGroundUnit);
                PaletteItem unit = PaletteItem.From(tank);
                Assert.Equal(tank.DisplayName, unit.Label);
                Assert.NotEqual(tank.Name, unit.Label.ToLowerInvariant());
                Assert.NotNull(unit.Thumbnail);
                Assert.Equal(new System.Drawing.Size(1, 1), unit.FootprintCells);

                BuildingType fact = doc.AvailableBuildings().First(b => b.Size.Width > 1);
                PaletteItem building = PaletteItem.From(fact);
                Assert.Equal(fact.DisplayName, building.Label);
                Assert.Equal(fact.Size, building.FootprintCells);
                Assert.NotNull(building.Thumbnail);
            }
        }
    }
}
