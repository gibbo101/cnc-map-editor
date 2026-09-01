using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.App.Tests
{
    /// <summary>
    /// The New dialog's "Random skirmish fill" runs the map generator on the fresh map:
    /// assigned starts, trees and resource fields land in the document, with an empty
    /// undo history — the fill is the starting state, not an edit.
    /// </summary>
    public class NewRandomMapTests
    {
        [AvaloniaFact]
        public void RandomFillGeneratesAPopulatedSkirmishMap()
        {
            string map = Path.Combine(TestPaths.MapEdits, "scm05ea.ini");
            MainWindow window = new MainWindow(new[] { map, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir });
            window.Show();
            Pump.UntilMapReady(window);

            NewMapWindow dialog = window.OpenNewMapDialog();
            Dispatcher.UIThread.RunJobs();
            ListBox theaters = dialog.FindControl<ListBox>("TheaterList");
            theaters.SelectedItem = theaters.Items.Cast<string>().First(t => t == "Temperate");
            dialog.FindControl<CheckBox>("RandomFill").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.FindControl<StackPanel>("RandomPanel").IsVisible);
            dialog.FindControl<NumericUpDown>("SeedNud").Value = 9;
            dialog.FindControl<NumericUpDown>("PlayersNud").Value = 3;
            dialog.FindControl<Button>("OkButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            while ((window.Document.Path != null || !window.Document.IsOpen
                    || !window.Document.Map.Technos.Occupiers.OfType<Terrain>().Any())
                && sw.ElapsedMilliseconds < 120000)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(25);
            }
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Document.IsOpen);
            Assert.Null(window.Document.Path);
            Assert.Equal(3, window.Document.Map.Waypoints.Count(w => w.Flags.HasFlag(WaypointFlag.PlayerStart) && w.Cell.HasValue));
            Assert.NotEmpty(window.Document.Map.Technos.Occupiers.OfType<Terrain>());
            Assert.Contains(window.Document.Map.Overlay, c => c.Value?.Type.IsResource == true);
            Assert.False(window.Document.CanUndo);
        }
    }
}
