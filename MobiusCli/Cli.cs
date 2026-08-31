using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Utility;

namespace MobiusCli
{
    /// <summary>Parsed invocation: a command, its positional arguments, and the profile options.</summary>
    public sealed class Invocation
    {
        public string Command;
        public List<string> Positional = new List<string>();
        public string GameDir;
        public List<string> ModDirs = new List<string>();
        public Dictionary<string, string> Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Every option in command-line order, repeats preserved — for commands whose options are an ordered operation list.</summary>
        public List<KeyValuePair<string, string>> Sequence = new List<KeyValuePair<string, string>>();

        public static Invocation Parse(string[] args)
        {
            Invocation inv = new Invocation();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--"))
                {
                    string name = a.Substring(2);
                    string value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
                    inv.Sequence.Add(new KeyValuePair<string, string>(name, value));
                    if (name == "game") inv.GameDir = value;
                    else if (name == "mod") inv.ModDirs.Add(value);
                    else inv.Options[name] = value;
                }
                else if (inv.Command == null) inv.Command = a;
                else inv.Positional.Add(a);
            }
            return inv;
        }

        public string Option(string name, string fallback = null) => Options.TryGetValue(name, out string v) ? v : fallback;
    }

    public static class Cli
    {
        public const string Usage = "usage: cncmap <info|validate|render|save> <map> [args] [--game <dir>] [--game-type RA|TD] [--mod <dir>]...\n       cncmap edit <map> --out <path> [--place <tile>@<x>,<y>] [--erase <x>,<y>] [--place-overlay <name>@<x>,<y>] [--erase-overlay <x>,<y>]...\n       cncmap expand-mission <map> <spec.json> --out <path>\n       cncmap mods [--mods-root <dir>] [--no-workshop]";

        /// <summary>Runs one command; returns the process exit code. Errors go to stderr, results to stdout.</summary>
        public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            Invocation inv = Invocation.Parse(args);
            CoreDiagnostics.Report = (title, message) => stderr.WriteLine(title + ": " + message);
            try
            {
                switch (inv.Command)
                {
                    case "info": return Commands.Info(inv, OpenSession(inv), stdout);
                    case "validate": return Commands.Validate(inv, OpenSession(inv), stdout);
                    case "mods": return Commands.Mods(inv, stdout);
                    case "render": return Commands.Render(inv, OpenSession(inv), stdout);
                    case "save": return Commands.Save(inv, OpenSession(inv), stdout);
                    case "edit": return Commands.Edit(inv, OpenSession(inv), stdout);
                    case "expand-mission": return Commands.ExpandMission(inv, OpenSession(inv), stdout);
                    case null: stderr.WriteLine(Usage); return 2;
                    default: stderr.WriteLine("unknown command: " + inv.Command); stderr.WriteLine(Usage); return 2;
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is ArgumentException)
            {
                stderr.WriteLine("error: " + ex.Message);
                return 1;
            }
        }

        private static EditorSession OpenSession(Invocation inv)
        {
            string game = inv.GameDir ?? SteamAssist.TryGetSteamGameFolder(MobiusEditor.Program.RemasterSteamId, "TiberianDawn.dll", "RedAlert.dll")
                ?? throw new DirectoryNotFoundException("Game install not found; pass --game <dir>.");
            string gameType = inv.Option("game-type", "RA");
            MobiusEditor.Model.GameType type = gameType.Equals("TD", StringComparison.OrdinalIgnoreCase) ? MobiusEditor.Model.GameType.TiberianDawn
                : gameType.Equals("RA", StringComparison.OrdinalIgnoreCase) ? MobiusEditor.Model.GameType.RedAlert
                : throw new ArgumentException("unknown --game-type " + gameType + " (RA or TD)");
            return new EditorSession(game, type, inv.ModDirs);
        }
    }
}
