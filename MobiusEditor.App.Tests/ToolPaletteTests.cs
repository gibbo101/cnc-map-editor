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
            Dispatcher.UIThread.RunJobs();
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
            Click(window, click, MouseButton.Right);
            Assert.Null(window.Document.Map.CellTriggers[new System.Drawing.Point(12, 12)]);

            // Picking another brush clears this one and drops the indicator layer.
            ListBox templates = window.FindControl<ListBox>("TemplatePalette");
            templates.SelectedItem = templates.Items.Cast<TemplateType>().First();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(palette.SelectedItem);
        }

        [AvaloniaFact]
        public void TerrainBrushPlacesAndErasesObjects()
        {
            MainWindow window = Open();
            ListBox palette = window.FindControl<ListBox>("TerrainPalette");
            TerrainType type = palette.Items.Cast<TerrainType>().First();
            palette.SelectedItem = type;
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

            Click(window, CellCenter(window, occupied.X, occupied.Y), MouseButton.Right);
            Assert.Null(window.Document.Map.Technos[occupied]);
        }

        [AvaloniaFact]
        public void UnitAndInfantryBrushesUseTheToolbarHouse()
        {
            MainWindow window = Open();
            ComboBox house = window.FindControl<ComboBox>("HouseCombo");
            house.SelectedItem = house.Items.Cast<string>().First(h => h == "USSR");
            Dispatcher.UIThread.RunJobs();

            ListBox units = window.FindControl<ListBox>("UnitPalette");
            units.SelectedItem = units.Items.Cast<UnitType>().First(t => t.IsGroundUnit);
            Dispatcher.UIThread.RunJobs();
            Click(window, CellCenter(window, 16, 16), MouseButton.Left);
            Unit unit = Assert.IsType<Unit>(window.Document.Map.Technos[new System.Drawing.Point(16, 16)]);
            Assert.Equal("USSR", unit.House.Name);
            Click(window, CellCenter(window, 16, 16), MouseButton.Right);
            Assert.Null(window.Document.Map.Technos[new System.Drawing.Point(16, 16)]);

            ListBox infantry = window.FindControl<ListBox>("InfantryPalette");
            infantry.SelectedItem = infantry.Items.Cast<InfantryType>().First();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(units.SelectedItem);
            Click(window, CellCenter(window, 17, 16), MouseButton.Left);
            InfantryGroup group = Assert.IsType<InfantryGroup>(window.Document.Map.Technos[new System.Drawing.Point(17, 16)]);
            Assert.Equal("USSR", group.Infantry.Single(i => i != null).House.Name);
            Click(window, CellCenter(window, 17, 16), MouseButton.Right);
            Assert.Null(window.Document.Map.Technos[new System.Drawing.Point(17, 16)]);
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
            Click(window, click, MouseButton.Right);
            Assert.False(window.Document.Map.Waypoints[index].Cell.HasValue);
        }
    }
}
