using MobiusEditor.Headless;

namespace MobiusCore.Tests
{
    /// <summary>One headless session for the whole test run: loading the game archives costs seconds.</summary>
    public static class EditorHost
    {
        private static readonly object gate = new object();
        private static EditorSession shared;

        public static EditorSession Shared
        {
            get { lock (gate) { return shared ?? (shared = new EditorSession(TestPaths.GameDir, new[] { TestPaths.ModDir })); } }
        }
    }
}
