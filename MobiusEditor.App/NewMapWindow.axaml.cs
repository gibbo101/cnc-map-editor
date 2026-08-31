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
            OkButton.Click += (s, e) =>
            {
                if (TheaterList.SelectedItem is string theater)
                {
                    document.NewMap(theater);
                }
                Close();
            };
            CancelButton.Click += (s, e) => Close();
        }
    }
}
