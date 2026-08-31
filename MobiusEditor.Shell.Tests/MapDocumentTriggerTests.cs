using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using MobiusEditor.RedAlert;
using MobiusEditor.Shell;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// Trigger and teamtype editing through the document is one undo step per edit session:
    /// undo restores the previous trigger/teamtype lists AND every rewired referrer — cell
    /// triggers put back on their cells, teamtype links, triggers' team references.
    /// </summary>
    public class MapDocumentTriggerTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        [Fact]
        public void ATriggerEditSessionIsOneUndoStep()
        {
            using (MapDocument doc = Open())
            {
                doc.EditTriggers(ed =>
                {
                    Trigger a = ed.Add();
                    ed.TryRename(a, "one");
                    Trigger b = ed.Add();
                    ed.TryRename(b, "two");
                });
                Assert.Contains(doc.Map.Triggers, t => t.Name == "one");
                Assert.Contains(doc.Map.Triggers, t => t.Name == "two");
                Assert.True(doc.CanUndo);
                doc.Undo();
                Assert.DoesNotContain(doc.Map.Triggers, t => t.Name == "one");
                Assert.DoesNotContain(doc.Map.Triggers, t => t.Name == "two");
                doc.Redo();
                Assert.Contains(doc.Map.Triggers, t => t.Name == "one");
            }
        }

        [Fact]
        public void UndoPutsRewiredReferencesBack()
        {
            using (MapDocument doc = Open())
            {
                doc.EditTriggers(ed =>
                {
                    Trigger t = ed.Add();
                    ed.TryRename(t, "abcd");
                    t.Event1.EventType = EventTypes.TEVENT_DISCOVERED;
                });
                doc.Map.CellTriggers[300] = new CellTrigger("abcd");
                doc.Map.TeamTypes.Add(new TeamType { Name = "tema", House = doc.Map.HouseTypes.First(), Trigger = "abcd" });

                doc.EditTriggers(ed => ed.TryRename(ed.Triggers.Single(t => t.Name == "abcd"), "neu"));
                Assert.Equal("neu", doc.Map.CellTriggers[300].Trigger);
                doc.Undo();
                Assert.Equal("abcd", doc.Map.CellTriggers[300].Trigger);
                Assert.Equal("abcd", doc.Map.TeamTypes.Single(t => t.Name == "tema").Trigger);
                Assert.Contains(doc.Map.Triggers, t => t.Name == "abcd");
                doc.Redo();
                Assert.Equal("neu", doc.Map.CellTriggers[300].Trigger);
            }
        }

        [Fact]
        public void DeletedCellTriggersComeBackOnUndo()
        {
            using (MapDocument doc = Open())
            {
                doc.EditTriggers(ed =>
                {
                    Trigger t = ed.Add();
                    ed.TryRename(t, "cell");
                    t.Event1.EventType = EventTypes.TEVENT_PLAYER_ENTERED;
                });
                doc.Map.CellTriggers[400] = new CellTrigger("cell");
                doc.EditTriggers(ed => ed.Remove(ed.Triggers.Single(t => t.Name == "cell")));
                Assert.True(doc.Map.CellTriggers[400] == null || Trigger.IsEmpty(doc.Map.CellTriggers[400].Trigger));
                doc.Undo();
                Assert.Equal("cell", doc.Map.CellTriggers[400]?.Trigger);
            }
        }

        [Fact]
        public void ATeamTypeEditSessionIsOneUndoStepAndRestoresTriggerTeamReferences()
        {
            using (MapDocument doc = Open())
            {
                doc.EditTriggers(ed =>
                {
                    Trigger t = ed.Add();
                    ed.TryRename(t, "wtch");
                    t.Event1.EventType = EventTypes.TEVENT_LEAVES_MAP;
                    t.Event1.Team = "olds";
                });
                doc.EditTeamTypes(ed =>
                {
                    TeamType team = ed.Add();
                    ed.TryRename(team, "olds");
                });
                doc.EditTeamTypes(ed => ed.TryRename(ed.TeamTypes.Single(t => t.Name == "olds"), "newteam"));
                Assert.Equal("newteam", doc.Map.Triggers.Single(t => t.Name == "wtch").Event1.Team);
                doc.Undo();
                Assert.Equal("olds", doc.Map.Triggers.Single(t => t.Name == "wtch").Event1.Team);
                Assert.Contains(doc.Map.TeamTypes, t => t.Name == "olds");
            }
        }
    }
}
