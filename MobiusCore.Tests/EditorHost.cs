using MobiusEditor.Headless;

namespace MobiusCore.Tests
{
    /// <summary>One headless session for the whole test run: loading the game archives costs seconds.</summary>
    public static class EditorHost
    {
        private static readonly object gate = new object();
        private static readonly System.Collections.Generic.Dictionary<MobiusEditor.Model.GameType, EditorSession> shared = new System.Collections.Generic.Dictionary<MobiusEditor.Model.GameType, EditorSession>();

        public static EditorSession Shared => SharedFor(MobiusEditor.Model.GameType.RedAlert);

        public static EditorSession SharedFor(MobiusEditor.Model.GameType game)
        {
            lock (gate)
            {
                if (!shared.TryGetValue(game, out EditorSession s))
                {
                    string[] mods = game == MobiusEditor.Model.GameType.RedAlert ? new[] { TestPaths.ModDir } : new string[0];
                    shared[game] = s = new EditorSession(TestPaths.GameDir, game, mods);
                }
                return s;
            }
        }
    }
}
