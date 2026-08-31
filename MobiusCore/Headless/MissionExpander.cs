//
// Expands a MissionSpec into raw triggers and teamtypes on the loaded map, through the
// trigger/teamtype editors so name invariants and commit semantics hold. One-time
// scaffolding: expand, then tweak in the editor — there is no two-way sync. Every pattern
// must build cleanly and the expanded trigger list must pass CheckTriggers without fatals
// before anything is committed; on any error the map is untouched. Red Alert only for now:
// Tiberian Dawn's trigger encoding is a different shape and has no generators yet.
using System;
using System.Collections.Generic;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.RedAlert;

namespace MobiusEditor.Headless
{
    public static class MissionExpander
    {
        /// <summary>Applies the spec to the map; false (with errors, and no map change) when anything is wrong.</summary>
        public static bool Expand(IGamePlugin plugin, MissionSpec spec, out string[] errors, out string[] warnings)
        {
            List<string> errs = new List<string>();
            List<string> warns = new List<string>();
            errors = warnings = Array.Empty<string>();
            if (!(plugin is GamePluginRA ra))
            {
                errors = new[] { "mission expansion currently supports Red Alert maps only." };
                return false;
            }
            TriggerEditor triggerEditor = TriggerEditor.Begin(plugin);
            TeamTypeEditor teamEditor = TeamTypeEditor.Begin(plugin);
            int nr = 0;
            foreach (MissionPattern p in spec.Patterns)
            {
                nr++;
                string prefix = "pattern " + nr + (string.IsNullOrEmpty(p.Name) ? "" : " (" + p.Name + ")") + ": ";
                switch (p.Pattern)
                {
                    case "win":
                        BuildWin(plugin, triggerEditor, p, prefix, errs);
                        break;
                    case "lose":
                        BuildLose(plugin, triggerEditor, p, prefix, errs);
                        break;
                    case "reinforce":
                        BuildReinforce(plugin, triggerEditor, teamEditor, p, prefix, errs);
                        break;
                    case "attack-wave":
                        BuildAttackWave(plugin, triggerEditor, teamEditor, p, prefix, errs);
                        break;
                    case "raw":
                        BuildRaw(ra, triggerEditor, teamEditor, p, prefix, errs);
                        break;
                    default:
                        errs.Add(prefix + "unknown pattern \"" + p.Pattern + "\".");
                        break;
                }
            }
            if (errs.Count == 0)
            {
                string[] checkResult = plugin.CheckTriggers(triggerEditor.Triggers, true, true, false, out bool fatal, false, out _)?.ToArray() ?? Array.Empty<string>();
                if (fatal)
                {
                    errs.Add("expanded triggers do not pass the game's trigger check:");
                    errs.AddRange(checkResult);
                }
                else
                {
                    warns.AddRange(checkResult);
                }
            }
            if (errs.Count > 0)
            {
                errors = errs.ToArray();
                warnings = warns.ToArray();
                return false;
            }
            teamEditor.Commit();
            triggerEditor.Commit();
            warnings = warns.ToArray();
            return true;
        }

        private static void BuildWin(IGamePlugin plugin, TriggerEditor triggers, MissionPattern p, string prefix, List<string> errs)
        {
            HouseType winner = ResolveHouse(plugin, p.House, prefix, errs);
            HouseType enemy = ResolveHouse(plugin, p.Enemy, prefix, errs, "enemy");
            Trigger t = AddTrigger(triggers, p, prefix, errs);
            if (winner == null || enemy == null || t == null)
            {
                return;
            }
            t.House = enemy.Name;
            t.Event1.EventType = EventTypes.TEVENT_ALL_DESTROYED;
            t.Event1.Data = enemy.ID;
            t.Action1.ActionType = ActionTypes.TACTION_WIN;
            t.Action1.Data = winner.ID;
        }

        private static void BuildLose(IGamePlugin plugin, TriggerEditor triggers, MissionPattern p, string prefix, List<string> errs)
        {
            HouseType loser = ResolveHouse(plugin, p.House, prefix, errs);
            Trigger t = AddTrigger(triggers, p, prefix, errs);
            if (loser == null || t == null)
            {
                return;
            }
            t.House = loser.Name;
            t.Event1.EventType = EventTypes.TEVENT_ALL_DESTROYED;
            t.Event1.Data = loser.ID;
            t.Action1.ActionType = ActionTypes.TACTION_LOSE;
            t.Action1.Data = loser.ID;
        }

        private static void BuildReinforce(IGamePlugin plugin, TriggerEditor triggers, TeamTypeEditor teams, MissionPattern p, string prefix, List<string> errs)
        {
            HouseType house = ResolveHouse(plugin, p.House, prefix, errs);
            if (!p.After.HasValue)
            {
                errs.Add(prefix + "reinforce needs \"after\" (TIME units, tenths of a minute).");
            }
            TeamType team = AddTeam(teams, p, prefix, errs);
            Trigger t = AddTrigger(triggers, p, prefix, errs);
            List<TeamTypeClass> classes = ParseUnits(plugin, p, prefix, errs);
            if (house == null || team == null || t == null || classes == null || !p.After.HasValue)
            {
                return;
            }
            team.House = house;
            team.IsReinforcable = true;
            team.RecruitPriority = 7;
            team.Origin = p.At ?? -1;
            team.Classes.AddRange(classes);
            if (p.To.HasValue)
            {
                team.Missions.Add(new TeamTypeMission { Mission = TeamMissionTypes.Move, Argument = p.To.Value });
            }
            t.House = house.Name;
            t.PersistentType = p.Repeat ? TriggerPersistentType.Persistent : TriggerPersistentType.Volatile;
            t.Event1.EventType = EventTypes.TEVENT_TIME;
            t.Event1.Data = p.After.Value;
            t.Action1.ActionType = ActionTypes.TACTION_REINFORCEMENTS;
            t.Action1.Team = team.Name;
        }

        private static void BuildAttackWave(IGamePlugin plugin, TriggerEditor triggers, TeamTypeEditor teams, MissionPattern p, string prefix, List<string> errs)
        {
            HouseType house = ResolveHouse(plugin, p.House, prefix, errs);
            if (!p.Every.HasValue)
            {
                errs.Add(prefix + "attack-wave needs \"every\" (TIME units, tenths of a minute).");
            }
            string targetLabel = p.Target ?? "Anything";
            (int Value, string Label) target = TeamMissionTypes.Attack.DropdownOptions
                .FirstOrDefault(op => op.Label.Equals(targetLabel, StringComparison.OrdinalIgnoreCase));
            if (target.Label == null)
            {
                errs.Add(prefix + "unknown attack target \"" + targetLabel + "\"; options: "
                    + string.Join(", ", TeamMissionTypes.Attack.DropdownOptions.Select(op => op.Label)) + ".");
            }
            TeamType team = AddTeam(teams, p, prefix, errs);
            Trigger t = AddTrigger(triggers, p, prefix, errs);
            List<TeamTypeClass> classes = ParseUnits(plugin, p, prefix, errs);
            if (house == null || team == null || t == null || classes == null || target.Label == null || !p.Every.HasValue)
            {
                return;
            }
            team.House = house;
            team.RecruitPriority = 7;
            team.Origin = p.At ?? -1;
            team.Classes.AddRange(classes);
            team.Missions.Add(new TeamTypeMission { Mission = TeamMissionTypes.Attack, Argument = target.Value });
            t.House = house.Name;
            t.PersistentType = TriggerPersistentType.Persistent;
            t.Event1.EventType = EventTypes.TEVENT_TIME;
            t.Event1.Data = p.Every.Value;
            t.Action1.ActionType = ActionTypes.TACTION_CREATE_TEAM;
            t.Action1.Team = team.Name;
        }

        private static void BuildRaw(GamePluginRA ra, TriggerEditor triggers, TeamTypeEditor teams, MissionPattern p, string prefix, List<string> errs)
        {
            List<string> parseErrors = new List<string>();
            (List<Trigger> rawTriggers, List<TeamType> rawTeams) = ra.ParseRawScriptRows(p.Triggers, p.TeamTypes, parseErrors);
            // The load path is lenient with real maps; hand-written rows should parse clean.
            errs.AddRange(parseErrors.Select(e => prefix + e));
            foreach (TeamType raw in rawTeams)
            {
                TeamType team = teams.Add();
                if (team == null)
                {
                    errs.Add(prefix + "the map's teamtype cap is reached.");
                    return;
                }
                string renameError = teams.TryRename(team, raw.Name);
                if (renameError != null)
                {
                    errs.Add(prefix + "teamtype \"" + raw.Name + "\": " + renameError);
                    continue;
                }
                CopyTeamInto(raw, team);
            }
            foreach (Trigger raw in rawTriggers)
            {
                Trigger t = triggers.Add();
                if (t == null)
                {
                    errs.Add(prefix + "the map's trigger cap is reached.");
                    return;
                }
                string renameError = triggers.TryRename(t, raw.Name);
                if (renameError != null)
                {
                    errs.Add(prefix + "trigger \"" + raw.Name + "\": " + renameError);
                    continue;
                }
                t.House = raw.House;
                t.PersistentType = raw.PersistentType;
                t.EventControl = raw.EventControl;
                t.Event1.FillDataFrom(raw.Event1);
                t.Event2.FillDataFrom(raw.Event2);
                t.Action1.FillDataFrom(raw.Action1);
                t.Action2.FillDataFrom(raw.Action2);
            }
        }

        private static HouseType ResolveHouse(IGamePlugin plugin, string name, string prefix, List<string> errs, string field = "house")
        {
            if (string.IsNullOrEmpty(name))
            {
                errs.Add(prefix + "missing \"" + field + "\".");
                return null;
            }
            HouseType house = plugin.Map.HouseTypes.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (house == null)
            {
                errs.Add(prefix + "unknown house \"" + name + "\".");
            }
            return house;
        }

        private static Trigger AddTrigger(TriggerEditor triggers, MissionPattern p, string prefix, List<string> errs)
        {
            if (string.IsNullOrEmpty(p.Name))
            {
                errs.Add(prefix + "missing \"name\".");
                return null;
            }
            Trigger t = triggers.Add();
            if (t == null)
            {
                errs.Add(prefix + "the map's trigger cap is reached.");
                return null;
            }
            string renameError = triggers.TryRename(t, p.Name);
            if (renameError != null)
            {
                errs.Add(prefix + renameError);
                return null;
            }
            return t;
        }

        private static TeamType AddTeam(TeamTypeEditor teams, MissionPattern p, string prefix, List<string> errs)
        {
            if (string.IsNullOrEmpty(p.Name))
            {
                errs.Add(prefix + "missing \"name\".");
                return null;
            }
            TeamType team = teams.Add();
            if (team == null)
            {
                errs.Add(prefix + "the map's teamtype cap is reached.");
                return null;
            }
            string renameError = teams.TryRename(team, p.Name);
            if (renameError != null)
            {
                errs.Add(prefix + renameError);
                return null;
            }
            return team;
        }

        /// <summary>Null when any entry is unusable; a team with no units at all is also an error.</summary>
        private static List<TeamTypeClass> ParseUnits(IGamePlugin plugin, MissionPattern p, string prefix, List<string> errs)
        {
            if (p.Units.Count == 0)
            {
                errs.Add(prefix + "needs \"units\" (\"TYPE:count\" entries).");
                return null;
            }
            List<TeamTypeClass> classes = new List<TeamTypeClass>();
            bool ok = true;
            foreach (string entry in p.Units)
            {
                string[] parts = entry.Split(':');
                if (parts.Length != 2 || !byte.TryParse(parts[1], out byte count))
                {
                    errs.Add(prefix + "unit entry \"" + entry + "\" is not \"TYPE:count\".");
                    ok = false;
                    continue;
                }
                ITechnoType type = plugin.Map.TeamTechnoTypes.FirstOrDefault(tt => tt.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                if (type == null)
                {
                    errs.Add(prefix + "unknown unit type \"" + parts[0] + "\".");
                    ok = false;
                    continue;
                }
                classes.Add(new TeamTypeClass { Type = type, Count = count });
            }
            return ok ? classes : null;
        }

        private static void CopyTeamInto(TeamType source, TeamType target)
        {
            target.House = source.House;
            target.IsRoundAbout = source.IsRoundAbout;
            target.IsLearning = source.IsLearning;
            target.IsSuicide = source.IsSuicide;
            target.IsAutocreate = source.IsAutocreate;
            target.IsMercenary = source.IsMercenary;
            target.IsReinforcable = source.IsReinforcable;
            target.IsPrebuilt = source.IsPrebuilt;
            target.RecruitPriority = source.RecruitPriority;
            target.MaxAllowed = source.MaxAllowed;
            target.InitNum = source.InitNum;
            target.Fear = source.Fear;
            target.Origin = source.Origin;
            target.Trigger = source.Trigger;
            target.Classes.AddRange(source.Classes.Select(c => c.Clone()));
            target.Missions.AddRange(source.Missions.Select(m => m.Clone()));
        }
    }
}
