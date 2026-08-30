using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.Render
{
    /// <summary>Draws the editor-only indicator layers (grid, outlines, radiuses, waypoints) over a rendered map.</summary>
    public static class IndicatorRenderer
    {
        public static void RenderIndicators(Graphics graphics, IGamePlugin plugin, Map map, double tileScale, MapLayerFlag layersToRender, MapLayerFlag manuallyHandledLayers, bool inPlacementMode, Rectangle visibleCells)
        {
            // tileScale should always be given so it results in an exact integer tile size. Math.Round was added to account for .999 situations in the floats.
            Size tileSize = new Size(Math.Max(1, (int)Math.Round(Globals.OriginalTileWidth * tileScale)), Math.Max(1, (int)Math.Round(Globals.OriginalTileHeight * tileScale)));
            // For bounds, add one more cell to get all borders showing.
            Rectangle boundRenderCells = visibleCells;
            boundRenderCells.Inflate(1, 1);
            boundRenderCells.Intersect(map.Metrics.Bounds);
            GameInfo gameInfo = plugin.GameInfo;
            // Only render these if they are not in the priority layers, and not handled manually.
            // The functions themselves will take care of checking whether they are in the active layers to render.
            if (layersToRender.HasAnyFlags(MapLayerFlag.LandTypes | MapLayerFlag.TechnoOccupancy))
            {
                CellGrid<Template> templates = layersToRender.HasFlag(MapLayerFlag.LandTypes)
                    && !manuallyHandledLayers.HasFlag(MapLayerFlag.LandTypes) ? map.Templates : null;
                bool renderTechnos = layersToRender.HasFlag(MapLayerFlag.TechnoOccupancy) && !manuallyHandledLayers.HasFlag(MapLayerFlag.TechnoOccupancy);
                OccupierSet<ICellOccupier> technos = renderTechnos ? map.Technos : null;
                OccupierSet<ICellOccupier> buildings = renderTechnos ? map.Buildings : null;
                if (templates != null || technos != null)
                {
                    MapRenderer.RenderHashAreas(graphics, plugin, templates, technos, buildings, Globals.MapTileSize, visibleCells, null, false, false);
                }
            }
            if ((Globals.ShowPlacementGrid && inPlacementMode) ||
                layersToRender.HasFlag(MapLayerFlag.MapGrid)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.MapGrid))
            {
                MapRenderer.RenderMapGrid(graphics, visibleCells, map.Bounds, layersToRender.HasFlag(MapLayerFlag.Boundaries), tileSize, Globals.MapGridColor);
            }
            if (layersToRender.HasFlag(MapLayerFlag.MapSymmetry)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.MapSymmetry))
            {
                MapRenderer.RenderMapSymmetry(graphics, map.Bounds, tileSize, Color.Cyan);
            }
            if (layersToRender.HasFlag(MapLayerFlag.Boundaries)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.Boundaries))
            {
                MapRenderer.RenderMapBoundaries(graphics, map, visibleCells, tileSize);
            }
            bool autoHandleOutlines = !manuallyHandledLayers.HasFlag(MapLayerFlag.OverlapOutlines);
            bool renderOverlay = layersToRender.HasFlag(MapLayerFlag.Overlay);
            bool renderAllCrateOutlines = layersToRender.HasFlag(MapLayerFlag.CrateOutlines);
            if (layersToRender.HasFlag(MapLayerFlag.OverlapOutlines) && autoHandleOutlines)
            {
                if (layersToRender.HasFlag(MapLayerFlag.Buildings) && gameInfo.SupportsMapLayer(MapLayerFlag.Buildings))
                {
                    MapRenderer.RenderAllBuildingOutlines(graphics, gameInfo, map, visibleCells, tileSize, tileScale, true);
                }
                if (layersToRender.HasFlag(MapLayerFlag.Terrain) && gameInfo.SupportsMapLayer(MapLayerFlag.Terrain))
                {
                    MapRenderer.RenderAllTerrainOutlines(graphics, gameInfo, map, visibleCells, tileSize, tileScale, true);
                }
                if (layersToRender.HasFlag(MapLayerFlag.Units) && gameInfo.SupportsMapLayer(MapLayerFlag.Units))
                {
                    MapRenderer.RenderAllUnitOutlines(graphics, gameInfo, map, visibleCells, tileSize, true);
                }
                if (layersToRender.HasFlag(MapLayerFlag.Infantry) && gameInfo.SupportsMapLayer(MapLayerFlag.Infantry))
                {
                    MapRenderer.RenderAllInfantryOutlines(graphics, map, visibleCells, tileSize, true);
                }
                if (renderOverlay)
                {
                    if (!renderAllCrateOutlines && map.CrateOverlaysAvailable && !Globals.CratesOnTop)
                    {
                        MapRenderer.RenderAllCrateOutlines(graphics, gameInfo, map, visibleCells, tileSize, tileScale, true);
                    }
                    MapRenderer.RenderAllSolidOverlayOutlines(graphics, gameInfo, map, visibleCells, tileSize, tileScale, true);
                }
            }
            // Special case: while it's not handled by OverlapOutlines, tools indicating that they handle the OverlapOutlines
            // manually will also paint this, so of all the outlines, it's drawn last.
            if (renderOverlay && autoHandleOutlines && renderAllCrateOutlines && map.CrateOverlaysAvailable)
            {
                MapRenderer.RenderAllCrateOutlines(graphics, gameInfo, map, visibleCells, tileSize, tileScale, false);
            }
            if (layersToRender.HasFlag(MapLayerFlag.CellTriggers)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.CellTriggers))
            {
                MapRenderer.RenderCellTriggersSoft(graphics, gameInfo, map, visibleCells, tileSize);
            }
            // This is handled quite early, so it doesn't paint over too many things. But it does paint over object and celltrigger box outlines.
            if (layersToRender.HasFlag(MapLayerFlag.Waypoints | MapLayerFlag.HomeAreaBox)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.HomeAreaBox) && plugin.Map.BasicSection.SoloMission)
            {
                MapRenderer.RenderHomeWayPointBox(graphics, plugin, map, boundRenderCells, tileSize, null, Color.Orange);
            }
            if (layersToRender.HasFlag(MapLayerFlag.Buildings | MapLayerFlag.EffectRadius)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.EffectRadius) && gameInfo.SupportsMapLayer(MapLayerFlag.EffectRadius))
            {
                MapRenderer.RenderAllBuildingEffectRadiuses(graphics, map, visibleCells, tileSize, map.GapRadius, null);
            }
            if (layersToRender.HasFlag(MapLayerFlag.Units | MapLayerFlag.EffectRadius)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.EffectRadius) && gameInfo.SupportsMapLayer(MapLayerFlag.EffectRadius))
            {
                MapRenderer.RenderAllUnitEffectRadiuses(graphics, map, visibleCells, tileSize, map.RadarJamRadius, null);
            }
            if (layersToRender.HasFlag(MapLayerFlag.Waypoints | MapLayerFlag.WaypointRadius)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.WaypointRadius))
            {
                MapRenderer.RenderAllWayPointRevealRadiuses(graphics, plugin, map, boundRenderCells, tileSize, null);
            }
            if (layersToRender.HasFlag(MapLayerFlag.Waypoints | MapLayerFlag.WaypointsIndic)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.WaypointsIndic))
            {
                MapRenderer.RenderWayPointIndicators(graphics, map, gameInfo, visibleCells, tileSize, Color.LightGreen, false, true);
            }
            if (layersToRender.HasFlag(MapLayerFlag.Buildings | MapLayerFlag.BuildingFakes)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.BuildingFakes) && gameInfo.SupportsMapLayer(MapLayerFlag.BuildingFakes))
            {
                MapRenderer.RenderAllFakeBuildingLabels(graphics, gameInfo, map, visibleCells, tileSize);
            }
            if (layersToRender.HasFlag(MapLayerFlag.Buildings | MapLayerFlag.BuildingRebuild)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.BuildingRebuild) && gameInfo.SupportsMapLayer(MapLayerFlag.BuildingRebuild))
            {
                MapRenderer.RenderAllRebuildPriorityLabels(graphics, gameInfo, map.Buildings.OfType<Building>(), visibleCells, tileSize, tileScale);
            }
            if (layersToRender.HasFlag(MapLayerFlag.TechnoTriggers)
                && !manuallyHandledLayers.HasFlag(MapLayerFlag.TechnoTriggers))
            {
                MapRenderer.RenderAllTechnoTriggers(graphics, gameInfo, map, visibleCells, tileSize, layersToRender);
            }
        }
    }
}
