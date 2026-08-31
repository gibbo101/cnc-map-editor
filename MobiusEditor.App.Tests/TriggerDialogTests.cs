using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;
using RA = MobiusEditor.RedAlert;

namespace MobiusEditor.App.Tests
{
    /// <summary>
    /// The trigger and teamtype dialogs work end to end on the headless platform: they edit
    /// a working copy, populate the value controls the arg type calls for, commit as a
    /// single undo step on OK, and leave no trace on Cancel. Semantics live in the Shell
    /// and core editors; these tests pin the wiring.
    /// </summary>
    public class TriggerDialogTests
    {
        private static MainWindow Open()
        {
            string map = Path.Combine(TestPaths.MapEdits, "scm05ea.ini");
            MainWindow window = new MainWindow(new[] { map, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir });
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        [AvaloniaFact]
        public void AddRenameEditAndOkCommitAsOneUndoStep()
        {
            MainWindow window = Open();
            TriggersWindow dialog = window.OpenTriggersDialog();
            Dispatcher.UIThread.RunJobs();
            dialog.FindControl<Button>("AddButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(dialog.Selected);
            dialog.FindControl<TextBox>("RenameBox").Text = "atk1";
            dialog.FindControl<Button>("RenameButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("atk1", dialog.Selected.Name);

            // Pick a number event and set its value through the spinner.
            ComboBox event1Type = dialog.FindControl<ComboBox>("Event1Type");
            event1Type.SelectedItem = RA.EventTypes.TEVENT_TIME;
            Dispatcher.UIThread.RunJobs();
            NumericUpDown nud = dialog.FindControl<NumericUpDown>("Event1Nud");
            Assert.True(nud.IsVisible);
            nud.Value = 30;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(30, dialog.Selected.Event1.Data);

            // A house action populates the option list.
            ComboBox action1Type = dialog.FindControl<ComboBox>("Action1Type");
            action1Type.SelectedItem = RA.ActionTypes.TACTION_WIN;
            Dispatcher.UIThread.RunJobs();
            ComboBox action1Value = dialog.FindControl<ComboBox>("Action1Value");
            Assert.True(action1Value.IsVisible);
            Assert.Contains("USSR", action1Value.Items.Cast<string>());

            // The map is untouched until OK, then the whole session is one undo step.
            Assert.DoesNotContain(window.Document.Map.Triggers, t => t.Name == "atk1");
            dialog.FindControl<Button>("OkButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Trigger committed = window.Document.Map.Triggers.Single(t => t.Name == "atk1");
            Assert.Equal(RA.EventTypes.TEVENT_TIME, committed.Event1.EventType);
            Assert.Equal(30, committed.Event1.Data);
            window.Document.Undo();
            Assert.DoesNotContain(window.Document.Map.Triggers, t => t.Name == "atk1");
        }

        [AvaloniaFact]
        public void EventTwoAppearsOnlyForMultiEventStyles()
        {
            MainWindow window = Open();
            TriggersWindow dialog = window.OpenTriggersDialog();
            Dispatcher.UIThread.RunJobs();
            dialog.FindControl<Button>("AddButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            StackPanel event2Panel = dialog.FindControl<StackPanel>("Event2Panel");
            Assert.False(event2Panel.IsVisible);
            dialog.FindControl<ComboBox>("EventControlCombo").SelectedIndex = (int)TriggerMultiStyleType.And;
            Dispatcher.UIThread.RunJobs();
            Assert.True(event2Panel.IsVisible);
            Assert.Equal(TriggerMultiStyleType.And, dialog.Selected.EventControl);
        }

        [AvaloniaFact]
        public void CancelLeavesTheMapUntouched()
        {
            MainWindow window = Open();
            int before = window.Document.Map.Triggers.Count;
            TriggersWindow dialog = window.OpenTriggersDialog();
            Dispatcher.UIThread.RunJobs();
            dialog.FindControl<Button>("AddButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            dialog.FindControl<Button>("CancelButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, window.Document.Map.Triggers.Count);
            Assert.False(window.Document.CanUndo);
        }

        [AvaloniaFact]
        public void TeamDialogBuildsATeamAndCommitsAsOneUndoStep()
        {
            MainWindow window = Open();
            TeamTypesWindow dialog = window.OpenTeamsDialog();
            Dispatcher.UIThread.RunJobs();
            dialog.FindControl<Button>("AddButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            dialog.FindControl<TextBox>("RenameBox").Text = "crew";
            dialog.FindControl<Button>("RenameButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            ComboBox classType = dialog.FindControl<ComboBox>("ClassTypeCombo");
            classType.SelectedItem = classType.Items.Cast<string>().First(n => n.Equals("e1", System.StringComparison.OrdinalIgnoreCase));
            dialog.FindControl<NumericUpDown>("ClassCountNud").Value = 3;
            dialog.FindControl<Button>("ClassAddButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(dialog.Selected.Classes);
            dialog.FindControl<CheckBox>("ReinforcableCheck").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            dialog.FindControl<Button>("OkButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            TeamType committed = window.Document.Map.TeamTypes.Single(t => t.Name == "crew");
            Assert.True(committed.IsReinforcable);
            Assert.Equal(3, committed.Classes.Single().Count);
            window.Document.Undo();
            Assert.DoesNotContain(window.Document.Map.TeamTypes, t => t.Name == "crew");
        }
    }
}
