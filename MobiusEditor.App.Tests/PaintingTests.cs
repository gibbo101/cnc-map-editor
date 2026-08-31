using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MobiusCore.Tests;
using MobiusEditor.App;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.App.Tests
{
    /// <summary>
    /// Painting works end to end on the headless platform: pick a template in the palette,
    /// click the canvas, the cell changes; right-click erases; Ctrl+Z undoes. The window
    /// stays a thin skin — all semantics live in MapDocument and are tested there.
    /// </summary>
    public class PaintingTests
    {
        private static MainWindow OpenTemperate()
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

        [AvaloniaFact]
        public void ClickingTheCanvasPaintsTheSelectedTemplateAndCtrlZUndoesIt()
        {
            MainWindow window = OpenTemperate();
            ListBox palette = window.FindControl<ListBox>("TemplatePalette");
            PaletteEntry sh1Entry = palette.Items.Cast<PaletteEntry>().Single(p => (p.Type as TemplateType)?.Name == "tdsh1");
            TemplateType sh1 = (TemplateType)sh1Entry.Type;
            palette.SelectedItem = sh1Entry;
            Template before = window.Document.Map.Templates[10, 10];

            Point click = CellCenter(window, 10, 10);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(sh1, window.Document.Map.Templates[10, 10].Type);

            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(before, window.Document.Map.Templates[10, 10]);
        }

        [AvaloniaFact]
        public void RightClickErases()
        {
            MainWindow window = OpenTemperate();
            ListBox palette = window.FindControl<ListBox>("TemplatePalette");
            palette.SelectedItem = palette.Items.Cast<PaletteEntry>().Single(p => (p.Type as TemplateType)?.Name == "tdsh1");
            Point click = CellCenter(window, 20, 20);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.Document.Map.Templates[20, 20]);

            Pump.EraseClick(window, click);
            Assert.Null(window.Document.Map.Templates[20, 20]);
        }

        [AvaloniaFact]
        public void SelectingAnOverlayPaintsItAndDeselectsTheTemplateBrush()
        {
            MainWindow window = OpenTemperate();
            ListBox templates = window.FindControl<ListBox>("TemplatePalette");
            ListBox overlays = window.FindControl<ListBox>("OverlayPalette");
            templates.SelectedItem = templates.Items.Cast<PaletteEntry>().Single(p => (p.Type as TemplateType)?.Name == "tdsh1");
            overlays.SelectedItem = overlays.Items.Cast<PaletteEntry>().Single(p => (p.Type as OverlayType)?.Name == "brik");
            Assert.Null(templates.SelectedItem);

            window.Document.Map.Overlay[15, 15] = null;
            Point click = CellCenter(window, 15, 15);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("brik", window.Document.Map.Overlay[15, 15].Type.Name);

            // Right-click with the wall brush erases the wall again.
            Pump.EraseClick(window, click);
            Assert.Null(window.Document.Map.Overlay[15, 15]);
        }

        [AvaloniaFact]
        public void PaletteOffersOnlyTheaterAvailableTemplates()
        {
            MainWindow window = OpenTemperate();
            ListBox palette = window.FindControl<ListBox>("TemplatePalette");
            var names = palette.Items.Cast<PaletteEntry>().Select(p => ((TemplateType)p.Type).Name).ToList();
            Assert.Contains("tdsh1", names);
            // Desert-only tiles live in the Interior slot; a Temperate map must not offer them.
            Assert.DoesNotContain("tdsh51", names);
        }
    }
}
