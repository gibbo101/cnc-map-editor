using System.IO;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.RedAlert;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>
    /// TriggerEditor/TeamTypeEditor port the fork's dialog semantics headlessly: a working
    /// copy of the list plus a rename ledger, applied to the map in one commit. The name
    /// invariant ladder matches the fork (non-empty, game's length cap, not the reserved
    /// "None", INI-safe characters, case-insensitively unique). Renames and deletes rewire
    /// every referrer — cell triggers, placed objects, teamtypes, other triggers' actions —
    /// and the committed list is sorted, because the save format writes references as
    /// indices into list order.
    /// </summary>
    public class TriggerEditorTests
    {
        private static IGamePlugin Load() =>
            EditorHost.Shared.Load(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), out _);

        [Fact]
        public void AddAssignsAUniqueShortNameAndTheFirstHouse()
        {
            IGamePlugin plugin = Load();
            TriggerEditor editor = TriggerEditor.Begin(plugin);
            int before = plugin.Map.Triggers.Count;
            Trigger t = editor.Add();
            Assert.NotNull(t);
            Assert.Equal(4, t.Name.Length);
            Assert.Equal(plugin.Map.HouseTypes.First().Name, t.House);
            Assert.Equal(before, plugin.Map.Triggers.Count);
            editor.Commit();
            Assert.Contains(plugin.Map.Triggers, x => x.Name == t.Name);
            Assert.True(plugin.Dirty);
        }

        [Fact]
        public void RenameEnforcesTheInvariantLadder()
        {
            IGamePlugin plugin = Load();
            TriggerEditor editor = TriggerEditor.Begin(plugin);
            Trigger a = editor.Add();
            Trigger b = editor.Add();
            Assert.NotNull(editor.TryRename(a, ""));
            Assert.NotNull(editor.TryRename(a, "toolong"));
            Assert.NotNull(editor.TryRename(a, "None"));
            Assert.NotNull(editor.TryRename(a, "a=b"));
            Assert.Null(editor.TryRename(a, "okay"));
            Assert.Equal("okay", a.Name);
            Assert.NotNull(editor.TryRename(b, "OKAY"));
        }

        [Fact]
        public void CommitRewiresEveryReferrerOnRename()
        {
            IGamePlugin plugin = Load();
            Map map = plugin.Map;
            TriggerEditor setup = TriggerEditor.Begin(plugin);
            Trigger target = setup.Add();
            setup.TryRename(target, "abcd");
            target.Event1.EventType = EventTypes.TEVENT_DISCOVERED;
            Trigger referrer = setup.Add();
            setup.TryRename(referrer, "refr");
            referrer.Action1.ActionType = ActionTypes.TACTION_FORCE_TRIGGER;
            referrer.Action1.Trigger = "abcd";
            setup.Commit();
            map.CellTriggers[100] = new CellTrigger("abcd");
            map.TeamTypes.Add(new TeamType { Name = "team", House = map.HouseTypes.First(), Trigger = "abcd" });

            TriggerEditor editor = TriggerEditor.Begin(plugin);
            Assert.Null(editor.TryRename(editor.Triggers.Single(t => t.Name == "abcd"), "neu"));
            editor.Commit();

            Assert.Equal("neu", map.CellTriggers[100].Trigger);
            Assert.Equal("neu", map.TeamTypes.Single(t => t.Name == "team").Trigger);
            Assert.Equal("neu", map.Triggers.Single(t => t.Name == "refr").Action1.Trigger);
        }

        [Fact]
        public void CommitClearsEveryReferrerOnDelete()
        {
            IGamePlugin plugin = Load();
            Map map = plugin.Map;
            TriggerEditor setup = TriggerEditor.Begin(plugin);
            Trigger target = setup.Add();
            setup.TryRename(target, "gone");
            target.Event1.EventType = EventTypes.TEVENT_DISCOVERED;
            Trigger referrer = setup.Add();
            setup.TryRename(referrer, "stay");
            referrer.Action1.ActionType = ActionTypes.TACTION_FORCE_TRIGGER;
            referrer.Action1.Trigger = "gone";
            setup.Commit();
            map.CellTriggers[200] = new CellTrigger("gone");
            map.TeamTypes.Add(new TeamType { Name = "team2", House = map.HouseTypes.First(), Trigger = "gone" });

            TriggerEditor editor = TriggerEditor.Begin(plugin);
            editor.Remove(editor.Triggers.Single(t => t.Name == "gone"));
            editor.Commit();

            Assert.DoesNotContain(map.Triggers, t => t.Name == "gone");
            Assert.True(map.CellTriggers[200] == null || Trigger.IsEmpty(map.CellTriggers[200].Trigger));
            Assert.Equal(Trigger.None, map.TeamTypes.Single(t => t.Name == "team2").Trigger);
            Assert.Equal(Trigger.None, map.Triggers.Single(t => t.Name == "stay").Action1.Trigger);
        }

        [Fact]
        public void CommitSortsTheListBecauseSavesWriteIndices()
        {
            IGamePlugin plugin = Load();
            TriggerEditor editor = TriggerEditor.Begin(plugin);
            editor.TryRename(editor.Add(), "zzz");
            editor.TryRename(editor.Add(), "aaa");
            editor.Commit();
            var names = plugin.Map.Triggers.Select(t => t.Name).ToList();
            Assert.True(names.IndexOf("aaa") < names.IndexOf("zzz"));
        }

        [Fact]
        public void AddRefusesBeyondTheGameCap()
        {
            IGamePlugin plugin = Load();
            TriggerEditor editor = TriggerEditor.Begin(plugin);
            while (editor.Add() != null) { }
            Assert.Equal(plugin.GameInfo.MaxTriggers, editor.Triggers.Count);
        }

        [Fact]
        public void RemovingATriggerAddedThisSessionLeavesNoRenameGhost()
        {
            IGamePlugin plugin = Load();
            TriggerEditor editor = TriggerEditor.Begin(plugin);
            Trigger t = editor.Add();
            editor.Remove(t);
            editor.Commit();
            Assert.DoesNotContain(plugin.Map.Triggers, x => x.Name == t.Name);
        }
    }

    public class TeamTypeEditorTests
    {
        private static IGamePlugin Load() =>
            EditorHost.Shared.Load(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), out _);

        [Fact]
        public void AddRenameLadderUsesTheLongerTeamNameCap()
        {
            IGamePlugin plugin = Load();
            TeamTypeEditor editor = TeamTypeEditor.Begin(plugin);
            TeamType t = editor.Add();
            Assert.NotNull(t);
            Assert.Null(editor.TryRename(t, "eightchr"));
            Assert.NotNull(editor.TryRename(t, "ninechars"));
            Assert.NotNull(editor.TryRename(t, "None"));
        }

        [Fact]
        public void RenameAndDeleteRewireTriggerTeamReferences()
        {
            IGamePlugin plugin = Load();
            Map map = plugin.Map;
            TriggerEditor triggers = TriggerEditor.Begin(plugin);
            Trigger watcher = triggers.Add();
            triggers.TryRename(watcher, "wtch");
            watcher.Event1.EventType = EventTypes.TEVENT_LEAVES_MAP;
            watcher.Event1.Team = "olds";
            triggers.Commit();
            map.TeamTypes.Add(new TeamType { Name = "olds", House = map.HouseTypes.First() });

            TeamTypeEditor editor = TeamTypeEditor.Begin(plugin);
            Assert.Null(editor.TryRename(editor.TeamTypes.Single(t => t.Name == "olds"), "newteam"));
            editor.Commit();
            Assert.Equal("newteam", map.Triggers.Single(t => t.Name == "wtch").Event1.Team);

            TeamTypeEditor again = TeamTypeEditor.Begin(plugin);
            again.Remove(again.TeamTypes.Single(t => t.Name == "newteam"));
            again.Commit();
            Assert.Equal(TeamType.None, map.Triggers.Single(t => t.Name == "wtch").Event1.Team);
            Assert.Empty(map.TeamTypes.Where(t => t.Name == "newteam"));
        }
    }
}
