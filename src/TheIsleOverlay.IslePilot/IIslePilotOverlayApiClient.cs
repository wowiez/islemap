namespace TheIsleOverlay.IslePilot;

public interface IIslePilotOverlayApiClient
{
    Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default);

    Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default);

    Task<IslePilotOverlayMarkersDto> GetMarkersAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlayMarkersDto());

    Task<IslePilotOverlayGarageDto> GetGarageAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlayGarageDto());

    Task<IslePilotOverlayGarageCommandDto> ParkGarageDinoAsync(
        string step,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlayGarageCommandDto());

    Task<IslePilotOverlayGarageCommandDto> RestoreGarageDinoAsync(
        string dinoId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlayGarageCommandDto());

    Task<IslePilotOverlayGarageCommandStatusDto> GetGarageCommandStatusAsync(
        string commandId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlayGarageCommandStatusDto());
}
