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

namespace MobiusEditor.App.Tests
{
    /// <summary>
    /// The properties panel: a brushless click selects the object under the cell, the panel
    /// shows the fields the fork's rules allow for it, each change is one undo step, and the
    /// panel drops the selection when undo retires the object. Semantics live in the Shell
    /// presenter and document; these tests pin the wiring.
    /// </summary>
    public class PropertiesPanelTests
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
        public void BrushlessClickSelectsAndEditsAreUndoable()
        {
            MainWindow window = Open();
            Unit unit = window.Document.PlaceUnit(new System.Drawing.Point(15, 15), window.Document.AvailableUnits().First(t => t.IsGroundUnit));
            Dispatcher.UIThread.RunJobs();
            StackPanel panel = window.FindControl<StackPanel>("PropertiesPanel");
            Assert.False(panel.IsVisible);

            Click(window, CellCenter(window, 15, 15), MouseButton.Left);
            Assert.Same(unit, window.SelectedObject);
            Assert.True(panel.IsVisible);

            ComboBox house = window.FindControl<ComboBox>("PropHouse");
            string otherHouse = house.Items.Cast<string>().First(h => h != unit.House.Name);
            house.SelectedItem = otherHouse;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(otherHouse, unit.House.Name);

            NumericUpDown strength = window.FindControl<NumericUpDown>("PropStrength");
            strength.Value = 100;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, unit.Strength);

            // Each change was one undo step.
            window.Document.Undo();
            Assert.Equal(256, unit.Strength);
            Assert.Equal(otherHouse, unit.House.Name);
            window.Document.Undo();
            Assert.NotEqual(otherHouse, unit.House.Name);
        }

        [AvaloniaFact]
        public void UndoingThePlacementDropsThePanel()
        {
            MainWindow window = Open();
            window.Document.PlaceUnit(new System.Drawing.Point(15, 15), window.Document.AvailableUnits().First(t => t.IsGroundUnit));
            Dispatcher.UIThread.RunJobs();
            Click(window, CellCenter(window, 15, 15), MouseButton.Left);
            StackPanel panel = window.FindControl<StackPanel>("PropertiesPanel");
            Assert.True(panel.IsVisible);
            window.Document.Undo();
            Dispatcher.UIThread.RunJobs();
            Assert.False(panel.IsVisible);
            Assert.Null(window.SelectedObject);
        }

        [AvaloniaFact]
        public void ClickingAnEmptyCellClearsTheSelection()
        {
            MainWindow window = Open();
            window.Document.PlaceUnit(new System.Drawing.Point(15, 15), window.Document.AvailableUnits().First(t => t.IsGroundUnit));
            Dispatcher.UIThread.RunJobs();
            Click(window, CellCenter(window, 15, 15), MouseButton.Left);
            Assert.NotNull(window.SelectedObject);
            Click(window, CellCenter(window, 5, 5), MouseButton.Left);
            Assert.Null(window.SelectedObject);
            Assert.False(window.FindControl<StackPanel>("PropertiesPanel").IsVisible);
        }

        [AvaloniaFact]
        public void BuildingSelectionShowsTheExtrasAndPrebuiltRules()
        {
            MainWindow window = Open();
            Building building = window.Document.PlaceBuilding(new System.Drawing.Point(10, 10), window.Document.AvailableBuildings().First(b => !b.HasTurret));
            Dispatcher.UIThread.RunJobs();
            Click(window, CellCenter(window, 10, 10), MouseButton.Left);
            Assert.Same(building, window.SelectedObject);
            Assert.True(window.FindControl<StackPanel>("PropBuildingRow").IsVisible);
            Assert.False(window.FindControl<StackPanel>("PropDirectionRow").IsVisible);
            Assert.False(window.FindControl<StackPanel>("PropMissionRow").IsVisible);
            CheckBox prebuilt = window.FindControl<CheckBox>("PropPrebuilt");
            Assert.False(prebuilt.IsEnabled);
            window.FindControl<NumericUpDown>("PropBasePriority").Value = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, building.BasePriority);
            Assert.True(prebuilt.IsEnabled);
        }
    }
}
