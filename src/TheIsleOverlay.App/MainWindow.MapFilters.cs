using System.Windows;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private void ApplyMapLayerFilters(bool showZones, bool showWildlife, bool persist)
    {
        _layoutSettings = _layoutSettings with { ShowMapZones = showZones, ShowWildlife = showWildlife };
        RefreshMapLayerVisibility();
        _largeMapWindow?.UpdateMapLayerFilters(showZones, showWildlife);
        _guideWindow?.UpdateMapLayerFilters(showZones, showWildlife);
        if (persist) _layoutSettingsStore.Save(_layoutSettings);
    }

    private void RefreshMapLayerVisibility()
    {
        SbtcZoneLayer.Visibility = _layoutSettings.ShowMapZones &&
            _sbtcZoneFeatures.Any(feature => SbtcZoneOverlay.IsFilterableZone(feature.Kind))
            ? Visibility.Visible : Visibility.Collapsed;
        SbtcZoneDecorationLayer.Visibility = SbtcZoneLayer.Visibility;
        DrinkingWaterImage.Visibility = Visibility.Visible;
        SbtcMapPoiLayer.Visibility = _sbtcZoneFeatures.Any(feature =>
            feature.Kind != SbtcZoneKind.Wildlife && !SbtcZoneOverlay.IsFilterableZone(feature.Kind))
            ? Visibility.Visible : Visibility.Collapsed;
        SbtcWildlifeLayer.Visibility = _layoutSettings.ShowWildlife &&
            _sbtcZoneFeatures.Any(feature => feature.Kind == SbtcZoneKind.Wildlife)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MapWindow_MapLayerFiltersChanged(bool showZones, bool showWildlife) =>
        ApplyMapLayerFilters(showZones, showWildlife, persist: true);
}
