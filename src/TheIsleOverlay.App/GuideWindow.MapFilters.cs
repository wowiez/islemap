namespace TheIsleOverlay.App;

public partial class GuideWindow
{
    public event Action<bool, bool>? MapLayerFiltersChanged;

    public void UpdateMapLayerFilters(bool showZones, bool showWildlife) =>
        _embeddedMapWindow.UpdateMapLayerFilters(showZones, showWildlife);
}
