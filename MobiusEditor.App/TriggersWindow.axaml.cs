using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Shell;
using MobiusEditor.Utility;

namespace MobiusEditor.App
{
    /// <summary>
    /// The trigger dialog: edits a working copy through a MapDocument trigger edit session,
    /// so the map changes only when OK commits — one undo step. Value controls per
    /// event/action come from TriggerArgPresenter (a spinner, an option list, a name list,
    /// or nothing), the headless port of the original dialog's control switches.
    /// </summary>
    public partial class TriggersWindow : Window
    {
        private readonly MapDocument document;
        private readonly MapDocument.TriggerEditSession session;
        private bool committed;
        private bool updating;
        private ArgSlot event1Slot, event2Slot, action1Slot, action2Slot;

        public TriggerEditor Editor => session.Editor;
        public Trigger Selected => TriggerList.SelectedIndex >= 0 && TriggerList.SelectedIndex < Editor.Triggers.Count
            ? Editor.Triggers[TriggerList.SelectedIndex] : null;

        public TriggersWindow()
        {
            InitializeComponent();
        }

        public TriggersWindow(MapDocument document) : this()
        {
            this.document = document;
            session = document.BeginTriggerEdit();
            IGamePlugin plugin = document.Plugin;
            HouseCombo.ItemsSource = House.None.Yield().Concat(document.Map.Houses.Select(h => h.Type.Name)).ToList();
            PersistenceCombo.ItemsSource = Trigger.PersistenceNames.ToList();
            EventControlCombo.ItemsSource = Trigger.MultiStyleNames.ToList();
            event1Slot = new ArgSlot(this, Event1Type, Event1Nud, Event1Value, () => Selected?.Event1, null);
            event2Slot = new ArgSlot(this, Event2Type, Event2Nud, Event2Value, () => Selected?.Event2, null);
            action1Slot = new ArgSlot(this, Action1Type, Action1Nud, Action1Value, null, () => Selected?.Action1);
            action2Slot = new ArgSlot(this, Action2Type, Action2Nud, Action2Value, null, () => Selected?.Action2);
            AddButton.Click += (s, e) => AddTrigger();
            CloneButton.Click += (s, e) => CloneTrigger();
            RemoveButton.Click += (s, e) => RemoveTrigger();
            RenameButton.Click += (s, e) => RenameTrigger();
            TriggerList.SelectionChanged += (s, e) => { if (!updating) RefreshDetail(); };
            HouseCombo.SelectionChanged += (s, e) => { if (!updating && Selected != null && HouseCombo.SelectedItem is string h) { Selected.House = h; UpdateSummary(); } };
            PersistenceCombo.SelectionChanged += (s, e) => { if (!updating && Selected != null && PersistenceCombo.SelectedIndex >= 0) { Selected.PersistentType = (TriggerPersistentType)PersistenceCombo.SelectedIndex; UpdateSummary(); } };
            EventControlCombo.SelectionChanged += (s, e) => { if (!updating && Selected != null && EventControlCombo.SelectedIndex >= 0) { Selected.EventControl = (TriggerMultiStyleType)EventControlCombo.SelectedIndex; ApplyEventControl(); UpdateSummary(); } };
            OkButton.Click += (s, e) => { session.Commit(); committed = true; Close(); };
            CancelButton.Click += (s, e) => Close();
            Closed += (s, e) => { if (!committed) session.Cancel(); };
            RefreshList(Editor.Triggers.FirstOrDefault());
        }

        private void AddTrigger()
        {
            Trigger t = Editor.Add();
            if (t == null) { ErrorLabel.Text = "The trigger limit is reached."; return; }
            ErrorLabel.Text = "";
            RefreshList(t);
        }

        private void CloneTrigger()
        {
            if (Selected == null) return;
            Trigger t = Editor.Clone(Selected);
            if (t == null) { ErrorLabel.Text = "The trigger limit is reached."; return; }
            ErrorLabel.Text = "";
            RefreshList(t);
        }

        private void RemoveTrigger()
        {
            if (Selected == null) return;
            Editor.Remove(Selected);
            ErrorLabel.Text = "";
            RefreshList(Editor.Triggers.FirstOrDefault());
        }

        private void RenameTrigger()
        {
            if (Selected == null) return;
            string error = Editor.TryRename(Selected, RenameBox.Text ?? "");
            ErrorLabel.Text = error ?? "";
            if (error == null) RefreshList(Selected);
        }

        private void RefreshList(Trigger select)
        {
            updating = true;
            TriggerList.ItemsSource = Editor.Triggers.Select(t => t.Name).ToList();
            updating = false;
            TriggerList.SelectedIndex = select == null ? -1 : Editor.Triggers.ToList().IndexOf(select);
            RefreshDetail();
        }

        private void RefreshDetail()
        {
            Trigger t = Selected;
            DetailPanel.IsEnabled = t != null;
            if (t == null)
            {
                SummaryLabel.Text = "";
                return;
            }
            updating = true;
            RenameBox.Text = t.Name;
            List<string> houses = (List<string>)HouseCombo.ItemsSource;
            HouseCombo.SelectedIndex = Math.Max(0, houses.FindIndex(h => h.Equals(t.House ?? House.None, StringComparison.OrdinalIgnoreCase)));
            PersistenceCombo.SelectedIndex = (int)t.PersistentType;
            EventControlCombo.SelectedIndex = (int)t.EventControl;
            updating = false;
            event1Slot.Refresh();
            event2Slot.Refresh();
            action1Slot.Refresh();
            action2Slot.Refresh();
            ApplyEventControl();
            UpdateSummary();
        }

        /// <summary>Event 2 exists only for multi-event styles; hiding it resets it to None, as the original dialog does.</summary>
        private void ApplyEventControl()
        {
            Trigger t = Selected;
            bool hasEvent2 = t != null && t.EventControl != TriggerMultiStyleType.Only;
            if (!hasEvent2 && t != null && !TriggerEvent.IsEmpty(t.Event2.EventType))
            {
                t.Event2.EventType = TriggerEvent.None;
                document.Plugin.CoerceEventArg(t.Event2);
                event2Slot.Refresh();
            }
            Event2Panel.IsVisible = hasEvent2;
        }

        private void UpdateSummary()
        {
            SummaryLabel.Text = Selected == null ? "" : document.Plugin.TriggerSummary(Selected, false, false);
        }

        /// <summary>One event/action group: the type combo plus the value control the arg type calls for.</summary>
        private sealed class ArgSlot
        {
            private readonly TriggersWindow owner;
            private readonly ComboBox type;
            private readonly NumericUpDown nud;
            private readonly ComboBox value;
            private readonly Func<TriggerEvent> getEvent;
            private readonly Func<TriggerAction> getAction;
            private TriggerArgPresentation presentation;

            public ArgSlot(TriggersWindow owner, ComboBox type, NumericUpDown nud, ComboBox value, Func<TriggerEvent> getEvent, Func<TriggerAction> getAction)
            {
                this.owner = owner;
                this.type = type;
                this.nud = nud;
                this.value = value;
                this.getEvent = getEvent;
                this.getAction = getAction;
                Map map = owner.document.Map;
                type.ItemsSource = (getEvent != null ? map.EventTypes : map.ActionTypes).ToList();
                type.SelectionChanged += (s, e) => OnTypeChanged();
                nud.ValueChanged += (s, e) => OnNumberChanged();
                value.SelectionChanged += (s, e) => OnValueChanged();
            }

            /// <summary>A manual type pick resets the slot's value to defaults before coercion, as the original dialog does.</summary>
            private void OnTypeChanged()
            {
                if (owner.updating || !(type.SelectedItem is string picked)) return;
                TriggerEvent evt = getEvent?.Invoke();
                TriggerAction act = getAction?.Invoke();
                if (evt != null)
                {
                    evt.EventType = picked;
                    evt.Data = 0;
                    evt.Team = TeamType.None;
                }
                else if (act != null)
                {
                    act.ActionType = picked;
                    act.Data = 0;
                    act.Team = TeamType.None;
                    act.Trigger = Trigger.None;
                }
                else return;
                Refresh();
                owner.UpdateSummary();
            }

            private void OnNumberChanged()
            {
                if (owner.updating || presentation == null || presentation.Control != TriggerArgControl.Number) return;
                long v = (long)(nud.Value ?? 0);
                TriggerEvent evt = getEvent?.Invoke();
                if (evt != null) evt.Data = v; else if (getAction?.Invoke() is TriggerAction act) act.Data = v;
                owner.UpdateSummary();
            }

            private void OnValueChanged()
            {
                if (owner.updating || presentation == null || value.SelectedIndex < 0) return;
                TriggerEvent evt = getEvent?.Invoke();
                TriggerAction act = getAction?.Invoke();
                if (presentation.Control == TriggerArgControl.DataList)
                {
                    long v = presentation.Options[value.SelectedIndex].Value;
                    if (evt != null) evt.Data = v; else if (act != null) act.Data = v;
                }
                else if (presentation.Control == TriggerArgControl.NameList)
                {
                    string name = presentation.Names[value.SelectedIndex];
                    if (evt != null) evt.Team = name;
                    else if (act != null && presentation.ArgType == TriggerArgType.TeamType) act.Team = name;
                    else if (act != null) act.Trigger = name;
                }
                owner.UpdateSummary();
            }

            public void Refresh()
            {
                TriggerEvent evt = getEvent?.Invoke();
                TriggerAction act = getAction?.Invoke();
                bool prior = owner.updating;
                owner.updating = true;
                try
                {
                    if (evt == null && act == null)
                    {
                        presentation = null;
                        nud.IsVisible = value.IsVisible = false;
                        return;
                    }
                    IGamePlugin plugin = owner.document.Plugin;
                    presentation = evt != null
                        ? TriggerArgPresenter.ForEvent(plugin, evt)
                        : TriggerArgPresenter.ForAction(plugin, act, owner.Editor.Triggers);
                    type.SelectedItem = evt != null ? evt.EventType : act.ActionType;
                    nud.IsVisible = presentation.Control == TriggerArgControl.Number;
                    value.IsVisible = presentation.Control == TriggerArgControl.DataList || presentation.Control == TriggerArgControl.NameList;
                    switch (presentation.Control)
                    {
                        case TriggerArgControl.Number:
                            nud.Minimum = presentation.Min;
                            nud.Maximum = presentation.Max;
                            nud.Value = presentation.Value;
                            break;
                        case TriggerArgControl.DataList:
                            value.ItemsSource = presentation.Options.Select(o => o.Label).ToList();
                            value.SelectedIndex = IndexOfValue(presentation.Options, presentation.Value);
                            break;
                        case TriggerArgControl.NameList:
                            value.ItemsSource = presentation.Names.ToList();
                            value.SelectedIndex = IndexOfName(presentation.Names, presentation.Name);
                            break;
                    }
                }
                finally
                {
                    owner.updating = prior;
                }
            }

            private static int IndexOfValue(IReadOnlyList<(long Value, string Label)> options, long v)
            {
                for (int i = 0; i < options.Count; i++) if (options[i].Value == v) return i;
                return options.Count > 0 ? 0 : -1;
            }

            private static int IndexOfName(IReadOnlyList<string> names, string name)
            {
                for (int i = 0; i < names.Count; i++) if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
                return names.Count > 0 ? 0 : -1;
            }
        }
    }
}
