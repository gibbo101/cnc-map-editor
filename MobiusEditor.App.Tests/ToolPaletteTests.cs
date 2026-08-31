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
