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
                await System.Threading.Tasks.Task.Run(() => document.NewMap(theater, playable));
                Close();
            };
            CancelButton.Click += (s, e) => Close();
        }
    }
}
