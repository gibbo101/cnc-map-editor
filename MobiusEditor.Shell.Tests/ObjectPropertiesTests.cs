using System.Drawing;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using MobiusEditor.Utility;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// Property editing of placed objects, with the fork's ObjectProperties rules: units and
    /// infantry offer unit directions, missions and unit-eligible triggers (aircraft never
    /// get a trigger); buildings gate house/strength/direction/trigger behind IsPrebuilt,
    /// show direction only with a turret, and carry the RA extras (base priority, prebuilt,
    /// sellable, rebuild) with the fork's normalization — a non-prebuilt building belongs to
    /// the base house at full strength with nothing attached, and one outside the rebuild
    /// base can never be non-prebuilt. Every edit lands as one undo step.
    /// </summary>
    public class ObjectPropertiesTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        [Fact]
        public void ObjectAtFindsEachPlacedKindAndNullOnEmpty()
        {
            using (MapDocument doc = Open())
            {
                Assert.Null(doc.ObjectAt(new Point(20, 20)));
                Unit unit = doc.PlaceUnit(new Point(20, 20), doc.AvailableUnits().First(t => t.IsGroundUnit));
                Assert.Same(unit, doc.ObjectAt(new Point(20, 20)));
                Infantry man = doc.PlaceInfantry(new Point(21, 20), doc.AvailableInfantry().First());
                Assert.Same(man, doc.ObjectAt(new Point(21, 20)));
                Building building = doc.PlaceBuilding(new Point(30, 30), doc.AvailableBuildings().First(b => b.HasBib));
                Assert.Same(building, doc.ObjectAt(doc.Map.Buildings[building].Value));
            }
        }

        [Fact]
        public void UnitPresentationOffersUnitListsAndUnitTriggers()
        {
            using (MapDocument doc = Open())
            {
                Unit unit = doc.PlaceUnit(new Point(20, 20), doc.AvailableUnits().First(t => t.IsGroundUnit));
                ObjectPropertiesPresentation p = ObjectPropertiesPresenter.For(doc.Plugin, unit);
                Assert.True(p.HouseEnabled);
                Assert.Equal(doc.Map.Houses.Select(h => h.Type.Name).ToList(), p.Houses);
                Assert.True(p.StrengthEnabled);
                Assert.Equal(256, p.Strength);
                Assert.True(p.DirectionVisible);
                Assert.True(p.DirectionEnabled);
                Assert.Equal(doc.Map.UnitDirectionTypes.Select(d => d.Name).ToList(), p.Directions);
                Assert.True(p.MissionVisible);
                Assert.Equal(doc.Map.MissionTypes.ToList(), p.Missions);
                Assert.Equal(unit.Mission, p.Mission);
                Assert.True(p.TriggerEnabled);
                Assert.Equal(Trigger.None, p.Triggers[0]);
                Assert.Equal(doc.Map.FilterUnitTriggers().Select(t => t.Name).ToList(), p.Triggers.Skip(1).ToList());
                Assert.False(p.BuildingExtrasVisible);
            }
        }

        [Fact]
        public void AircraftNeverGetATrigger()
        {
            using (MapDocument doc = Open())
            {
                UnitType aircraft = doc.AvailableUnits().FirstOrDefault(t => t.IsAircraft);
                if (aircraft == null) return;
                Unit unit = doc.PlaceUnit(new Point(20, 20), aircraft);
                Assert.NotNull(unit);
                ObjectPropertiesPresentation p = ObjectPropertiesPresenter.For(doc.Plugin, unit);
                Assert.False(p.TriggerEnabled);
            }
        }

        [Fact]
        public void BuildingPresentationFollowsPrebuiltAndTurret()
        {
            using (MapDocument doc = Open())
            {
                BuildingType noTurret = doc.AvailableBuildings().First(b => !b.HasTurret);
                Building building = doc.PlaceBuilding(new Point(30, 30), noTurret);
                ObjectPropertiesPresentation p = ObjectPropertiesPresenter.For(doc.Plugin, building);
                Assert.False(p.DirectionVisible);
                Assert.True(p.HouseEnabled);
                Assert.False(p.MissionVisible);
                Assert.True(p.BuildingExtrasVisible);
                Assert.True(p.SellableRebuildVisible);
                Assert.False(p.PrebuiltEnabled);
                Assert.Equal(-1, p.BasePriority);
                Assert.True(p.IsPrebuilt);
                Assert.Equal(Trigger.None.Yield().Concat(doc.Map.FilterStructureTriggers().Select(t => t.Name)).ToList(), p.Triggers);

                BuildingType turret = doc.AvailableBuildings().FirstOrDefault(b => b.HasTurret);
                if (turret != null)
                {
                    Building turreted = doc.PlaceBuilding(new Point(40, 40), turret);
                    Assert.True(ObjectPropertiesPresenter.For(doc.Plugin, turreted).DirectionVisible);
                }
            }
        }

        [Fact]
        public void NonPrebuiltBuildingIsNormalizedToTheBaseHouse()
        {
            using (MapDocument doc = Open())
            {
                Building building = doc.PlaceBuilding(new Point(30, 30), doc.AvailableBuildings().First(b => !b.HasTurret));
                // Outside the rebuild base it can never be non-prebuilt.
                doc.EditObjectProperties(building, () => building.IsPrebuilt = false);
                Assert.True(building.IsPrebuilt);
                // Inside the base, un-prebuilding hands it to the base house with nothing attached.
                doc.EditObjectProperties(building, () =>
                {
                    building.BasePriority = 0;
                    building.IsPrebuilt = false;
                    building.Strength = 100;
                    building.Sellable = true;
                });
                Assert.False(building.IsPrebuilt);
                Assert.Equal(256, building.Strength);
                Assert.False(building.Sellable);
                Assert.Equal(Trigger.None, building.Trigger);
                Assert.Equal(doc.Map.GetBaseHouse(doc.Plugin.GameInfo).Name, building.House.Name);
                ObjectPropertiesPresentation p = ObjectPropertiesPresenter.For(doc.Plugin, building);
                Assert.False(p.HouseEnabled);
                Assert.False(p.StrengthEnabled);
                Assert.False(p.TriggerEnabled);
                Assert.True(p.PrebuiltEnabled);
            }
        }

        [Fact]
        public void PropertyEditsAreOneUndoStep()
        {
            using (MapDocument doc = Open())
            {
                Unit unit = doc.PlaceUnit(new Point(20, 20), doc.AvailableUnits().First(t => t.IsGroundUnit));
                HouseType originalHouse = unit.House;
                HouseType ussr = doc.Map.HouseTypes.First(h => h.Name == "USSR");
                HouseType greece = doc.Map.HouseTypes.First(h => h.Name == "Greece");
                doc.EditObjectProperties(unit, () =>
                {
                    unit.House = unit.House == ussr ? greece : ussr;
                    unit.Strength = 100;
                });
                HouseType editedHouse = unit.House;
                Assert.Equal(100, unit.Strength);
                doc.Undo();
                Assert.Same(originalHouse, unit.House);
                Assert.Equal(256, unit.Strength);
                doc.Redo();
                Assert.Same(editedHouse, unit.House);
                Assert.Equal(100, unit.Strength);
            }
        }

        [Fact]
        public void TerrainHasNothingEditableInRedAlert()
        {
            using (MapDocument doc = Open())
            {
                Terrain terrain = doc.PlaceTerrain(new Point(24, 24), doc.AvailableTerrain().First());
                ObjectPropertiesPresentation p = ObjectPropertiesPresenter.For(doc.Plugin, terrain);
                Assert.False(p.HouseEnabled);
                Assert.False(p.StrengthEnabled);
                Assert.False(p.DirectionVisible);
                Assert.False(p.MissionVisible);
                Assert.False(p.TriggerEnabled);
                Assert.False(p.BuildingExtrasVisible);
            }
        }

        [Fact]
        public void PropertyEditsKeepIncrementalRenderIdentical()
        {
            using (MapDocument doc = Open())
            {
                doc.Scale = 1.0 / 16;
                Unit unit = doc.PlaceUnit(new Point(20, 20), doc.AvailableUnits().First(t => t.IsGroundUnit));
                using (doc.Render()) { }
                HouseType other = doc.Map.HouseTypes.First(h => h != unit.House);
                doc.EditObjectProperties(unit, () => unit.House = other);
                using (Bitmap incremental = doc.Render())
                {
                    double original = doc.Scale;
                    doc.Scale = original * 2;
                    using (doc.Render()) { }
                    doc.Scale = original;
                    using (Bitmap full = doc.Render())
                    {
                        ImageDiff diff = ImageCompare.Compare(incremental, full);
                        Assert.True(diff.Differing == 0, "incremental render differs from full render: " + diff);
                    }
                }
            }
        }
    }
}
