//
// The mission spec: a JSON document (Newtonsoft; // comments are tolerated for inline
// design notes) holding a flat list of patterns for cncmap expand-mission. Flexibility
// comes from the spec's shape, not the format — new patterns are new generator functions,
// never format changes — and the raw pattern is an escape hatch of literal trigger/teamtype
// rows in the game's own INI encoding, so the spec's floor is the raw format itself.
// The parser never throws; malformed input comes back as errors and a null spec.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MobiusEditor.Headless
{
    /// <summary>One pattern entry. A flat bag of fields; which are required depends on the pattern kind.</summary>
    public sealed class MissionPattern
    {
        public string Pattern { get; set; }
        public string Name { get; set; }
        public string House { get; set; }
        /// <summary>win: the house whose destruction triggers the victory.</summary>
        public string Enemy { get; set; }
        /// <summary>reinforce, attack-wave: team composition as "TYPE:count" entries.</summary>
        public List<string> Units { get; } = new List<string>();
        /// <summary>reinforce, attack-wave: origin waypoint.</summary>
        public int? At { get; set; }
        /// <summary>reinforce: waypoint the team moves to after arriving.</summary>
        public int? To { get; set; }
        /// <summary>reinforce: delay before arrival, in the game's TIME units (tenths of a minute).</summary>
        public long? After { get; set; }
        /// <summary>attack-wave: wave interval, in the game's TIME units (tenths of a minute).</summary>
        public long? Every { get; set; }
        /// <summary>reinforce: true to keep repeating instead of firing once.</summary>
        public bool Repeat { get; set; }
        /// <summary>attack-wave: quarry label from the "Attack a Type..." options; defaults to Anything.</summary>
        public string Target { get; set; }
        /// <summary>raw: literal [Trigs] rows, in declaration order.</summary>
        public List<KeyValuePair<string, string>> Triggers { get; } = new List<KeyValuePair<string, string>>();
        /// <summary>raw: literal [TeamTypes] rows, in declaration order.</summary>
        public List<KeyValuePair<string, string>> TeamTypes { get; } = new List<KeyValuePair<string, string>>();
    }

    public sealed class MissionSpec
    {
        public List<MissionPattern> Patterns { get; } = new List<MissionPattern>();

        /// <summary>Parses a spec; returns null (with errors) when the document is unusable.</summary>
        public static MissionSpec Parse(string json, out string[] errors)
        {
            List<string> errs = new List<string>();
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                errors = new[] { "spec is not valid JSON: " + ex.Message };
                return null;
            }
            MissionSpec spec = new MissionSpec();
            if (!(root["patterns"] is JArray patterns))
            {
                errors = new[] { "spec has no \"patterns\" list." };
                return null;
            }
            int nr = 0;
            foreach (JToken token in patterns)
            {
                nr++;
                if (!(token is JObject obj))
                {
                    errs.Add("pattern " + nr + " is not an object.");
                    continue;
                }
                MissionPattern p = new MissionPattern
                {
                    Pattern = (string)obj["pattern"],
                    Name = (string)obj["name"],
                    House = (string)obj["house"],
                    Enemy = (string)obj["enemy"],
                    At = (int?)obj["at"],
                    To = (int?)obj["to"],
                    After = (long?)obj["after"],
                    Every = (long?)obj["every"],
                    Repeat = (bool?)obj["repeat"] ?? false,
                    Target = (string)obj["target"],
                };
                if (obj["units"] is JArray units)
                {
                    foreach (JToken u in units) p.Units.Add(u.ToString());
                }
                ReadRows(obj["triggers"], p.Triggers, errs, nr, "triggers");
                ReadRows(obj["teamtypes"], p.TeamTypes, errs, nr, "teamtypes");
                spec.Patterns.Add(p);
            }
            errors = errs.ToArray();
            return spec;
        }

        private static void ReadRows(JToken token, List<KeyValuePair<string, string>> rows, List<string> errs, int nr, string what)
        {
            if (token == null)
            {
                return;
            }
            if (!(token is JObject obj))
            {
                errs.Add("pattern " + nr + ": \"" + what + "\" must be an object of name → row.");
                return;
            }
            foreach (JProperty prop in obj.Properties())
            {
                rows.Add(new KeyValuePair<string, string>(prop.Name, prop.Value.ToString()));
            }
        }
    }
}
