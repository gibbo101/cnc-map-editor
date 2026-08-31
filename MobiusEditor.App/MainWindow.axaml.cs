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
        private object paletteForPlugin;
        private object fittedForPlugin;
        private WriteableBitmap frameBitmap;
        private System.Drawing.Point? lastPaintCell;
        private bool painting, erasing;
        private object selectedObject;
        private bool updatingProperties;
        private System.Drawing.Point? pointerSubPixel;
        private object dragObject;
        private System.Drawing.Point? dragStartCell;

        /// <summary>The open document; the window is a thin skin over it (tests reach through here).</summary>
        public MapDocument Document => document;

        public MainWindow() : this(Array.Empty<string>()) { }

        public MainWindow(string[] args)
        {
            InitializeComponent();
            NewButton.Click += (s, e) => OpenNewMapDialog();
            OpenButton.Click += async (s, e) => await OpenAsync();
            SaveAsButton.Click += async (s, e) => await SaveAsAsync();
            ZoomInButton.Click += (s, e) => Zoom(2.0);
            ZoomOutButton.Click += (s, e) => Zoom(0.5);
            UndoButton.Click += (s, e) => document?.Undo();
            RedoButton.Click += (s, e) => document?.Redo();
            SettingsButton.Click += (s, e) => OpenSettingsDialog();
            TriggersButton.Click += (s, e) => OpenTriggersDialog();
            TeamsButton.Click += (s, e) => OpenTeamsDialog();
            // One brush at a time: picking in one palette clears the others. The cell-trigger
            // and waypoint brushes also switch their indicator layer on while active.
            TemplatePalette.SelectionChanged += (s, e) => { if (TemplatePalette.SelectedItem != null) ClearOtherBrushes(TemplatePalette); };
            TerrainPalette.SelectionChanged += (s, e) => { if (TerrainPalette.SelectedItem != null) ClearOtherBrushes(TerrainPalette); };
            OverlayPalette.SelectionChanged += (s, e) => { if (OverlayPalette.SelectedItem != null) ClearOtherBrushes(OverlayPalette); };
            BuildingPalette.SelectionChanged += (s, e) => { if (BuildingPalette.SelectedItem != null) ClearOtherBrushes(BuildingPalette); };
            UnitPalette.SelectionChanged += (s, e) => { if (UnitPalette.SelectedItem != null) ClearOtherBrushes(UnitPalette); };
            InfantryPalette.SelectionChanged += (s, e) => { if (InfantryPalette.SelectedItem != null) ClearOtherBrushes(InfantryPalette); };
            SmudgePalette.SelectionChanged += (s, e) => { if (SmudgePalette.SelectedItem != null) ClearOtherBrushes(SmudgePalette); };
            HouseCombo.SelectionChanged += (s, e) =>
            {
                if (document?.Map != null && HouseCombo.SelectedItem is string houseName)
                {
                    document.PlacementHouse = document.Map.HouseTypes.First(h => h.Name == houseName);
                }
            };
            CellTriggerPalette.SelectionChanged += (s, e) => { if (CellTriggerPalette.SelectedItem != null) ClearOtherBrushes(CellTriggerPalette); UpdateIndicatorLayers(); };
            WaypointPalette.SelectionChanged += (s, e) => { if (WaypointPalette.SelectedItem != null) ClearOtherBrushes(WaypointPalette); UpdateIndicatorLayers(); };
            WireProperties();
            // Pointer handling lives on the panel, whose coordinates are map-absolute in both
            // render modes (the image itself shifts around inside it in viewport mode).
            MapPanel.PointerPressed += OnPointerPressed;
            MapPanel.PointerReleased += OnPointerReleased;
            MapPanel.PointerMoved += OnPointerMoved;
            MapPanel.PointerWheelChanged += OnWheel;
            MapPanel.PointerExited += (s, e) => GhostBorder.IsVisible = false;
            Scroller.ScrollChanged += (s, e) => { if (viewportMode && document != null && document.IsOpen) PresentViewportIfMoved(); };
            Opened += (s, e) => StartSession(args);
        }

        /// <summary>
        /// Game install autodetected from Steam; mods from --mod arguments, in order. The
        /// session load (many seconds of archive parsing) runs off the UI thread so the
        /// window paints immediately with a status line instead of sitting frozen and blank.
        /// </summary>
        private async void StartSession(string[] args)
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
                StatusLabel.Text = "Loading game data…";
                ShowBusy("Loading game data…");
                session = await System.Threading.Tasks.Task.Run(() => new EditorSession(game, mods));
                document = new MapDocument(session);
                // Edits fire Changed on the UI thread; background map opens fire it from the loader thread.
                document.Changed += (s, e) =>
                {
                    if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Refresh();
                    else Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
                };
                NewButton.IsEnabled = true;
                StatusLabel.Text = "Game: " + game + (mods.Count == 0 ? "" : "; mods: " + string.Join(", ", mods.Select(Path.GetFileName)));
                string map = args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));
                if (map != null) await OpenInBackground(map);
                else HideBusy();
            }
            catch (Exception ex) { StatusLabel.Text = "Failed to start: " + ex.Message; HideBusy(); }
        }

        /// <summary>Loads a map off the UI thread; the theater switch alone takes seconds on first use.</summary>
        private async System.Threading.Tasks.Task OpenInBackground(string path)
        {
            StatusLabel.Text = "Opening " + Path.GetFileName(path) + "…";
            ShowBusy("Opening " + Path.GetFileName(path) + "…");
            try
            {
                await System.Threading.Tasks.Task.Run(() => document.Open(path));
            }
            catch (Exception ex) { StatusLabel.Text = "Open failed: " + ex.Message; }
            finally { HideBusy(); }
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
            await OpenInBackground(path);
        }

        private async System.Threading.Tasks.Task SaveAsAsync()
        {
            if (document == null || !document.IsOpen) return;
            IStorageFile file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save map as", SuggestedFileName = Path.GetFileName(document.Path) });
            string path = file?.TryGetLocalPath();
            if (path == null) return;
            if (document.Path != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(document.Path), StringComparison.Ordinal)) { StatusLabel.Text = "Refusing to overwrite the open map; choose a new name."; return; }
            try { document.Save(path); StatusLabel.Text = "Saved " + path; }
            catch (Exception ex) { StatusLabel.Text = "Save failed: " + ex.Message; }
        }

        /// <summary>The object the properties panel is editing; set by a brushless click on the canvas.</summary>
        public object SelectedObject => selectedObject;

        /// <summary>Repopulates the properties panel from the selected object, or hides it.</summary>
        private void RefreshProperties()
        {
            if (selectedObject == null || document == null || !document.IsOpen || !document.IsObjectOnMap(selectedObject))
            {
                selectedObject = null;
                PropertiesPanel.IsVisible = false;
                return;
            }
            MobiusEditor.Shell.ObjectPropertiesPresentation p = MobiusEditor.Shell.ObjectPropertiesPresenter.For(document.Plugin, selectedObject);
            updatingProperties = true;
            try
            {
                PropertiesPanel.IsVisible = true;
                PropTitle.Text = p.Title;
                PropHouse.ItemsSource = p.Houses.ToList();
                PropHouse.SelectedItem = p.House;
                PropHouse.IsEnabled = p.HouseEnabled;
                PropStrength.Value = p.Strength;
                PropStrength.IsEnabled = p.StrengthEnabled;
                PropDirectionRow.IsVisible = p.DirectionVisible;
                PropDirection.ItemsSource = p.Directions.ToList();
                PropDirection.SelectedItem = p.Direction;
                PropDirection.IsEnabled = p.DirectionEnabled;
                PropMissionRow.IsVisible = p.MissionVisible;
                PropMission.ItemsSource = p.Missions.ToList();
                PropMission.SelectedItem = p.Mission;
                PropTrigger.ItemsSource = p.Triggers.ToList();
                PropTrigger.SelectedItem = p.Triggers.FirstOrDefault(t => t.Equals(p.Trigger ?? "None", StringComparison.OrdinalIgnoreCase)) ?? "None";
                PropTrigger.IsEnabled = p.TriggerEnabled;
                PropBuildingRow.IsVisible = p.BuildingExtrasVisible;
                PropBasePriority.Value = p.BasePriority;
                PropPrebuilt.IsChecked = p.IsPrebuilt;
                PropPrebuilt.IsEnabled = p.PrebuiltEnabled;
                PropSellable.IsVisible = PropRebuild.IsVisible = p.SellableRebuildVisible;
                PropSellable.IsChecked = p.Sellable;
                PropSellable.IsEnabled = p.IsPrebuilt;
                PropRebuild.IsChecked = p.Rebuild;
            }
            finally
            {
                updatingProperties = false;
            }
        }

        /// <summary>Applies one property change as one undo step, then re-presents (enabled states shift).</summary>
        private void ApplyProperty(Action<object> apply)
        {
            if (updatingProperties || selectedObject == null) return;
            object target = selectedObject;
            document.EditObjectProperties(target, () => apply(target));
            RefreshProperties();
        }

        private void WireProperties()
        {
            PropHouse.SelectionChanged += (s, e) =>
            {
                if (updatingProperties || !(PropHouse.SelectedItem is string houseName)) return;
                MobiusEditor.Model.HouseType house = document.Map.HouseTypes.FirstOrDefault(h => h.Name == houseName);
                if (house == null) return;
                ApplyProperty(o =>
                {
                    switch (o)
                    {
                        case MobiusEditor.Model.Building b: b.House = house; break;
                        case MobiusEditor.Model.Unit u: u.House = house; break;
                        case MobiusEditor.Model.Infantry i: i.House = house; break;
                    }
                });
            };
            PropStrength.ValueChanged += (s, e) =>
            {
                if (updatingProperties) return;
                int strength = (int)(PropStrength.Value ?? 256);
                ApplyProperty(o =>
                {
                    switch (o)
                    {
                        case MobiusEditor.Model.Building b: b.Strength = strength; break;
                        case MobiusEditor.Model.Unit u: u.Strength = strength; break;
                        case MobiusEditor.Model.Infantry i: i.Strength = strength; break;
                    }
                });
            };
            PropDirection.SelectionChanged += (s, e) =>
            {
                if (updatingProperties || !(PropDirection.SelectedItem is string directionName)) return;
                ApplyProperty(o =>
                {
                    switch (o)
                    {
                        case MobiusEditor.Model.Building b:
                            b.Direction = document.Map.BuildingDirectionTypes.FirstOrDefault(d => d.Name == directionName) ?? b.Direction;
                            break;
                        case MobiusEditor.Model.Unit u:
                            u.Direction = document.Map.UnitDirectionTypes.FirstOrDefault(d => d.Name == directionName) ?? u.Direction;
                            break;
                        case MobiusEditor.Model.Infantry i:
                            i.Direction = document.Map.UnitDirectionTypes.FirstOrDefault(d => d.Name == directionName) ?? i.Direction;
                            break;
                    }
                });
            };
            PropMission.SelectionChanged += (s, e) =>
            {
                if (updatingProperties || !(PropMission.SelectedItem is string mission)) return;
                ApplyProperty(o =>
                {
                    switch (o)
                    {
                        case MobiusEditor.Model.Unit u: u.Mission = mission; break;
                        case MobiusEditor.Model.Infantry i: i.Mission = mission; break;
                    }
                });
            };
            PropTrigger.SelectionChanged += (s, e) =>
            {
                if (updatingProperties || !(PropTrigger.SelectedItem is string trigger)) return;
                ApplyProperty(o =>
                {
                    switch (o)
                    {
                        case MobiusEditor.Model.Building b: b.Trigger = trigger; break;
                        case MobiusEditor.Model.Unit u: u.Trigger = trigger; break;
                        case MobiusEditor.Model.Infantry i: i.Trigger = trigger; break;
                    }
                });
            };
            PropBasePriority.ValueChanged += (s, e) =>
            {
                if (updatingProperties) return;
                int priority = (int)(PropBasePriority.Value ?? -1);
                ApplyProperty(o => { if (o is MobiusEditor.Model.Building b) b.BasePriority = priority; });
            };
            PropPrebuilt.IsCheckedChanged += (s, e) =>
            {
                if (updatingProperties) return;
                bool prebuilt = PropPrebuilt.IsChecked == true;
                ApplyProperty(o => { if (o is MobiusEditor.Model.Building b) b.IsPrebuilt = prebuilt; });
            };
            PropSellable.IsCheckedChanged += (s, e) =>
            {
                if (updatingProperties) return;
                bool sellable = PropSellable.IsChecked == true;
                ApplyProperty(o => { if (o is MobiusEditor.Model.Building b) b.Sellable = sellable; });
            };
            PropRebuild.IsCheckedChanged += (s, e) =>
            {
                if (updatingProperties) return;
                bool rebuild = PropRebuild.IsChecked == true;
                ApplyProperty(o => { if (o is MobiusEditor.Model.Building b) b.Rebuild = rebuild; });
            };
        }

        /// <summary>Opens the map settings dialog; returned for the headless tests.</summary>
        public MapSettingsWindow OpenSettingsDialog()
        {
            if (document == null || !document.IsOpen) return null;
            MapSettingsWindow dialog = new MapSettingsWindow(document);
            dialog.Show(this);
            return dialog;
        }

        /// <summary>Opens the new-map theater picker; returned for the headless tests.</summary>
        public NewMapWindow OpenNewMapDialog()
        {
            if (document == null) return null;
            NewMapWindow dialog = new NewMapWindow(document);
            dialog.Show(this);
            return dialog;
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

        /// <summary>Zooms about the viewport center instead of the map corner.</summary>
        private void Zoom(double factor)
        {
            if (document == null || !document.IsOpen) return;
            double fx = MapPanel.Width > 0 ? (Scroller.Offset.X + Scroller.Viewport.Width / 2) / MapPanel.Width : 0.5;
            double fy = MapPanel.Height > 0 ? (Scroller.Offset.Y + Scroller.Viewport.Height / 2) / MapPanel.Height : 0.5;
            document.Scale *= factor;
            Refresh();
            double nx = fx * MapPanel.Width - Scroller.Viewport.Width / 2;
            double ny = fy * MapPanel.Height - Scroller.Viewport.Height / 2;
            // The scroll extent follows the panel size on the next layout pass; set the offset after it.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => Scroller.Offset = new Vector(Math.Max(0, nx), Math.Max(0, ny)),
                Avalonia.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>The busy overlay: an indeterminate spinner over the map area during long loads.</summary>
        public void ShowBusy(string text)
        {
            BusyLabel.Text = text;
            BusyOverlay.IsVisible = true;
        }

        public void HideBusy()
        {
            BusyOverlay.IsVisible = false;
        }

        private void OnWheel(object sender, PointerWheelEventArgs e)
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            Zoom(e.Delta.Y > 0 ? 2.0 : 0.5);
            e.Handled = true;
        }

        private MobiusEditor.Model.TemplateType SelectedTemplate => (TemplatePalette.SelectedItem as PaletteEntry)?.Type as MobiusEditor.Model.TemplateType;
        private MobiusEditor.Model.OverlayType SelectedOverlay => (OverlayPalette.SelectedItem as PaletteEntry)?.Type as MobiusEditor.Model.OverlayType;
        private string SelectedCellTrigger => CellTriggerPalette.SelectedItem as string;
        private int SelectedWaypoint => WaypointPalette.SelectedIndex;
        private MobiusEditor.Model.TerrainType SelectedTerrain => (TerrainPalette.SelectedItem as PaletteEntry)?.Type as MobiusEditor.Model.TerrainType;
        private MobiusEditor.Model.BuildingType SelectedBuilding => (BuildingPalette.SelectedItem as PaletteEntry)?.Type as MobiusEditor.Model.BuildingType;
        private MobiusEditor.Model.UnitType SelectedUnit => (UnitPalette.SelectedItem as PaletteEntry)?.Type as MobiusEditor.Model.UnitType;
        private MobiusEditor.Model.InfantryType SelectedInfantry => (InfantryPalette.SelectedItem as PaletteEntry)?.Type as MobiusEditor.Model.InfantryType;
        private MobiusEditor.Model.SmudgeType SelectedSmudge => (SmudgePalette.SelectedItem as PaletteEntry)?.Type as MobiusEditor.Model.SmudgeType;

        /// <summary>The active brush's palette entry, whichever palette holds it.</summary>
        private PaletteEntry ActiveEntry() =>
            (TemplatePalette.SelectedItem ?? TerrainPalette.SelectedItem ?? OverlayPalette.SelectedItem
             ?? BuildingPalette.SelectedItem ?? UnitPalette.SelectedItem ?? InfantryPalette.SelectedItem
             ?? SmudgePalette.SelectedItem) as PaletteEntry;

        /// <summary>
        /// The placement ghost: the selected type's thumbnail, footprint-sized, snapped to the
        /// hovered cell — so it is clear what is being placed and where before clicking. The
        /// cell-trigger and waypoint brushes show the plain highlight box.
        /// </summary>
        private void UpdateGhost(System.Drawing.Point? cell)
        {
            PaletteEntry entry = ActiveEntry();
            bool boxOnly = entry == null && (SelectedCellTrigger != null || SelectedWaypoint >= 0);
            if (document == null || !document.IsOpen || cell == null || (entry == null && !boxOnly))
            {
                GhostBorder.IsVisible = false;
                return;
            }
            System.Drawing.Size tile = document.TileSize;
            System.Drawing.Size footprint = entry?.Item.FootprintCells ?? new System.Drawing.Size(1, 1);
            GhostBorder.Margin = new Thickness(cell.Value.X * tile.Width, cell.Value.Y * tile.Height, 0, 0);
            GhostBorder.Width = footprint.Width * tile.Width;
            GhostBorder.Height = footprint.Height * tile.Height;
            GhostImage.Source = entry?.Image;
            GhostBorder.IsVisible = true;
        }

        private void ClearOtherBrushes(ListBox active)
        {
            foreach (ListBox palette in new[] { TemplatePalette, TerrainPalette, OverlayPalette, BuildingPalette, UnitPalette, InfantryPalette, SmudgePalette, CellTriggerPalette, WaypointPalette })
            {
                if (!ReferenceEquals(palette, active)) palette.SelectedItem = null;
            }
        }

        /// <summary>The cell-trigger and waypoint brushes show their indicator layers while active.</summary>
        private void UpdateIndicatorLayers()
        {
            if (document == null || !document.IsOpen) return;
            MobiusEditor.Model.MapLayerFlag layers = MobiusEditor.Model.MapLayerFlag.MapLayers;
            if (SelectedCellTrigger != null) layers |= MobiusEditor.Model.MapLayerFlag.CellTriggers;
            if (SelectedWaypoint >= 0) layers |= MobiusEditor.Model.MapLayerFlag.WaypointsIndic;
            if (document.Layers != layers)
            {
                document.Layers = layers;
                Refresh();
            }
        }

        private System.Drawing.Point? CellUnder(PointerEventArgs e)
        {
            Point p = e.GetPosition(MapPanel);
            return document.CellAt((int)p.X, (int)p.Y);
        }

        private System.Drawing.Point? SubPixelUnder(PointerEventArgs e)
        {
            Point p = e.GetPosition(MapPanel);
            return document.SubPixelAt((int)p.X, (int)p.Y);
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (document == null || !document.IsOpen) return;
            System.Drawing.Point? cell = CellUnder(e);
            if (cell == null) return;
            PointerPointProperties props = e.GetCurrentPoint(MapImage).Properties;
            if (props.IsLeftButtonPressed && SelectedTemplate == null && SelectedOverlay == null && SelectedCellTrigger == null && SelectedWaypoint < 0
                && SelectedTerrain == null && SelectedUnit == null && SelectedInfantry == null && SelectedBuilding == null && SelectedSmudge == null)
            {
                // No brush: a left click selects the object under the cell for the properties
                // panel, and holding it starts a drag-move released on the target cell.
                selectedObject = document.ObjectAt(cell.Value, SubPixelUnder(e));
                dragObject = selectedObject;
                dragStartCell = cell;
                RefreshProperties();
                return;
            }
            pointerSubPixel = SubPixelUnder(e);
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
            if (dragObject != null)
            {
                System.Drawing.Point? cell = CellUnder(e);
                if (cell != null && cell != dragStartCell)
                {
                    if (!document.MoveObject(dragObject, cell.Value, SubPixelUnder(e)))
                    {
                        StatusLabel.Text = "Can't move there.";
                    }
                    RefreshProperties();
                }
                dragObject = null;
                dragStartCell = null;
                return;
            }
            if (!painting && !erasing) return;
            painting = erasing = false;
            lastPaintCell = null;
            document.EndStroke();
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (document == null || !document.IsOpen) return;
            System.Drawing.Point? cell = CellUnder(e);
            if (cell != null && cell != lastPaintCell && SelectedWaypoint < 0)
            {
                pointerSubPixel = SubPixelUnder(e);
                if (painting) Paint(cell.Value);
                else if (erasing) EraseAt(cell.Value);
            }
            UpdateGhost(cell);
            StatusLabel.Text = cell == null ? "" : document.Describe(cell.Value);
        }

        private void Paint(System.Drawing.Point cell)
        {
            if (SelectedOverlay != null) document.PlaceOverlay(cell, SelectedOverlay);
            else if (SelectedTerrain != null) document.PlaceTerrain(cell, SelectedTerrain);
            else if (SelectedBuilding != null) document.PlaceBuilding(cell, SelectedBuilding);
            else if (SelectedUnit != null) document.PlaceUnit(cell, SelectedUnit);
            else if (SelectedInfantry != null) document.PlaceInfantry(cell, SelectedInfantry, pointerSubPixel);
            else if (SelectedSmudge != null) document.PlaceSmudge(cell, SelectedSmudge);
            else if (SelectedCellTrigger != null) document.PlaceCellTrigger(cell, SelectedCellTrigger);
            else if (SelectedWaypoint >= 0) document.PlaceWaypoint(SelectedWaypoint, cell);
            else document.PlaceTemplate(cell, SelectedTemplate);
            lastPaintCell = cell;
        }

        /// <summary>Right-drag erases what the active brush would paint on that cell.</summary>
        private void EraseAt(System.Drawing.Point cell)
        {
            if (SelectedOverlay != null) document.EraseOverlay(cell, SelectedOverlay);
            else if (SelectedTerrain != null) document.EraseTerrainAt(cell);
            else if (SelectedBuilding != null) document.EraseBuildingAt(cell);
            else if (SelectedUnit != null) document.EraseUnitAt(cell);
            else if (SelectedInfantry != null) document.EraseInfantryAt(cell, pointerSubPixel);
            else if (SelectedSmudge != null) document.EraseSmudge(cell, SelectedSmudge);
            else if (SelectedCellTrigger != null) document.EraseCellTrigger(cell);
            else if (SelectedWaypoint >= 0) document.EraseWaypointAt(cell);
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

        /// <summary>
        /// A freshly opened map starts fitted to the viewport — on a small screen that means a
        /// far smaller render surface, which is most of the difference between smooth and
        /// laggy. Returns false while the viewport is not laid out yet (a retry is queued),
        /// so the first expensive render never happens at the wrong scale.
        /// </summary>
        private bool FitToViewport()
        {
            if (ReferenceEquals(fittedForPlugin, document.Plugin)) return true;
            double vw = Scroller.Bounds.Width, vh = Scroller.Bounds.Height;
            if (vw < 50 || vh < 50)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(Refresh, Avalonia.Threading.DispatcherPriority.Background);
                return false;
            }
            fittedForPlugin = document.Plugin;
            double mapW = document.Map.Metrics.Width * 128.0, mapH = document.Map.Metrics.Height * 128.0;
            double[] steps = { 1, 0.5, 0.25, 0.125, 1.0 / 16 };
            double fit = steps.FirstOrDefault(s => mapW * s <= vw && mapH * s <= vh);
            if (fit == 0) fit = 1.0 / 16;
            if (document.Scale > fit) document.Scale = fit;
            return true;
        }

        private void Refresh()
        {
            if (document == null || !document.IsOpen) return;
            if (!FitToViewport()) return;
            TitleLabel.Text = document.Title;
            Title = document.Title + " — C&C Map Editor";
            ZoomLabel.Text = (document.Scale * 100).ToString("0.#") + "%";
            SaveAsButton.IsEnabled = ZoomInButton.IsEnabled = ZoomOutButton.IsEnabled = true;
            TriggersButton.IsEnabled = TeamsButton.IsEnabled = SettingsButton.IsEnabled = true;
            UndoButton.IsEnabled = document.CanUndo;
            RedoButton.IsEnabled = document.CanRedo;
            // Rebuild the palettes only when a different map is open, or per-op refreshes would drop the selection.
            if (!ReferenceEquals(paletteForPlugin, document.Plugin))
            {
                TemplatePalette.ItemsSource = document.AvailableTemplates().Select(t => new PaletteEntry(MobiusEditor.Shell.PaletteItem.From(t))).ToList();
                TerrainPalette.ItemsSource = document.AvailableTerrain().Select(t => new PaletteEntry(MobiusEditor.Shell.PaletteItem.From(t))).ToList();
                OverlayPalette.ItemsSource = document.AvailableOverlays().Select(t => new PaletteEntry(MobiusEditor.Shell.PaletteItem.From(t))).ToList();
                BuildingPalette.ItemsSource = document.AvailableBuildings().Select(t => new PaletteEntry(MobiusEditor.Shell.PaletteItem.From(t))).ToList();
                UnitPalette.ItemsSource = document.AvailableUnits().Select(t => new PaletteEntry(MobiusEditor.Shell.PaletteItem.From(t))).ToList();
                InfantryPalette.ItemsSource = document.AvailableInfantry().Select(t => new PaletteEntry(MobiusEditor.Shell.PaletteItem.From(t))).ToList();
                SmudgePalette.ItemsSource = document.AvailableSmudge().Select(t => new PaletteEntry(MobiusEditor.Shell.PaletteItem.From(t))).ToList();
                WaypointPalette.ItemsSource = document.Map.Waypoints.Select((w, i) => i + ": " + w.Name).ToList();
                HouseCombo.ItemsSource = document.Map.HouseTypes.Select(h => h.Name).ToList();
                HouseCombo.SelectedIndex = 0;
                HouseCombo.IsEnabled = true;
                paletteForPlugin = document.Plugin;
            }
            // The eligible cell triggers follow the trigger list; rebuild only when it actually changed.
            List<string> cellTriggers = document.AvailableCellTriggers().ToList();
            if (!(CellTriggerPalette.ItemsSource is List<string> current) || !current.SequenceEqual(cellTriggers))
            {
                string selected = SelectedCellTrigger;
                CellTriggerPalette.ItemsSource = cellTriggers;
                if (selected != null) CellTriggerPalette.SelectedItem = cellTriggers.FirstOrDefault(n => n.Equals(selected, StringComparison.OrdinalIgnoreCase));
            }
            PresentFrame();
            // Zoom or map changes leave the ghost's position stale; it returns on the next pointer move.
            GhostBorder.IsVisible = false;
            // Undo can retire the selected object; drop the panel when it does.
            if (selectedObject != null && !document.IsObjectOnMap(selectedObject)) RefreshProperties();
            if (document.LoadNotes.Length > 0) StatusLabel.Text = document.LoadNotes.Length + " load note(s): " + document.LoadNotes[0];
        }

        /// <summary>Above this many pixels the whole-map surface would risk gigabyte bitmaps (a 128-cell map at 100% is 16384², 1 GB — the Deck crash), so the viewport path takes over.</summary>
        private const long FullSurfacePixelBudget = 4096L * 4096;
        private bool viewportMode;
        private System.Drawing.Rectangle viewportBlock;

        /// <summary>
        /// Presents the map: at modest zooms the cached whole-map surface with dirty-region
        /// copies; past the surface budget, only the block of cells around the viewport is
        /// rendered and positioned inside the full-size panel, so zooming to 100% costs a
        /// screenful, not gigabytes.
        /// </summary>
        private void PresentFrame()
        {
            System.Drawing.Size tile = document.TileSize;
            double mapPixelWidth = document.Map.Metrics.Width * (double)tile.Width;
            double mapPixelHeight = document.Map.Metrics.Height * (double)tile.Height;
            // The panel carries the full map size so the scrollbars are right in both modes.
            MapPanel.Width = mapPixelWidth;
            MapPanel.Height = mapPixelHeight;
            if ((long)mapPixelWidth * (long)mapPixelHeight <= FullSurfacePixelBudget)
            {
                if (viewportMode)
                {
                    viewportMode = false;
                    viewportBlock = System.Drawing.Rectangle.Empty;
                    MapImage.Margin = new Thickness(0);
                }
                PresentFull();
            }
            else
            {
                viewportMode = true;
                PresentViewport();
            }
        }

        private void PresentFull()
        {
            System.Drawing.Bitmap cache = document.UpdateRenderCache(out System.Drawing.Rectangle dirty);
            if (EnsureFrame(cache.Width, cache.Height))
            {
                dirty = new System.Drawing.Rectangle(0, 0, cache.Width, cache.Height);
            }
            if (dirty.Width <= 0 || dirty.Height <= 0)
            {
                return;
            }
            CopyToFrame(cache, dirty, dirty);
            MapImage.InvalidateVisual();
        }

        /// <summary>The block of cells the viewport needs, with a margin so small scrolls stay free.</summary>
        private System.Drawing.Rectangle NeededCells(int margin)
        {
            System.Drawing.Size tile = document.TileSize;
            int x0 = (int)(Scroller.Offset.X / tile.Width) - margin;
            int y0 = (int)(Scroller.Offset.Y / tile.Height) - margin;
            int x1 = (int)((Scroller.Offset.X + Scroller.Viewport.Width) / tile.Width) + margin + 1;
            int y1 = (int)((Scroller.Offset.Y + Scroller.Viewport.Height) / tile.Height) + margin + 1;
            System.Drawing.Rectangle cells = System.Drawing.Rectangle.FromLTRB(x0, y0, x1, y1);
            cells.Intersect(new System.Drawing.Rectangle(0, 0, document.Map.Metrics.Width, document.Map.Metrics.Height));
            return cells;
        }

        private void PresentViewport()
        {
            viewportBlock = NeededCells(3);
            RenderViewportBlock();
        }

        /// <summary>On scroll, re-render only when the viewport leaves the current block.</summary>
        private void PresentViewportIfMoved()
        {
            System.Drawing.Rectangle needed = NeededCells(1);
            if (!viewportBlock.Contains(needed))
            {
                viewportBlock = NeededCells(3);
                RenderViewportBlock();
            }
        }

        private void RenderViewportBlock()
        {
            if (viewportBlock.Width <= 0 || viewportBlock.Height <= 0) return;
            System.Drawing.Size tile = document.TileSize;
            using (System.Drawing.Bitmap block = document.RenderBlock(viewportBlock))
            {
                EnsureFrame(block.Width, block.Height);
                System.Drawing.Rectangle all = new System.Drawing.Rectangle(0, 0, block.Width, block.Height);
                CopyToFrame(block, all, all);
            }
            MapImage.Margin = new Thickness(viewportBlock.X * tile.Width, viewportBlock.Y * tile.Height, 0, 0);
            MapImage.InvalidateVisual();
        }

        /// <summary>Reuses the frame bitmap, recreating on size change; true when recreated.</summary>
        private bool EnsureFrame(int width, int height)
        {
            if (frameBitmap != null && frameBitmap.PixelSize.Width == width && frameBitmap.PixelSize.Height == height)
            {
                return false;
            }
            WriteableBitmap old = frameBitmap;
            frameBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);
            MapImage.Source = frameBitmap;
            old?.Dispose();
            return true;
        }

        private void CopyToFrame(System.Drawing.Bitmap source, System.Drawing.Rectangle srcRect, System.Drawing.Rectangle dstRect)
        {
            BitmapData data = source.LockBits(new System.Drawing.Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (ILockedFramebuffer fb = frameBitmap.Lock())
                {
                    int rowBytes = srcRect.Width * 4;
                    byte[] row = new byte[rowBytes];
                    for (int y = 0; y < srcRect.Height; y++)
                    {
                        Marshal.Copy(data.Scan0 + (srcRect.Y + y) * data.Stride + srcRect.X * 4, row, 0, rowBytes);
                        Marshal.Copy(row, 0, fb.Address + (dstRect.Y + y) * fb.RowBytes + dstRect.X * 4, rowBytes);
                    }
                }
            }
            finally { source.UnlockBits(data); }
            if (!ReferenceEquals(MapImage.Source, frameBitmap)) MapImage.Source = frameBitmap;
        }
    }
}
