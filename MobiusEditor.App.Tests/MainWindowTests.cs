using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MobiusCore.Tests;
using MobiusEditor.App;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(MobiusEditor.App.Tests.TestAppBuilder))]

namespace MobiusEditor.App.Tests
{
    public class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();
    }

    /// <summary>The window runs end to end on the headless platform: it opens a map from its arguments and paints it.</summary>
    public class MainWindowTests
    {
        [AvaloniaFact]
        public void OpensAMapFromArgumentsAndRendersIt()
        {
            string map = Path.Combine(TestPaths.OfficialMapsDir, "scm111ea.ini");
            MainWindow window = new MainWindow(new[] { map, "--game", TestPaths.GameDir });
            window.Show();
            Pump.UntilMapReady(window);
            Assert.Contains("Docklands", window.Title);
            Image image = window.FindControl<Image>("MapImage");
            Assert.NotNull(image.Source);
            Bitmap frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(frame.PixelSize.Width > 100 && frame.PixelSize.Height > 100);
            frame.Save(TestPaths.Output("main-window.png"));
        }

        [AvaloniaFact]
        public void OpensAFormatFourMapOnItsBigGrid()
        {
            string ts = TestPaths.Oracle(Path.Combine("inputs", "ts-theatres"));
            string map = Path.Combine(ts, "CCDATA", "TFTS1005.INI");
            MainWindow window = new MainWindow(new[] { map, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir, "--mod", ts });
            window.Show();
            Pump.UntilMapReady(window);
            Assert.Equal(256, window.Document.Map.Metrics.Width);
            Assert.Equal(new System.Drawing.Rectangle(43, 87, 169, 82), window.Document.Map.Bounds);
            Assert.NotEmpty(window.Document.Map.TilePatches);
            Bitmap frame = window.CaptureRenderedFrame();
            Assert.True(frame.PixelSize.Width > 100 && frame.PixelSize.Height > 100);
            frame.Save(TestPaths.Output("main-window-crater.png"));
        }

        [AvaloniaFact]
        public void StartsEmptyWithoutAMap()
        {
            MainWindow window = new MainWindow(new[] { "--game", TestPaths.GameDir });
            window.Show();
            Pump.UntilSessionReady(window);
            Assert.Equal("No map", window.FindControl<TextBlock>("TitleLabel").Text);
            Assert.False(window.FindControl<Button>("ZoomInButton").IsEnabled);
            Assert.Contains("Game: ", window.FindControl<TextBlock>("StatusLabel").Text);
        }
    }
}
