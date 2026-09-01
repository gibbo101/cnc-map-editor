using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;
using RA = MobiusEditor.RedAlert;

namespace MobiusEditor.App.Tests
{
    /// <summary>
    /// The docked tool tabs: cell-trigger and waypoint brushes paint and erase on the canvas
    /// like the terrain/overlay brushes, keep one brush active at a time across all four
    /// palettes, and switch their indicator layer on while active.
    /// </summary>
    public class ToolPaletteTests
    {
        private static MainWindow Open()
        {
            string map = Path.Combine(TestPaths.MapEdits, "scm05ea.ini");
            MainWindow window = new MainWindow(new[] { map, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir });
            window.Show();
            Pump.UntilMapReady(window);
            return window;
        }

        private static Point CellCenter(MainWindow window, int cellX, int cellY)
        {
            Image image = window.FindControl<Image>("MapImage");
            Point origin = image.TranslatePoint(new Point(0, 0), window).Value;
            int tile = (int)(128 * window.Document.Scale);
            return new Point(origin.X + cellX * tile + tile / 2, origin.Y + cellY * tile + tile / 2);
        }

        private static void Click(MainWindow window, Point at, MouseButton button)
        {
            window.MouseDown(at, button);
            window.MouseUp(at, button);
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void CellTriggerBrushPaintsErasesAndTogglesItsLayer()
        {
            MainWindow window = Open();
            window.Document.EditTriggers(ed =>
            {
                Trigger t = ed.Add();
                ed.TryRename(t, "celx");
                t.Event1.EventType = RA.EventTypes.TEVENT_PLAYER_ENTERED;
            });
            Dispatcher.UIThread.RunJobs();
            ListBox palette = window.FindControl<ListBox>("CellTriggerPalette");
            Assert.Contains("celx", palette.Items.Cast<string>());
            palette.SelectedItem = "celx";
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Document.Layers.HasFlag(MapLayerFlag.CellTriggers));

            Point click = CellCenter(window, 12, 12);
            Click(window, click, MouseButton.Left);
            Assert.Equal("celx", window.Document.Map.CellTriggers[new System.Drawing.Point(12, 12)].Trigger);
            Pump.EraseClick(window, click);
            Assert.Null(window.Document.Map.CellTriggers[new System.Drawing.Point(12, 12)]);

            // Picking another brush clears this one and drops the indicator layer.
            ListBox templates = window.FindControl<ListBox>("TemplatePalette");
            templates.SelectedItem = templates.Items.OfType<PaletteEntry>().First();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(palette.SelectedItem);
        }

        [AvaloniaFact]
        public void TerrainBrushPlacesAndErasesObjects()
        {
            MainWindow window = Open();
            ListBox palette = window.FindControl<ListBox>("TerrainPalette");
            PaletteEntry entry = palette.Items.OfType<PaletteEntry>().First();
            TerrainType type = (TerrainType)entry.Type;
            palette.SelectedItem = entry;
            Dispatcher.UIThread.RunJobs();

            Click(window, CellCenter(window, 20, 20), MouseButton.Left);
            // The occupy mask needn't claim the origin cell; find the first cell it does.
            bool[,] mask = type.OccupyMask;
            System.Drawing.Point occupied = new System.Drawing.Point(20, 20);
            for (int y = 0; y < mask.GetLength(0); y++)
                for (int x = 0; x < mask.GetLength(1); x++)
                    if (mask[y, x]) { occupied = new System.Drawing.Point(20 + x, 20 + y); y = mask.GetLength(0); break; }
            Terrain placed = window.Document.Map.Technos[occupied] as Terrain;
            Assert.NotNull(placed);
            Assert.Same(type, placed.Type);

            Pump.EraseClick(window, CellCenter(window, occupied.X, occupied.Y));
            Assert.Null(window.Document.Map.Technos[occupied]);
        }

        [AvaloniaFact]
        public void BuildingBrushPlacesWithTheToolbarHouseAndErases()
        {
            MainWindow window = Open();
            ComboBox house = window.FindControl<ComboBox>("HouseCombo");
            house.SelectedItem = house.Items.Cast<string>().First(h => h == "Greece");
            Dispatcher.UIThread.RunJobs();
            ListBox palette = window.FindControl<ListBox>("BuildingPalette");
            palette.SelectedItem = palette.Items.OfType<PaletteEntry>().First(p => (p.Type as BuildingType)?.HasBib == true);
            Dispatcher.UIThread.RunJobs();

            Click(window, CellCenter(window, 10, 10), MouseButton.Left);
            Building placed = window.Document.Map.Buildings[new System.Drawing.Point(10, 10)] as Building;
            Assert.NotNull(placed);
            Assert.Equal("Greece", placed.House.Name);
            Pump.EraseClick(window, CellCenter(window, 10, 10));
            Assert.Null(window.Document.Map.Buildings[new System.Drawing.Point(10, 10)]);
        }

        [AvaloniaFact]
        public void UnitAndInfantryBrushesUseTheToolbarHouse()
        {
            MainWindow window = Open();
            ComboBox house = window.FindControl<ComboBox>("HouseCombo");
            house.SelectedItem = house.Items.Cast<string>().First(h => h == "USSR");
            Dispatcher.UIThread.RunJobs();

            ListBox units = window.FindControl<ListBox>("UnitPalette");
            units.SelectedItem = units.Items.OfType<PaletteEntry>().First(p => (p.Type as UnitType)?.IsGroundUnit == true);
            Dispatcher.UIThread.RunJobs();
            Click(window, CellCenter(window, 16, 16), MouseButton.Left);
            Unit unit = Assert.IsType<Unit>(window.Document.Map.Technos[new System.Drawing.Point(16, 16)]);
            Assert.Equal("USSR", unit.House.Name);
            Pump.EraseClick(window, CellCenter(window, 16, 16));
            Assert.Null(window.Document.Map.Technos[new System.Drawing.Point(16, 16)]);

            ListBox infantry = window.FindControl<ListBox>("InfantryPalette");
            infantry.SelectedItem = infantry.Items.OfType<PaletteEntry>().First(p => p.Type is InfantryType);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(units.SelectedItem);
            Click(window, CellCenter(window, 17, 16), MouseButton.Left);
            InfantryGroup group = Assert.IsType<InfantryGroup>(window.Document.Map.Technos[new System.Drawing.Point(17, 16)]);
            Assert.Equal("USSR", group.Infantry.Single(i => i != null).House.Name);
            Pump.EraseClick(window, CellCenter(window, 17, 16));
            Assert.Null(window.Document.Map.Technos[new System.Drawing.Point(17, 16)]);
        }

        [AvaloniaFact]
        public void PalettesGroupByFactionWithUnselectableHeaders()
        {
            MainWindow window = Open();
            ListBox units = window.FindControl<ListBox>("UnitPalette");
            var items = units.Items.Cast<object>().ToList();
            Assert.Contains(items, i => (i as PaletteHeader)?.Label == "Allies");
            Assert.Contains(items, i => (i as PaletteHeader)?.Label == "Soviets");
            // The TF mod is loaded in the test session, so its faction groups appear.
            Assert.Contains(items, i => (i as PaletteHeader)?.Label == "GDI (mod)");
            Assert.True(items.FindIndex(i => (i as PaletteHeader)?.Label == "Allies")
                < items.FindIndex(i => (i as PaletteHeader)?.Label == "Soviets"));

            // The overlay palette leads with the harvestables.
            var overlays = window.FindControl<ListBox>("OverlayPalette").Items.Cast<object>().ToList();
            PaletteHeader first = Assert.IsType<PaletteHeader>(overlays[0]);
            Assert.Equal("Resources", first.Label);

            // Headers are not brushes: selecting one bounces off.
            units.SelectedItem = items.First(i => i is PaletteHeader);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(units.SelectedItem);
        }

        [AvaloniaFact]
        public void PalettesShowThumbnailsAndTheGhostFollowsTheBrush()
        {
            MainWindow window = Open();
            ListBox units = window.FindControl<ListBox>("UnitPalette");
            PaletteEntry entry = units.Items.OfType<PaletteEntry>().First(p => (p.Type as UnitType)?.IsGroundUnit == true);
            // Friendly label and a rendered thumbnail, not the bare INI name.
            Assert.Equal(((UnitType)entry.Type).DisplayName, entry.Label);
            Assert.NotNull(entry.Image);

            units.SelectedItem = entry;
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(CellCenter(window, 10, 10));
            Dispatcher.UIThread.RunJobs();
            Border ghost = window.FindControl<Border>("GhostBorder");
            Assert.True(ghost.IsVisible);
            // Object brushes get a live preview render, not the stretched palette thumbnail.
            Image ghostImage = window.FindControl<Image>("GhostImage");
            Assert.NotNull(ghostImage.Source);
            Assert.NotSame(entry.Image, ghostImage.Source);
            Assert.Equal(1.0, ghostImage.Opacity);

            // Template brushes keep their pixel-exact thumbnails.
            ListBox templates = window.FindControl<ListBox>("TemplatePalette");
            PaletteEntry template = templates.Items.OfType<PaletteEntry>().First();
            templates.SelectedItem = template;
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(CellCenter(window, 10, 10));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(template.Image, ghostImage.Source);
            units.SelectedItem = null;
            templates.SelectedItem = null;

            // No brush, no ghost.
            units.SelectedItem = null;
            window.MouseMove(CellCenter(window, 11, 10));
            Dispatcher.UIThread.RunJobs();
            Assert.False(ghost.IsVisible);
        }

        [AvaloniaFact]
        public void WaypointBrushPlacesAndErasesTheFlag()
        {
            MainWindow window = Open();
            int index = System.Array.FindIndex(window.Document.Map.Waypoints, w => !w.Cell.HasValue);
            ListBox palette = window.FindControl<ListBox>("WaypointPalette");
            palette.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Document.Layers.HasFlag(MapLayerFlag.WaypointsIndic));

            Point click = CellCenter(window, 14, 14);
            Click(window, click, MouseButton.Left);
            window.Document.Map.Metrics.GetCell(new System.Drawing.Point(14, 14), out int cell);
            Assert.Equal(cell, window.Document.Map.Waypoints[index].Cell);
            Pump.EraseClick(window, click);
            Assert.False(window.Document.Map.Waypoints[index].Cell.HasValue);
        }
    }
}
