//
// Headless trigger editing, porting the fork's TriggersDialog semantics: edit a working
// copy of the trigger list plus a rename ledger, then apply everything to the map in one
// Commit. The name invariant ladder matches the fork (non-empty, the game's length cap,
// not the reserved "None", INI-safe characters, case-insensitively unique). Renames and
// deletes rewire every referrer through Map.ApplyTriggerNameChanges — placed objects,
// teamtypes, cell triggers — while other working triggers' action references are rewritten
// here, as the fork's dialog does. The committed list is sorted, because the save format
// writes trigger references as indices into list order.
using System;
using System.Collections.Generic;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Utility;

namespace MobiusEditor.Model
{
    public sealed class TriggerEditor
    {
        private readonly IGamePlugin plugin;
        private readonly List<Trigger> working;
        private readonly List<(string Name1, string Name2)> renameActions = new List<(string, string)>();
        private readonly HashSet<string> addedThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<Trigger> Triggers => working;

        private TriggerEditor(IGamePlugin plugin)
        {
            this.plugin = plugin;
            working = plugin.Map.Triggers.Select(t => t.Clone()).ToList();
        }

        public static TriggerEditor Begin(IGamePlugin plugin) => new TriggerEditor(plugin);

        /// <summary>Adds a trigger with a fresh generated name and the first house; null when the game's cap is reached.</summary>
        public Trigger Add()
        {
            if (working.Count >= plugin.GameInfo.MaxTriggers) return null;
            Trigger trigger = new Trigger
            {
                Name = GeneralUtils.MakeNew4CharName(working.Select(t => t.Name), "????", Trigger.None),
                House = plugin.Map.HouseTypes.First().Name,
            };
            working.Add(trigger);
            addedThisSession.Add(trigger.Name);
            return trigger;
        }

        public Trigger Clone(Trigger source)
        {
            if (working.Count >= plugin.GameInfo.MaxTriggers) return null;
            Trigger clone = source.Clone();
            clone.Name = GeneralUtils.MakeNew4CharName(working.Select(t => t.Name), "----", Trigger.None);
            working.Add(clone);
            addedThisSession.Add(clone.Name);
            return clone;
        }

        public void Remove(Trigger trigger)
        {
            if (!working.Remove(trigger)) return;
            if (!addedThisSession.Remove(trigger.Name))
            {
                renameActions.Add((trigger.Name, Trigger.None));
            }
            RewriteActionReferences(trigger.Name, Trigger.None);
        }

        /// <summary>Renames a working trigger; null on success, otherwise the reason the name is refused.</summary>
        public string TryRename(Trigger trigger, string newName)
        {
            string error = CheckName(newName, plugin.GameInfo.MaxTriggerNameLength,
                working.Where(t => !ReferenceEquals(t, trigger)).Select(t => t.Name));
            if (error != null) return error;
            string oldName = trigger.Name;
            if (addedThisSession.Remove(oldName))
            {
                addedThisSession.Add(newName);
            }
            else
            {
                renameActions.Add((oldName, newName));
            }
            trigger.Name = newName;
            RewriteActionReferences(oldName, newName);
            return null;
        }

        /// <summary>Applies the working list and rename ledger to the map, rewiring and cleaning every referrer.</summary>
        public void Commit()
        {
            List<Trigger> sorted = working.OrderBy(t => t.Name, new ExplorerComparer()).ToList();
            plugin.Map.ApplyTriggerNameChanges(renameActions, out _, out _, out _, sorted);
            plugin.Map.Triggers = sorted;
            plugin.Dirty = true;
            renameActions.Clear();
            addedThisSession.Clear();
        }

        internal static string CheckName(string name, int maxLength, IEnumerable<string> taken)
        {
            if (string.IsNullOrEmpty(name)) return "The name cannot be empty.";
            if (name.Length > maxLength) return $"The name cannot be longer than {maxLength} characters.";
            if (string.Equals(name, Trigger.None, StringComparison.OrdinalIgnoreCase)) return $"'{Trigger.None}' is a reserved name.";
            if (!INITools.IsValidKey(name)) return "The name contains characters that cannot be saved in an INI file.";
            if (taken.Contains(name, StringComparer.OrdinalIgnoreCase)) return $"A trigger or team named '{name}' already exists.";
            return null;
        }

        private void RewriteActionReferences(string oldName, string newName)
        {
            foreach (Trigger t in working)
            {
                if (string.Equals(t.Action1.Trigger, oldName, StringComparison.OrdinalIgnoreCase)) t.Action1.Trigger = newName;
                if (string.Equals(t.Action2.Trigger, oldName, StringComparison.OrdinalIgnoreCase)) t.Action2.Trigger = newName;
            }
        }
    }

    /// <summary>The teamtype counterpart: same working-copy model; renames rewire the triggers' team references.</summary>
    public sealed class TeamTypeEditor
    {
        private readonly IGamePlugin plugin;
        private readonly List<TeamType> working;
        private readonly List<(string Name1, string Name2)> renameActions = new List<(string, string)>();
        private readonly HashSet<string> addedThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<TeamType> TeamTypes => working;

        private TeamTypeEditor(IGamePlugin plugin)
        {
            this.plugin = plugin;
            working = plugin.Map.TeamTypes.Select(CloneTeam).ToList();
        }

        public static TeamTypeEditor Begin(IGamePlugin plugin) => new TeamTypeEditor(plugin);

        public TeamType Add()
        {
            if (working.Count >= plugin.GameInfo.MaxTeams) return null;
            TeamType team = new TeamType
            {
                Name = GeneralUtils.MakeNew4CharName(working.Select(t => t.Name), "????", TeamType.None),
                House = plugin.Map.HouseTypes.First(),
            };
            working.Add(team);
            addedThisSession.Add(team.Name);
            return team;
        }

        public void Remove(TeamType team)
        {
            if (!working.Remove(team)) return;
            if (!addedThisSession.Remove(team.Name))
            {
                renameActions.Add((team.Name, TeamType.None));
            }
        }

        public string TryRename(TeamType team, string newName)
        {
            string error = TriggerEditor.CheckName(newName, plugin.GameInfo.MaxTeamNameLength,
                working.Where(t => !ReferenceEquals(t, team)).Select(t => t.Name));
            if (error != null) return error;
            string oldName = team.Name;
            if (addedThisSession.Remove(oldName))
            {
                addedThisSession.Add(newName);
            }
            else
            {
                renameActions.Add((oldName, newName));
            }
            team.Name = newName;
            return null;
        }

        public void Commit()
        {
            plugin.Map.TeamTypes.Clear();
            plugin.Map.ApplyTeamTypeRenames(renameActions);
            plugin.Map.TeamTypes.AddRange(working.OrderBy(t => t.Name, new ExplorerComparer()));
            plugin.Dirty = true;
            renameActions.Clear();
            addedThisSession.Clear();
        }

        private static TeamType CloneTeam(TeamType source)
        {
            TeamType clone = new TeamType
            {
                Name = source.Name,
                House = source.House,
                IsRoundAbout = source.IsRoundAbout,
                IsLearning = source.IsLearning,
                IsSuicide = source.IsSuicide,
                IsAutocreate = source.IsAutocreate,
                IsMercenary = source.IsMercenary,
                IsReinforcable = source.IsReinforcable,
                IsPrebuilt = source.IsPrebuilt,
                RecruitPriority = source.RecruitPriority,
                MaxAllowed = source.MaxAllowed,
                InitNum = source.InitNum,
                Fear = source.Fear,
                Origin = source.Origin,
                Trigger = source.Trigger,
            };
            clone.Classes.AddRange(source.Classes.Select(c => c.Clone()));
            clone.Missions.AddRange(source.Missions.Select(m => m.Clone()));
            return clone;
        }
    }
}
