using System.Linq;
using Avalonia.Controls;
using MobiusEditor.Model;
using MobiusEditor.Shell;

namespace MobiusEditor.App
{
    /// <summary>
    /// Edits the map settings ([Basic] + briefing). The controls hold working values; OK
    /// applies everything as one undo step through the document, Cancel discards.
    /// </summary>
    public partial class MapSettingsWindow : Window
    {
        private readonly MapDocument document;

        public MapSettingsWindow()
        {
            InitializeComponent();
        }

        public MapSettingsWindow(MapDocument document) : this()
        {
            this.document = document;
            BasicSection basic = document.Map.BasicSection;
            NameBox.Text = basic.Name;
            AuthorBox.Text = basic.Author;
            System.Collections.Generic.List<string> houses = document.Map.HouseTypes.Select(h => h.Name).ToList();
            PlayerCombo.ItemsSource = houses;
            PlayerCombo.SelectedItem = houses.FirstOrDefault(h => h.Equals(basic.Player, System.StringComparison.OrdinalIgnoreCase));
            BasePlayerCombo.ItemsSource = houses;
            BasePlayerCombo.SelectedItem = houses.FirstOrDefault(h => h.Equals(basic.BasePlayer, System.StringComparison.OrdinalIgnoreCase));
            PercentNud.Value = basic.Percent;
            SoloCheck.IsChecked = basic.SoloMission;
            ExpansionCheck.IsChecked = basic.ExpansionEnabled;
            BriefingBox.Text = document.Map.BriefingSection.Briefing;
            OkButton.Click += (s, e) =>
            {
                document.EditMapSettings(() =>
                {
                    basic.Name = NameBox.Text;
                    basic.Author = AuthorBox.Text;
                    if (PlayerCombo.SelectedItem is string player) basic.Player = player;
                    if (BasePlayerCombo.SelectedItem is string basePlayer) basic.BasePlayer = basePlayer;
                    basic.Percent = (int)(PercentNud.Value ?? basic.Percent);
                    basic.SoloMission = SoloCheck.IsChecked == true;
                    basic.ExpansionEnabled = ExpansionCheck.IsChecked == true;
                    document.Map.BriefingSection.Briefing = BriefingBox.Text;
                });
                Close();
            };
            CancelButton.Click += (s, e) => Close();
        }
    }
}
