using System.Diagnostics;
using Avalonia.Controls;
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
