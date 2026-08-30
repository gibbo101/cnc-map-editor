using System;
using System.Drawing;

namespace MobiusEditor.Properties
{
    /// <summary>Editor settings with the same names and defaults as the WinForms settings file; persistence is the shell's job.</summary>
    public sealed class Settings
    {
        public static Settings Default { get; } = new Settings();
        public void Save() { }
        public void Reload() { }

        public string EnabledGames { get; set; } = @"TD,RA,SS";
        public bool UseClassicFiles { get; set; } = false;
        public string ClassicPathTD { get; set; } = @"Classic\TD";
        public string ClassicPathRA { get; set; } = @"Classic\RA";
        public string ClassicPathSS { get; set; } = @"Classic\TD";
        public bool ClassicNoRemasterLogic { get; set; } = true;
        public bool ClassicProducesNoMetaFiles { get; set; } = true;
        public bool ClassicEncodesNameAsCp437 { get; set; } = true;
        public bool LazyInitSteam { get; set; } = true;
        public string EditorLanguage { get; set; } = @"Auto";
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public bool EnableDpiAwareness { get; set; } = false;
        public string ModsToLoadTD { get; set; } = @"2844969675;GraphicsFixesTD";
        public string ModsToLoadRA { get; set; } = @"2978875641;GraphicsFixesRA";
        public string ModsToLoadSS { get; set; } = @"2844969675;GraphicsFixesTD";
        public string MixContentInfoFile { get; set; } = @"Data\mixcontent.ini";
        public bool DefaultBoundsObstructFill { get; set; } = true;
        public bool DefaultTileDragProtect { get; set; } = true;
        public bool DefaultTileDragRandomize { get; set; } = true;
        public bool DefaultShowPlacementGrid { get; set; } = true;
        public bool DefaultCratesOnTop { get; set; } = false;
        public bool DefaultOutlineAllCrates { get; set; } = false;
        public double DefaultExportScale { get; set; } = -0.5d;
        public double DefaultExportScaleClassic { get; set; } = 1d;
        public bool DefaultExportMultiInBounds { get; set; } = true;
        public bool ZoomToBoundsOnLoad { get; set; } = true;
        public bool RememberToolData { get; set; } = false;
        public bool AllowDeleteRoutePoints { get; set; } = false;
        public double MapScale { get; set; } = 0.5d;
        public double MapScaleClassic { get; set; } = 1d;
        public double PreviewScale { get; set; } = 1d;
        public double PreviewScaleClassic { get; set; } = 5.333d;
        public double ObjectToolItemSizeMultiplier { get; set; } = 0.3d;
        public double TemplateToolTextureSizeMultiplier { get; set; } = 0.5d;
        public int MaxMapTileTextureSize { get; set; } = 0;
        public int UndoRedoStackSize { get; set; } = 100;
        public Size MinimumClampSize { get; set; } = new Size(30, 30);
        public Color MapBackColor { get; set; } = Color.FromArgb(32, 32, 32);
        public Color MapGridColor { get; set; } = Color.FromArgb(40, 0, 0, 255);
        public Color HashColorLandClear { get; set; } = Color.FromArgb(0, 255, 255, 255);
        public Color HashColorLandBeach { get; set; } = Color.FromArgb(255, 255, 85);
        public Color HashColorLandRock { get; set; } = Color.FromArgb(170, 0, 0);
        public Color HashColorLandRoad { get; set; } = Color.FromArgb(186, 128, 40);
        public Color HashColorLandWater { get; set; } = Color.FromArgb(186, 186, 255);
        public Color HashColorLandRiver { get; set; } = Color.FromArgb(0, 0, 255);
        public Color HashColorLandRough { get; set; } = Color.FromArgb(186, 128, 186);
        public Color HashColorTechnoPart { get; set; } = Color.FromArgb(0, 255, 0);
        public Color HashColorTechnoFull { get; set; } = Color.FromArgb(0, 170, 0);
        public Color OutlineColorCrateWood { get; set; } = Color.FromArgb(255, 192, 64);
        public Color OutlineColorCrateSteel { get; set; } = Color.FromArgb(255, 255, 255);
        public Color OutlineColorTerrain { get; set; } = Color.FromArgb(0, 192, 0);
        public Color OutlineColorSolidOverlay { get; set; } = Color.FromArgb(192, 192, 192);
        public Color OutlineColorWall { get; set; } = Color.FromArgb(0, 192, 0, 192);
        public bool IgnoreShadowOverlap { get; set; } = true;
        public float PreviewAlpha { get; set; } = 0.5f;
        public float UnbuiltAlpha { get; set; } = 0.6f;
        public bool ReportMissionDetection { get; set; } = true;
        public bool EnforceObjectMaximums { get; set; } = true;
        public bool EnforceTriggerTypes { get; set; } = true;
        public bool ConvertRaObsoleteClear { get; set; } = true;
        public bool BlockingBibs { get; set; } = true;
        public bool DisableAirUnits { get; set; } = true;
        public bool ConvertCraters { get; set; } = true;
        public bool DisableSquishMark { get; set; } = true;
        public bool FilterTheaterObjects { get; set; } = true;
        public bool WriteClassicBriefing { get; set; } = true;
        public bool WriteRemasterBriefing { get; set; } = true;
        public bool ApplyHarvestBug { get; set; } = true;
        public bool OverlayWallsOnly { get; set; } = true;
        public bool NoOwnedObjectsInSole { get; set; } = true;
        public bool ExpandTdScripting { get; set; } = false;
        public bool EnableTd106LineBreaks { get; set; } = true;
        public bool FixClassicEinstein { get; set; } = true;
        public bool AllowImageModsInMaps { get; set; } = true;
        public bool TdHelisSpawnOnGround { get; set; } = false;
        public bool RaHelisSpawnOnGround { get; set; } = false;
        public bool FixConcretePavement { get; set; } = false;
        public bool DrawSoleTeleports { get; set; } = true;
        public bool ShowInviteWarning { get; set; } = true;
        public string GameDirectoryPath { get; set; } = @"";
        public Point CellTriggersToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point ResourcesToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point TemplateToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point TerrainToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point WaypointsToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point SmudgeToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point InfantryToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point UnitToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point OverlayToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point BuildingToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public Point WallsToolDialogDefaultPosition { get; set; } = new Point(0, 0);
        public string ApplicationVersion { get; set; } = @"";
        public string LastCheckVersion { get; set; } = @"";
    }
}
