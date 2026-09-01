using System.Linq;
using Avalonia.Controls;
using MobiusEditor.Shell;

namespace MobiusEditor.App
{
    /// <summary>Theater picker for a fresh empty map; Create replaces the document's open map.</summary>
    public partial class NewMapWindow : Window
    {
        private readonly MapDocument document;

        public NewMapWindow()
        {
            InitializeComponent();
        }

        public NewMapWindow(MapDocument document) : this()
        {
            this.document = document;
            // Only the theaters the Remastered game actually supports; the CnCNet extras
            // (Winter/Desert/Jungle/Barren/Cave in RA) have no art in this install.
            TheaterList.ItemsSource = document.Session.GameInfo.AllTheaters.Where(t => !t.IsModTheater).Select(t => t.Name).ToList();
            TheaterList.SelectedIndex = 0;
            // The cell grid is fixed by the format; the choice is the playable bounds.
            int maxPlayable = System.Math.Max(document.Session.GameInfo.MapSize.Width, document.Session.GameInfo.MapSize.Height) - 2;
            WidthNud.Maximum = HeightNud.Maximum = maxPlayable;
            WidthNud.Value = HeightNud.Value = maxPlayable;
            // Standard presets fill the boxes; the boxes stay editable for odd sizes.
            (string Label, int Size)[] presets =
            {
                ($"Full ({maxPlayable} × {maxPlayable})", maxPlayable),
                ("Large (96 × 96)", 96),
                ("Medium (64 × 64)", 64),
                ("Small (48 × 48)", 48),
            };
            SizePreset.ItemsSource = presets.Where(p => p.Size <= maxPlayable).Select(p => p.Label).ToList();
            SizePreset.SelectedIndex = 0;
            SizePreset.SelectionChanged += (s, e) =>
            {
                if (SizePreset.SelectedIndex >= 0)
                {
                    WidthNud.Value = HeightNud.Value = presets[SizePreset.SelectedIndex].Size;
                }
            };
            WaterStyleCombo.ItemsSource = new[] { "Lakes", "River", "Ocean", "Islands", "None" };
            WaterStyleCombo.SelectedIndex = 0;
            RandomFill.IsCheckedChanged += (s, e) =>
            {
                bool random = RandomFill.IsChecked == true;
                RandomPanel.IsVisible = random;
                EmptyMapHint.IsVisible = !random;
            };
            OkButton.Click += async (s, e) =>
            {
                if (!(TheaterList.SelectedItem is string theater))
                {
                    Close();
                    return;
                }
                // First use of a theater loads its whole tileset — seconds of work that must
                // not freeze the window. The progress lives HERE, in the window being looked
                // at — the main window's overlay would be hidden right behind this dialog.
                OkButton.IsEnabled = CancelButton.IsEnabled = TheaterList.IsEnabled = false;
                CreateProgressLabel.Text = "Creating " + theater + " map…";
                CreateProgress.IsVisible = true;
                System.Drawing.Size playable = new System.Drawing.Size((int)(WidthNud.Value ?? 126), (int)(HeightNud.Value ?? 126));
                if (RandomFill.IsChecked == true)
                {
                    MobiusEditor.Headless.MapGeneratorOptions options = new MobiusEditor.Headless.MapGeneratorOptions
                    {
                        Seed = (int)(SeedNud.Value ?? 1),
                        Players = (int)(PlayersNud.Value ?? 4),
                        Trees = TreesSlider.Value,
                        Ore = OreSlider.Value,
                        Water = WaterSlider.Value,
                        Style = System.Enum.TryParse(WaterStyleCombo.SelectedItem as string, out MobiusEditor.Headless.WaterStyle style)
                            ? style : MobiusEditor.Headless.WaterStyle.Lakes,
                        Villages = (int)(VillagesNud.Value ?? 0),
                        Tiberium = TiberiumSlider.Value,
                        Roads = RoadsCheck.IsChecked == true,
                    };
                    await System.Threading.Tasks.Task.Run(() => document.NewRandomMap(theater, playable, options));
                }
                else
                {
                    await System.Threading.Tasks.Task.Run(() => document.NewMap(theater, playable));
                }
                Close();
            };
            CancelButton.Click += (s, e) => Close();
        }
    }
}
