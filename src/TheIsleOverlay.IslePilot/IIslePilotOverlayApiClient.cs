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

    Task<IslePilotOverlaySkinDraftsDto> GetSkinDraftsAsync(
        string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlaySkinDraftsDto());

    Task<IslePilotOverlaySkinDraftDto> SaveSkinDraftAsync(
        string slug, string species, string name, IslePilotOverlayGaragePaletteDto palette, bool female = true,
        int theme = 0, int pattern = 0, int variation = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlaySkinDraftDto { Species = species, Colors = palette });

    Task<IslePilotOverlaySkinApplyDto> ApplySkinPaletteAsync(
        string serverId, string species, IslePilotOverlayGaragePaletteDto palette, bool female = true,
        int theme = 0, int pattern = 0, int variation = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlaySkinApplyDto { Ok = true });

    Task<IslePilotOverlaySkinApplyDto> ApplySkinDraftAsync(
        string serverId, string species, IslePilotOverlaySkinDraftPayloadDto payload, bool female = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IslePilotOverlaySkinApplyDto { Ok = true });
}
