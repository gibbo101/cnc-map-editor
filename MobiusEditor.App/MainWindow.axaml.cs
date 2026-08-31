using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using MobiusEditor.Headless;
using MobiusEditor.Shell;
using MobiusEditor.Utility;

namespace MobiusEditor.App
{
    public partial class MainWindow : Window
    {
        private EditorSession session;
        private MapDocument document;
        private string paletteForPath;
        private System.Drawing.Point? lastPaintCell;
        private bool painting, erasing;

        /// <summary>The open document; the window is a thin skin over it (tests reach through here).</summary>
        public MapDocument Document => document;

        public MainWindow() : this(Array.Empty<string>()) { }

        public MainWindow(string[] args)
        {
            InitializeComponent();
            OpenButton.Click += async (s, e) => await OpenAsync();
            SaveAsButton.Click += async (s, e) => await SaveAsAsync();
            ZoomInButton.Click += (s, e) => Zoom(2.0);
            ZoomOutButton.Click += (s, e) => Zoom(0.5);
            UndoButton.Click += (s, e) => document?.Undo();
            RedoButton.Click += (s, e) => document?.Redo();
            TriggersButton.Click += (s, e) => OpenTriggersDialog();
            TeamsButton.Click += (s, e) => OpenTeamsDialog();
            // One brush at a time: picking in one palette clears the other.
            TemplatePalette.SelectionChanged += (s, e) => { if (TemplatePalette.SelectedItem != null) OverlayPalette.SelectedItem = null; };
            OverlayPalette.SelectionChanged += (s, e) => { if (OverlayPalette.SelectedItem != null) TemplatePalette.SelectedItem = null; };
            MapImage.PointerPressed += OnPointerPressed;
            MapImage.PointerReleased += OnPointerReleased;
            MapImage.PointerMoved += OnPointerMoved;
            MapImage.PointerWheelChanged += OnWheel;
            Opened += (s, e) => StartSession(args);
        }

        /// <summary>Game install autodetected from Steam; mods from --mod arguments, in order.</summary>
        private void StartSession(string[] args)
        {
            try
            {
                string game = null;
                List<string> mods = new List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--game" && i + 1 < args.Length) game = args[++i];
                    else if (args[i] == "--mod" && i + 1 < args.Length) mods.Add(args[++i]);
                }
                game = game ?? SteamAssist.TryGetSteamGameFolder(MobiusEditor.Program.RemasterSteamId, "TiberianDawn.dll", "RedAlert.dll");
                if (game == null) { StatusLabel.Text = "Game install not found; start with --game <dir>."; return; }
                session = new EditorSession(game, mods);
                document = new MapDocument(session);
                document.Changed += (s, e) => Refresh();
                StatusLabel.Text = "Game: " + game + (mods.Count == 0 ? "" : "; mods: " + string.Join(", ", mods.Select(Path.GetFileName)));
                string map = args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));
                if (map != null) document.Open(map);
            }
            catch (Exception ex) { StatusLabel.Text = "Failed to start: " + ex.Message; }
        }

        private async System.Threading.Tasks.Task OpenAsync()
        {
            if (document == null) return;
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open map",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Maps") { Patterns = new[] { "*.mpr", "*.ini", "*.MPR", "*.INI" } } },
            });
            string path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            try { document.Open(path); }
            catch (Exception ex) { StatusLabel.Text = "Open failed: " + ex.Message; }
        }

        private async System.Threading.Tasks.Task SaveAsAsync()
        {
            if (document == null || !document.IsOpen) return;
            IStorageFile file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save map as", SuggestedFileName = Path.GetFileName(document.Path) });
            string path = file?.TryGetLocalPath();
            if (path == null) return;
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(document.Path), StringComparison.Ordinal)) { StatusLabel.Text = "Refusing to overwrite the open map; choose a new name."; return; }
            try { document.Save(path); StatusLabel.Text = "Saved " + path; }
            catch (Exception ex) { StatusLabel.Text = "Save failed: " + ex.Message; }
        }

        /// <summary>Opens the trigger dialog over the document's edit session; returned for the headless tests.</summary>
        public TriggersWindow OpenTriggersDialog()
        {
            if (document == null || !document.IsOpen) return null;
            TriggersWindow dialog = new TriggersWindow(document);
            dialog.Show(this);
            return dialog;
        }

        /// <summary>Opens the teamtype dialog over the document's edit session; returned for the headless tests.</summary>
        public TeamTypesWindow OpenTeamsDialog()
        {
            if (document == null || !document.IsOpen) return null;
            TeamTypesWindow dialog = new TeamTypesWindow(document);
            dialog.Show(this);
            return dialog;
        }

        private void Zoom(double factor)
        {
            if (document == null || !document.IsOpen) return;
            document.Scale *= factor;
            Refresh();
        }

        private void OnWheel(object sender, PointerWheelEventArgs e)
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            Zoom(e.Delta.Y > 0 ? 2.0 : 0.5);
            e.Handled = true;
        }

        private MobiusEditor.Model.TemplateType SelectedTemplate => TemplatePalette.SelectedItem as MobiusEditor.Model.TemplateType;
        private MobiusEditor.Model.OverlayType SelectedOverlay => OverlayPalette.SelectedItem as MobiusEditor.Model.OverlayType;

        private System.Drawing.Point? CellUnder(PointerEventArgs e)
        {
            Point p = e.GetPosition(MapImage);
            return document.CellAt((int)p.X, (int)p.Y);
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (document == null || !document.IsOpen) return;
            System.Drawing.Point? cell = CellUnder(e);
            if (cell == null) return;
            PointerPointProperties props = e.GetCurrentPoint(MapImage).Properties;
            if (props.IsLeftButtonPressed && SelectedTemplate == null && SelectedOverlay == null) return;
            if (props.IsLeftButtonPressed)
            {
                painting = true;
                document.BeginStroke();
                Paint(cell.Value);
            }
            else if (props.IsRightButtonPressed)
            {
                erasing = true;
                document.BeginStroke();
                EraseAt(cell.Value);
            }
        }

        private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!painting && !erasing) return;
            painting = erasing = false;
            lastPaintCell = null;
            document.EndStroke();
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (document == null || !document.IsOpen) return;
            System.Drawing.Point? cell = CellUnder(e);
            if (cell != null && cell != lastPaintCell)
            {
                if (painting) Paint(cell.Value);
                else if (erasing) EraseAt(cell.Value);
            }
            StatusLabel.Text = cell == null ? "" : document.Describe(cell.Value);
        }

        private void Paint(System.Drawing.Point cell)
        {
            if (SelectedOverlay != null) document.PlaceOverlay(cell, SelectedOverlay);
            else document.PlaceTemplate(cell, SelectedTemplate);
            lastPaintCell = cell;
        }

        /// <summary>Right-drag erases what the active brush would paint: the overlay's category, or the template's footprint.</summary>
        private void EraseAt(System.Drawing.Point cell)
        {
            if (SelectedOverlay != null) document.EraseOverlay(cell, SelectedOverlay);
            else document.EraseTemplate(cell, SelectedTemplate);
            lastPaintCell = cell;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (document != null && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { document.Redo(); e.Handled = true; return; }
                if (e.Key == Key.Z) { document.Undo(); e.Handled = true; return; }
                if (e.Key == Key.Y) { document.Redo(); e.Handled = true; return; }
            }
            base.OnKeyDown(e);
        }

        private void Refresh()
        {
            if (document == null || !document.IsOpen) return;
            TitleLabel.Text = document.Title;
            Title = document.Title + " — C&C Map Editor";
            ZoomLabel.Text = (document.Scale * 100).ToString("0.#") + "%";
            SaveAsButton.IsEnabled = ZoomInButton.IsEnabled = ZoomOutButton.IsEnabled = true;
            TriggersButton.IsEnabled = TeamsButton.IsEnabled = true;
            UndoButton.IsEnabled = document.CanUndo;
            RedoButton.IsEnabled = document.CanRedo;
            // Rebuild the palette only when a different map is open, or per-op refreshes would drop the selection.
            if (paletteForPath != document.Path)
            {
                TemplatePalette.ItemsSource = document.AvailableTemplates();
                OverlayPalette.ItemsSource = document.AvailableOverlays();
                paletteForPath = document.Path;
            }
            using (System.Drawing.Bitmap rendered = document.Render())
            {
                MapImage.Source = ToAvalonia(rendered);
            }
            if (document.LoadNotes.Length > 0) StatusLabel.Text = document.LoadNotes.Length + " load note(s): " + document.LoadNotes[0];
        }

        /// <summary>Copies a core bitmap (BGRA, unpremultiplied) into an Avalonia bitmap of the same layout.</summary>
        private static WriteableBitmap ToAvalonia(System.Drawing.Bitmap source)
        {
            WriteableBitmap wb = new WriteableBitmap(new PixelSize(source.Width, source.Height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);
            BitmapData data = source.LockBits(new System.Drawing.Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (ILockedFramebuffer fb = wb.Lock())
                {
                    int rowBytes = source.Width * 4;
                    byte[] row = new byte[rowBytes];
                    for (int y = 0; y < source.Height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, rowBytes);
                        Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, rowBytes);
                    }
                }
            }
            finally { source.UnlockBits(data); }
            return wb;
        }
    }
}
