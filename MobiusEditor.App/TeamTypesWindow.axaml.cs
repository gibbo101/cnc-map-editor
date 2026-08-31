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
    /// The teamtype dialog: edits a working copy through a MapDocument teamtype edit
    /// session; the map changes only when OK commits — one undo step. First pass: flags,
    /// numbers, house and trigger link, and plain add/remove rows for unit classes and
    /// orders (the order argument is a raw number; the hint line names what the selected
    /// order expects).
    /// </summary>
    public partial class TeamTypesWindow : Window
    {
        private readonly MapDocument document;
        private readonly MapDocument.TeamTypeEditSession session;
        private bool committed;
        private bool updating;
        private List<ITechnoType> classTypes = new List<ITechnoType>();
        private List<TeamMission> missionTypes = new List<TeamMission>();

        public TeamTypeEditor Editor => session.Editor;
        public TeamType Selected => TeamList.SelectedIndex >= 0 && TeamList.SelectedIndex < Editor.TeamTypes.Count
            ? Editor.TeamTypes[TeamList.SelectedIndex] : null;

        public TeamTypesWindow()
        {
            InitializeComponent();
        }

        public TeamTypesWindow(MapDocument document) : this()
        {
            this.document = document;
            session = document.BeginTeamTypeEdit();
            Map map = document.Map;
            HouseCombo.ItemsSource = map.HouseTypes.Select(h => h.Name).ToList();
            TriggerCombo.ItemsSource = Trigger.None.Yield().Concat(map.Triggers.Select(t => t.Name)).ToList();
            classTypes = map.TeamTechnoTypes.ToList();
            ClassTypeCombo.ItemsSource = classTypes.Select(t => t.Name).ToList();
            missionTypes = map.TeamMissionTypes.ToList();
            MissionCombo.ItemsSource = missionTypes.Select(m => m.Mission).ToList();
            MissionCombo.SelectionChanged += (s, e) => UpdateMissionHint();
            AddButton.Click += (s, e) => AddTeam();
            RemoveButton.Click += (s, e) => RemoveTeam();
            RenameButton.Click += (s, e) => RenameTeam();
            TeamList.SelectionChanged += (s, e) => { if (!updating) RefreshDetail(); };
            HouseCombo.SelectionChanged += (s, e) => { if (Ready()) Selected.House = document.Map.HouseTypes.First(h => h.Name == (string)HouseCombo.SelectedItem); };
            TriggerCombo.SelectionChanged += (s, e) => { if (Ready() && TriggerCombo.SelectedItem is string trig) Selected.Trigger = trig; };
            PriorityNud.ValueChanged += (s, e) => { if (Ready()) Selected.RecruitPriority = (int)(PriorityNud.Value ?? 0); };
            InitNumNud.ValueChanged += (s, e) => { if (Ready()) Selected.InitNum = (byte)(InitNumNud.Value ?? 0); };
            MaxAllowedNud.ValueChanged += (s, e) => { if (Ready()) Selected.MaxAllowed = (byte)(MaxAllowedNud.Value ?? 0); };
            FearNud.ValueChanged += (s, e) => { if (Ready()) Selected.Fear = (byte)(FearNud.Value ?? 0); };
            OriginNud.ValueChanged += (s, e) => { if (Ready()) Selected.Origin = (int)(OriginNud.Value ?? -1); };
            RoundAboutCheck.IsCheckedChanged += (s, e) => { if (Ready()) Selected.IsRoundAbout = RoundAboutCheck.IsChecked == true; };
            LearningCheck.IsCheckedChanged += (s, e) => { if (Ready()) Selected.IsLearning = LearningCheck.IsChecked == true; };
            SuicideCheck.IsCheckedChanged += (s, e) => { if (Ready()) Selected.IsSuicide = SuicideCheck.IsChecked == true; };
            AutocreateCheck.IsCheckedChanged += (s, e) => { if (Ready()) Selected.IsAutocreate = AutocreateCheck.IsChecked == true; };
            MercenaryCheck.IsCheckedChanged += (s, e) => { if (Ready()) Selected.IsMercenary = MercenaryCheck.IsChecked == true; };
            ReinforcableCheck.IsCheckedChanged += (s, e) => { if (Ready()) Selected.IsReinforcable = ReinforcableCheck.IsChecked == true; };
            PrebuiltCheck.IsCheckedChanged += (s, e) => { if (Ready()) Selected.IsPrebuilt = PrebuiltCheck.IsChecked == true; };
            ClassAddButton.Click += (s, e) => AddClass();
            ClassRemoveButton.Click += (s, e) => RemoveClass();
            MissionAddButton.Click += (s, e) => AddMission();
            MissionRemoveButton.Click += (s, e) => RemoveMission();
            OkButton.Click += (s, e) => { session.Commit(); committed = true; Close(); };
            CancelButton.Click += (s, e) => Close();
            Closed += (s, e) => { if (!committed) session.Cancel(); };
            RefreshList(Editor.TeamTypes.FirstOrDefault());
        }

        private bool Ready() => !updating && Selected != null;

        private void AddTeam()
        {
            TeamType t = Editor.Add();
            if (t == null) { ErrorLabel.Text = "The teamtype limit is reached."; return; }
            ErrorLabel.Text = "";
            RefreshList(t);
        }

        private void RemoveTeam()
        {
            if (Selected == null) return;
            Editor.Remove(Selected);
            ErrorLabel.Text = "";
            RefreshList(Editor.TeamTypes.FirstOrDefault());
        }

        private void RenameTeam()
        {
            if (Selected == null) return;
            string error = Editor.TryRename(Selected, RenameBox.Text ?? "");
            ErrorLabel.Text = error ?? "";
            if (error == null) RefreshList(Selected);
        }

        private void AddClass()
        {
            if (Selected == null || ClassTypeCombo.SelectedIndex < 0) return;
            ITechnoType type = classTypes[ClassTypeCombo.SelectedIndex];
            byte count = (byte)(ClassCountNud.Value ?? 1);
            TeamTypeClass existing = Selected.Classes.FirstOrDefault(c => c.Type == type);
            if (existing != null) existing.Count = count;
            else Selected.Classes.Add(new TeamTypeClass { Type = type, Count = count });
            RefreshRows();
        }

        private void RemoveClass()
        {
            if (Selected == null || ClassList.SelectedIndex < 0 || ClassList.SelectedIndex >= Selected.Classes.Count) return;
            Selected.Classes.RemoveAt(ClassList.SelectedIndex);
            RefreshRows();
        }

        private void AddMission()
        {
            if (Selected == null || MissionCombo.SelectedIndex < 0) return;
            TeamMission mission = missionTypes[MissionCombo.SelectedIndex];
            Selected.Missions.Add(new TeamTypeMission { Mission = mission, Argument = (int)(MissionArgNud.Value ?? 0) });
            RefreshRows();
        }

        private void RemoveMission()
        {
            if (Selected == null || MissionList.SelectedIndex < 0 || MissionList.SelectedIndex >= Selected.Missions.Count) return;
            Selected.Missions.RemoveAt(MissionList.SelectedIndex);
            RefreshRows();
        }

        private void UpdateMissionHint()
        {
            TeamMission mission = MissionCombo.SelectedIndex >= 0 ? missionTypes[MissionCombo.SelectedIndex] : null;
            if (mission == null) { MissionHint.Text = ""; return; }
            string hint = "argument: " + mission.ArgType;
            if (mission.ArgType == TeamMissionArgType.OptionsList && mission.DropdownOptions.Length > 0)
            {
                hint += " — " + string.Join(", ", mission.DropdownOptions.Select(o => o.Value + "=" + o.Label));
            }
            MissionHint.Text = hint;
        }

        private void RefreshList(TeamType select)
        {
            updating = true;
            TeamList.ItemsSource = Editor.TeamTypes.Select(t => t.Name).ToList();
            updating = false;
            TeamList.SelectedIndex = select == null ? -1 : Editor.TeamTypes.ToList().IndexOf(select);
            RefreshDetail();
        }

        private void RefreshDetail()
        {
            TeamType t = Selected;
            DetailPanel.IsEnabled = t != null;
            if (t == null) return;
            updating = true;
            RenameBox.Text = t.Name;
            List<string> houses = (List<string>)HouseCombo.ItemsSource;
            HouseCombo.SelectedIndex = Math.Max(0, houses.FindIndex(h => h.Equals(t.House?.Name, StringComparison.OrdinalIgnoreCase)));
            List<string> triggerNames = (List<string>)TriggerCombo.ItemsSource;
            int trigIndex = triggerNames.FindIndex(n => n.Equals(t.Trigger ?? Trigger.None, StringComparison.OrdinalIgnoreCase));
            TriggerCombo.SelectedIndex = Math.Max(0, trigIndex);
            PriorityNud.Value = t.RecruitPriority;
            InitNumNud.Value = t.InitNum;
            MaxAllowedNud.Value = t.MaxAllowed;
            FearNud.Value = t.Fear;
            OriginNud.Value = t.Origin;
            RoundAboutCheck.IsChecked = t.IsRoundAbout;
            LearningCheck.IsChecked = t.IsLearning;
            SuicideCheck.IsChecked = t.IsSuicide;
            AutocreateCheck.IsChecked = t.IsAutocreate;
            MercenaryCheck.IsChecked = t.IsMercenary;
            ReinforcableCheck.IsChecked = t.IsReinforcable;
            PrebuiltCheck.IsChecked = t.IsPrebuilt;
            updating = false;
            RefreshRows();
        }

        private void RefreshRows()
        {
            TeamType t = Selected;
            ClassList.ItemsSource = t == null ? new List<string>() : t.Classes.Select(c => c.Type.Name + " × " + c.Count).ToList();
            MissionList.ItemsSource = t == null ? new List<string>() : t.Missions.Select(m => m.Mission.Mission + " : " + m.Argument).ToList();
        }
    }
}
