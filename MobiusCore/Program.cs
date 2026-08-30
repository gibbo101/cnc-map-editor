using System;
using System.IO;

namespace MobiusEditor
{
    /// <summary>Process-level constants the core reads; the shell owns the rest of startup.</summary>
    public static class Program
    {
        public const string RemasterSteamId = "1213210";
        public static string ApplicationPath { get; set; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    }
}
