using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotMapCalibration
{
    private readonly IslePilotMapCalibrationPointDto _a;
    private readonly IslePilotMapCalibrationPointDto _b;
    public MapProjectionTelemetry Projection { get; }

    public IslePilotMapCalibration(IslePilotMapCalibrationDto calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        _a = calibration.A ?? throw new InvalidDataException("Map calibration point A is missing.");
        _b = calibration.B ?? throw new InvalidDataException("Map calibration point B is missing.");

        if (!IsFinite(_a) || !IsFinite(_b) ||
            Math.Abs(_b.WorldX - _a.WorldX) <= double.Epsilon ||
            Math.Abs(_b.WorldY - _a.WorldY) <= double.Epsilon)
        {
            throw new InvalidDataException("Map calibration is degenerate.");
        }

        Projection = new MapProjectionTelemetry(
            _a.WorldX, _a.WorldY, _a.U, _a.V,
            _b.WorldX, _b.WorldY, _b.U, _b.V);
    }

    public MapPoint Project(double x, double y)
    {
        return Projection.Project(new WorldLocation { X = x, Y = y });
    }

    public double ProjectHeading(double x, double y, double yawDegrees)
    {
        return Projection.ProjectHeading(new WorldLocation { X = x, Y = y }, yawDegrees);
    }

    private static bool IsFinite(IslePilotMapCalibrationPointDto point) =>
        double.IsFinite(point.WorldX) && double.IsFinite(point.WorldY) &&
        double.IsFinite(point.U) && double.IsFinite(point.V);
}
