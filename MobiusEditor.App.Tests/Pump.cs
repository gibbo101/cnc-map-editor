using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using MobiusEditor.App;
using Xunit;

namespace MobiusEditor.App.Tests
{
    /// <summary>
    /// The session and map load off the UI thread now, so tests pump the dispatcher until
    /// the window is actually ready instead of assuming one RunJobs is enough.
    /// </summary>
    internal static class Pump
    {
        /// <summary>Scrolls the cell into view, then gives its centre in window coordinates.</summary>
        public static Avalonia.Point CellCenter(MainWindow window, int cellX, int cellY)
        {
            int tile = (int)(128 * window.Document.Scale);
            ScrollViewer scroller = window.FindControl<ScrollViewer>("Scroller");
            scroller.Offset = new Avalonia.Vector(System.Math.Max(0, cellX * tile - 64), System.Math.Max(0, cellY * tile - 64));
            Dispatcher.UIThread.RunJobs();
            Image image = window.FindControl<Image>("MapImage");
            Avalonia.Point origin = image.TranslatePoint(new Avalonia.Point(0, 0), window).Value;
            return new Avalonia.Point(origin.X + cellX * tile + tile / 2, origin.Y + cellY * tile + tile / 2);
        }

        public static void UntilMapReady(MainWindow window)
        {
            Image image = window.FindControl<Image>("MapImage");
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 120000)
            {
                Dispatcher.UIThread.RunJobs();
                if (window.Document != null && window.Document.IsOpen && image.Source != null) break;
                System.Threading.Thread.Sleep(25);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Document != null && window.Document.IsOpen && image.Source != null, "map did not open and render in time");
        }

        /// <summary>Left click with the eraser toggled on, then paint mode restored.</summary>
        public static void EraseClick(MainWindow window, Avalonia.Point at)
        {
            Avalonia.Controls.Primitives.ToggleButton eraser = window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("EraserButton");
            eraser.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, at, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, at, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            eraser.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
        }

        public static void UntilSessionReady(MainWindow window)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (window.Document == null && sw.ElapsedMilliseconds < 120000)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(25);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.Document);
        }
    }
}
