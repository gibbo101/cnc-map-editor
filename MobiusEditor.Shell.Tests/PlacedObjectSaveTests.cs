using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>Objects the editor creates (not just ones it loaded) survive a save/reload round trip.</summary>
    public class PlacedObjectSaveTests
    {
        [Fact]
        public void EditorPlacedObjectsSurviveSaveAndReload()
        {
            using (MapDocument doc = new MapDocument(EditorHost.Shared))
            {
                doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
                doc.PlacementHouse = doc.Map.HouseTypes.First(h => h.Name == "USSR");
                BuildingType buildingType = doc.AvailableBuildings().First(b => b.HasBib);
                UnitType unitType = doc.AvailableUnits().First(t => t.IsGroundUnit);
                InfantryType infantryType = doc.AvailableInfantry().First();
                TerrainType terrainType = doc.AvailableTerrain().First();
                Assert.NotNull(doc.PlaceBuilding(new Point(40, 40), buildingType));
                Assert.NotNull(doc.PlaceUnit(new Point(44, 40), unitType));
                Assert.NotNull(doc.PlaceInfantry(new Point(45, 40), infantryType));
                Assert.NotNull(doc.PlaceTerrain(new Point(46, 40), terrainType));
                string outDir = TestPaths.Output("placed-objects");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "placed.ini");
                doc.Save(outPath);

                using (MapDocument reloaded = new MapDocument(EditorHost.Shared))
                {
                    reloaded.Open(outPath);
                    Building building = Assert.IsType<Building>(reloaded.Map.Buildings[new Point(40, 40)]);
                    Assert.Same(reloaded.Map.BuildingTypes.First(b => b.Name == buildingType.Name), building.Type);
                    Assert.Equal("USSR", building.House.Name);
                    Unit unit = Assert.IsType<Unit>(reloaded.Map.Technos[new Point(44, 40)]);
                    Assert.Equal(unitType.Name, unit.Type.Name);
                    InfantryGroup group = Assert.IsType<InfantryGroup>(reloaded.Map.Technos[new Point(45, 40)]);
                    Assert.Equal(infantryType.Name, group.Infantry.Single(i => i != null).Type.Name);
                    Assert.Contains(reloaded.Map.GetAllTechnos().OfType<Terrain>(), t => t.Type.Name == terrainType.Name);
                }
            }
        }
    }
}
