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

        [AvaloniaFact]
        public void ClickingTheCanvasPaintsTheSelectedTemplateAndCtrlZUndoesIt()
        {
            MainWindow window = OpenTemperate();
            ListBox palette = window.FindControl<ListBox>("TemplatePalette");
            TemplateType sh1 = palette.Items.Cast<TemplateType>().Single(t => t.Name == "tdsh1");
            palette.SelectedItem = sh1;
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
            palette.SelectedItem = palette.Items.Cast<TemplateType>().Single(t => t.Name == "tdsh1");
            Point click = CellCenter(window, 20, 20);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.Document.Map.Templates[20, 20]);

            window.MouseDown(click, MouseButton.Right);
            window.MouseUp(click, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(window.Document.Map.Templates[20, 20]);
        }

        [AvaloniaFact]
        public void PaletteOffersOnlyTheaterAvailableTemplates()
        {
            MainWindow window = OpenTemperate();
            ListBox palette = window.FindControl<ListBox>("TemplatePalette");
            var names = palette.Items.Cast<TemplateType>().Select(t => t.Name).ToList();
            Assert.Contains("tdsh1", names);
            // Desert-only tiles live in the Interior slot; a Temperate map must not offer them.
            Assert.DoesNotContain("tdsh51", names);
        }
    }
}
