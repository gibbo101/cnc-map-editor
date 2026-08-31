using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.App.Tests
{
    /// <summary>
    /// Past the surface budget the window renders only the cell block around the viewport —
    /// a 128-cell map at 100% zoom would otherwise need a 16384², one-gigabyte bitmap (the
    /// Deck crash). The frame stays screenful-sized, the scrollbars still span the whole
    /// map, and painting through the panel lands on the right cell wherever the view is
    /// scrolled.
    /// </summary>
    public class ViewportRenderTests
    {
        [AvaloniaFact]
        public void HighZoomStaysSmallAndClicksLandOnTheRightCell()
        {
            string map = Path.Combine(TestPaths.MapEdits, "scm05ea.ini");
            MainWindow window = new MainWindow(new[] { map, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir });
            window.Show();
            Pump.UntilMapReady(window);

            Button zoomIn = window.FindControl<Button>("ZoomInButton");
            for (int i = 0; i < 4; i++)
            {
                zoomIn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(1.0, window.Document.Scale);
            // The frame is a viewport block, not the 16384² whole-map surface.
            WriteableBitmap frame = Assert.IsType<WriteableBitmap>(window.FindControl<Image>("MapImage").Source);
            Assert.True(frame.PixelSize.Width < 4096 && frame.PixelSize.Height < 4096,
                $"frame is {frame.PixelSize} — expected a viewport-sized block");
            // Scrollbars still cover the whole map.
            Panel panel = window.FindControl<Panel>("MapPanel");
            Assert.Equal(128 * 128, panel.Width);

            // Scroll deep into the map and paint there.
            ScrollViewer scroller = window.FindControl<ScrollViewer>("Scroller");
            scroller.Offset = new Vector(60 * 128, 60 * 128);
            Dispatcher.UIThread.RunJobs();
            ListBox palette = window.FindControl<ListBox>("TemplatePalette");
            palette.SelectedItem = palette.Items.OfType<PaletteEntry>().Single(p => (p.Type as TemplateType)?.Name == "tdsh1");
            Dispatcher.UIThread.RunJobs();
            Point panelOrigin = panel.TranslatePoint(new Point(0, 0), window).Value;
            Point click = new Point(panelOrigin.X + 61 * 128 + 64, panelOrigin.Y + 61 * 128 + 64);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("tdsh1", window.Document.Map.Templates[new System.Drawing.Point(61, 61)]?.Type.Name);
        }
    }
}
