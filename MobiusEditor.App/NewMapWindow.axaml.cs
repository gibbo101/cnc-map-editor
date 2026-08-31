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
            TheaterList.ItemsSource = document.Session.GameInfo.AllTheaters.Select(t => t.Name).ToList();
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
