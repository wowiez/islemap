namespace TheIsleOverlay.Core;

public sealed record MapTelemetry
{
    public DateTimeOffset? UpdatedAt { get; init; }
    public IReadOnlyList<MapMarkerTelemetry> Markers { get; init; } = [];
    public IReadOnlyList<MapPointOfInterestTelemetry> PointsOfInterest { get; init; } = [];
}

public sealed record MapMarkerTelemetry
{
    public string? SteamId { get; init; }
    public string? Label { get; init; }
    public bool Self { get; init; }
    public bool Group { get; init; }
    public WorldLocation? Location { get; init; }
    public MapPoint? MapLocation { get; init; }
    public double? ExactMapHeadingDegrees { get; init; }
    public IReadOnlyList<MapPoint> Path { get; init; } = [];
}

public sealed record MapPointOfInterestTelemetry
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string? Shape { get; init; }
    public double? Size { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public bool? Enabled { get; init; }
    public bool? HideLabel { get; init; }
    public IReadOnlyList<MapPoint> Points { get; init; } = [];
}
