using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using MobiusEditor.Shell;
using Xunit;
using RA = MobiusEditor.RedAlert;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// The pieces a trigger dialog builds on. An interactive edit session exposes the
    /// working-copy editor and touches the map only on Commit (one undo step; Cancel leaves
    /// everything alone). The arg presenter turns an event/action into its value control —
    /// nothing, a number spinner with a range, a Data-bound option list, or a name list
    /// (teams/triggers) — coercing the value into validity first, exactly as the fork's
    /// dialog control switches do.
    /// </summary>
    public class TriggerPanelTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        [Fact]
        public void TriggerSessionTouchesTheMapOnlyOnCommitAsOneUndoStep()
        {
            using (MapDocument doc = Open())
            {
                int before = doc.Map.Triggers.Count;
                MapDocument.TriggerEditSession session = doc.BeginTriggerEdit();
                Trigger a = session.Editor.Add();
                session.Editor.TryRename(a, "one");
                Assert.Equal(before, doc.Map.Triggers.Count);
                Assert.False(doc.CanUndo);
                session.Commit();
                Assert.Contains(doc.Map.Triggers, t => t.Name == "one");
                Assert.True(doc.CanUndo);
                doc.Undo();
                Assert.Equal(before, doc.Map.Triggers.Count);
            }
        }

        [Fact]
        public void CancelledTriggerSessionLeavesNoTrace()
        {
            using (MapDocument doc = Open())
            {
                int before = doc.Map.Triggers.Count;
                MapDocument.TriggerEditSession session = doc.BeginTriggerEdit();
                session.Editor.Add();
                session.Cancel();
                Assert.Equal(before, doc.Map.Triggers.Count);
                Assert.False(doc.CanUndo);
            }
        }

        [Fact]
        public void TeamTypeSessionCommitsAsOneUndoStep()
        {
            using (MapDocument doc = Open())
            {
                MapDocument.TeamTypeEditSession session = doc.BeginTeamTypeEdit();
                TeamType team = session.Editor.Add();
                session.Editor.TryRename(team, "crew");
                session.Commit();
                Assert.Contains(doc.Map.TeamTypes, t => t.Name == "crew");
                doc.Undo();
                Assert.DoesNotContain(doc.Map.TeamTypes, t => t.Name == "crew");
                doc.Redo();
                Assert.Contains(doc.Map.TeamTypes, t => t.Name == "crew");
            }
        }

        [Fact]
        public void NoArgEventPresentsNothingAndIsBlanked()
        {
            using (MapDocument doc = Open())
            {
                TriggerEvent evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_DESTROYED, Data = 5, Team = "x" };
                TriggerArgPresentation p = TriggerArgPresenter.ForEvent(doc.Plugin, evt);
                Assert.Equal(TriggerArgControl.None, p.Control);
                Assert.Equal(0, evt.Data);
                Assert.Equal(TeamType.None, evt.Team);
            }
        }

        [Fact]
        public void NumberEventPresentsTheSpinnerWithTheCoercedValue()
        {
            using (MapDocument doc = Open())
            {
                TriggerEvent evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_TIME, Data = -3 };
                TriggerArgPresentation p = TriggerArgPresenter.ForEvent(doc.Plugin, evt);
                Assert.Equal(TriggerArgControl.Number, p.Control);
                Assert.Equal(0, p.Min);
                Assert.Equal(int.MaxValue, p.Max);
                Assert.Equal(0, p.Value);
                Assert.Equal(0, evt.Data);
            }
        }

        [Fact]
        public void HouseEventPresentsTheOptionListWithTheSnappedValue()
        {
            using (MapDocument doc = Open())
            {
                TriggerEvent evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_PLAYER_ENTERED, Data = 9999 };
                TriggerArgPresentation p = TriggerArgPresenter.ForEvent(doc.Plugin, evt);
                Assert.Equal(TriggerArgControl.DataList, p.Control);
                Assert.Equal((-1L, House.None), p.Options[0]);
                Assert.Equal(-1, p.Value);
                Assert.Equal(-1, evt.Data);
            }
        }

        [Fact]
        public void TeamEventPresentsTheNameListFromTheMapsTeams()
        {
            using (MapDocument doc = Open())
            {
                doc.Map.TeamTypes.Add(new TeamType { Name = "alfa", House = doc.Map.HouseTypes.First() });
                TriggerEvent evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_LEAVES_MAP, Team = "ALFA", Data = 9 };
                TriggerArgPresentation p = TriggerArgPresenter.ForEvent(doc.Plugin, evt);
                Assert.Equal(TriggerArgControl.NameList, p.Control);
                Assert.Equal(TeamType.None, p.Names[0]);
                Assert.Contains("alfa", p.Names);
                Assert.Equal("alfa", p.Name);
                Assert.Equal(0, evt.Data);
            }
        }

        [Fact]
        public void TriggerActionPresentsTheWorkingListsNames()
        {
            using (MapDocument doc = Open())
            {
                Trigger[] working = { new Trigger { Name = "abcd" } };
                TriggerAction act = new TriggerAction { ActionType = RA.ActionTypes.TACTION_FORCE_TRIGGER, Trigger = "ghost" };
                TriggerArgPresentation p = TriggerArgPresenter.ForAction(doc.Plugin, act, working);
                Assert.Equal(TriggerArgControl.NameList, p.Control);
                Assert.Equal(new[] { Trigger.None, "abcd" }, p.Names);
                Assert.Equal(Trigger.None, p.Name);
            }
        }

        [Fact]
        public void MusicActionPresentsThemesAndGlobalActionTheCappedSpinner()
        {
            using (MapDocument doc = Open())
            {
                TriggerAction act = new TriggerAction { ActionType = RA.ActionTypes.TACTION_PLAY_MUSIC, Data = -1 };
                TriggerArgPresentation p = TriggerArgPresenter.ForAction(doc.Plugin, act, Enumerable.Empty<Trigger>());
                Assert.Equal(TriggerArgControl.DataList, p.Control);
                Assert.Equal(doc.Map.ThemeTypes.Count, p.Options.Count);
                act = new TriggerAction { ActionType = RA.ActionTypes.TACTION_SET_GLOBAL, Data = 99 };
                p = TriggerArgPresenter.ForAction(doc.Plugin, act, Enumerable.Empty<Trigger>());
                Assert.Equal(TriggerArgControl.Number, p.Control);
                Assert.Equal(RA.Constants.HighestGlobal, p.Max);
                Assert.Equal(RA.Constants.HighestGlobal, p.Value);
            }
        }

        [Fact]
        public void TeamMissionArgumentsPresentPerArgTypeControls()
        {
            using (MapDocument doc = Open())
            {
                // Waypoint order: the same option list trigger waypoint args use.
                TriggerArgPresentation p = TriggerArgPresenter.ForTeamMission(doc.Plugin, RA.TeamMissionTypes.Move, 9999);
                Assert.Equal(TriggerArgControl.DataList, p.Control);
                Assert.Equal((-1L, Waypoint.None), p.Options[0]);
                Assert.Equal(-1, p.Value);
                // Options-list order: the mission's own dropdown, invalid values snap to the first.
                p = TriggerArgPresenter.ForTeamMission(doc.Plugin, RA.TeamMissionTypes.Attack, 2);
                Assert.Equal(TriggerArgControl.DataList, p.Control);
                Assert.Equal(RA.TeamMissionTypes.Attack.DropdownOptions.Length, p.Options.Count);
                Assert.Equal(2, p.Value);
                // Time order: a spinner; global order: the capped spinner; no-arg: nothing.
                p = TriggerArgPresenter.ForTeamMission(doc.Plugin, RA.TeamMissionTypes.Guard, -5);
                Assert.Equal(TriggerArgControl.Number, p.Control);
                Assert.Equal(0, p.Value);
                p = TriggerArgPresenter.ForTeamMission(doc.Plugin, RA.TeamMissionTypes.SetGlobal, 99);
                Assert.Equal(TriggerArgControl.Number, p.Control);
                Assert.Equal(RA.Constants.HighestGlobal, p.Max);
                Assert.Equal(RA.Constants.HighestGlobal, p.Value);
                p = TriggerArgPresenter.ForTeamMission(doc.Plugin, RA.TeamMissionTypes.Unload, 7);
                Assert.Equal(TriggerArgControl.None, p.Control);
            }
        }

        [Fact]
        public void NoArgActionPresentsNothingAndLeavesDataAlone()
        {
            using (MapDocument doc = Open())
            {
                TriggerAction act = new TriggerAction { ActionType = RA.ActionTypes.TACTION_REVEAL_ALL, Data = 7 };
                TriggerArgPresentation p = TriggerArgPresenter.ForAction(doc.Plugin, act, Enumerable.Empty<Trigger>());
                Assert.Equal(TriggerArgControl.None, p.Control);
                Assert.Equal(7, act.Data);
            }
        }
    }
}
