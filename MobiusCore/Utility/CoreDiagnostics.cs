using System;

namespace MobiusEditor.Utility
{
    /// <summary>Where the core reports user-facing errors; the shell decides how to show them.</summary>
    public static class CoreDiagnostics
    {
        /// <summary>Asks the user for the game install directory; null means they declined.</summary>
        public static Func<string> AskGameDirectory { get; set; }
        public static Action<string, string> Report { get; set; } = (title, message) => Console.Error.WriteLine(title + ": " + message);
    }
}
