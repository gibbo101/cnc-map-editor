using System;
using Avalonia;

namespace MobiusEditor.App
{
    internal static class Program
    {
        /// <summary>Arguments the editor was started with; the session is built from them once Avalonia is up.</summary>
        public static string[] Args { get; private set; } = Array.Empty<string>();

        [STAThread]
        public static void Main(string[] args)
        {
            Args = args;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}
