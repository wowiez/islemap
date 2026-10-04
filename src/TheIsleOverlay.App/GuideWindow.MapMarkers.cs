using System.Windows;

namespace TheIsleOverlay.App;

public partial class GuideWindow
{
    private bool _updatingMapMarkerSettings;
    private double _playerMarkerScale = OverlayLayoutRules.DefaultPlayerMarkerScale;
    private bool _wildlifeAbovePlayer;

    public event Action<double, bool>? MapMarkerSettingsChanged;

    public void UpdateMapMarkerSettings(double scale, bool wildlifeAbovePlayer)
    {
        _playerMarkerScale = OverlayLayoutRules.NormalizePlayerMarkerScale(scale);
        _wildlifeAbovePlayer = wildlifeAbovePlayer;
        _embeddedMapWindow.UpdateMapMarkerSettings(_playerMarkerScale, wildlifeAbovePlayer);
        _updatingMapMarkerSettings = true;
        try
        {
            GuidePlayerMarkerScaleSlider.Value = _playerMarkerScale * 100d;
            GuidePlayerMarkerScaleLabel.Text = OverlayLayoutRules.FormatPlayerMarkerScale(_playerMarkerScale);
            GuideWildlifeBelowPlayerButton.Background = BrushFrom(!wildlifeAbovePlayer ? "#34363A" : "#00000000");
            GuideWildlifeAbovePlayerButton.Background = BrushFrom(wildlifeAbovePlayer ? "#34363A" : "#00000000");
        }
        finally { _updatingMapMarkerSettings = false; }
    }

    private void GuidePlayerMarkerScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _updatingMapMarkerSettings) return;
        UpdateMapMarkerSettings(e.NewValue / 100d, _wildlifeAbovePlayer);
        MapMarkerSettingsChanged?.Invoke(_playerMarkerScale, _wildlifeAbovePlayer);
    }

    private void GuideWildlifeBelowPlayerButton_Click(object sender, RoutedEventArgs e) => SetWildlifeAbovePlayer(false);
    private void GuideWildlifeAbovePlayerButton_Click(object sender, RoutedEventArgs e) => SetWildlifeAbovePlayer(true);

    private void SetWildlifeAbovePlayer(bool above)
    {
        UpdateMapMarkerSettings(_playerMarkerScale, above);
        MapMarkerSettingsChanged?.Invoke(_playerMarkerScale, above);
    }
}
