using System.Windows;
using System.Windows.Controls;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private bool _updatingMapMarkerSettings;

    private void ApplyMapMarkerSettings(double scale, bool wildlifeAbovePlayer, bool persist)
    {
        _layoutSettings = OverlayLayoutRules.Normalize(_layoutSettings with
        {
            PlayerMarkerScale = scale,
            WildlifeAbovePlayer = wildlifeAbovePlayer
        });
        _updatingMapMarkerSettings = true;
        try
        {
            PlayerMarkerSizeTransform.ScaleX = PlayerMarkerSizeTransform.ScaleY = _layoutSettings.PlayerMarkerScale;
            PlayerMarkerScaleSlider.Value = _layoutSettings.PlayerMarkerScale * 100d;
            PlayerMarkerScaleLabel.Text = OverlayLayoutRules.FormatPlayerMarkerScale(_layoutSettings.PlayerMarkerScale);
            Panel.SetZIndex(SbtcWildlifeLayer, wildlifeAbovePlayer ? 7 : 3);
            WildlifeBelowPlayerButton.Background = BrushFrom(!wildlifeAbovePlayer ? "#3A1D514B" : "#151D1B");
            WildlifeAbovePlayerButton.Background = BrushFrom(wildlifeAbovePlayer ? "#3A1D514B" : "#151D1B");
        }
        finally { _updatingMapMarkerSettings = false; }
        _largeMapWindow?.UpdateMapMarkerSettings(_layoutSettings.PlayerMarkerScale, wildlifeAbovePlayer);
        _guideWindow?.UpdateMapMarkerSettings(_layoutSettings.PlayerMarkerScale, wildlifeAbovePlayer);
        if (persist) _layoutSettingsStore.Save(_layoutSettings);
    }

    private void PlayerMarkerScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _updatingMapMarkerSettings) return;
        ApplyMapMarkerSettings(e.NewValue / 100d, _layoutSettings.WildlifeAbovePlayer, persist: true);
    }

    private void WildlifeBelowPlayerButton_Click(object sender, RoutedEventArgs e) =>
        ApplyMapMarkerSettings(_layoutSettings.PlayerMarkerScale, wildlifeAbovePlayer: false, persist: true);

    private void WildlifeAbovePlayerButton_Click(object sender, RoutedEventArgs e) =>
        ApplyMapMarkerSettings(_layoutSettings.PlayerMarkerScale, wildlifeAbovePlayer: true, persist: true);

    private void GuideWindow_MapMarkerSettingsChanged(double scale, bool wildlifeAbovePlayer) =>
        ApplyMapMarkerSettings(scale, wildlifeAbovePlayer, persist: true);
}
