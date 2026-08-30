using System;
using System.Collections.Generic;
using System.IO;
using MobiusEditor.Utility;

namespace MobiusEditor.Headless
{
    /// <summary>The official Red Alert skirmish maps shipped inside the game's MIX archives.</summary>
    public static class OfficialMaps
    {
        public const string MainMixRelative = "Data/CNCDATA/RED_ALERT/AFTERMATH/MAIN.MIX";

        /// <summary>
        /// Writes every scm??ea.ini / scm???ea.ini found in general.mix into outDir and returns the
        /// names found. MIX archives store only name hashes, so candidates are generated: two or
        /// three alphanumerics between "scm" and "ea" (numeric for the classic maps, letter-coded
        /// for the Counterstrike/Aftermath ones).
        /// </summary>
        public static string[] Extract(string gameDir, string outDir)
        {
            Directory.CreateDirectory(outDir);
            List<string> found = new List<string>();
            using (MixFile main = new MixFile(Path.Combine(gameDir, MainMixRelative)))
            using (MixFile general = new MixFile(main, "general.mix"))
            {
                foreach (string name in CandidateNames())
                {
                    byte[] data = general.ReadFile(name);
                    if (data == null) continue;
                    File.WriteAllBytes(Path.Combine(outDir, name), data);
                    found.Add(name);
                }
            }
            return found.ToArray();
        }

        private static IEnumerable<string> CandidateNames()
        {
            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            foreach (char a in alphabet)
                foreach (char b in alphabet)
                {
                    yield return "scm" + a + b + "ea.ini";
                    foreach (char c in alphabet) yield return "scm" + a + b + c + "ea.ini";
                }
        }
    }
}
