using System.Net;
using System.Text.Json.Nodes;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Sbtc;

public sealed partial class SbtcIslandVaultClient
{
    /// <summary>
    /// Studio's Saved tab combines ordinary designs and the creator's separate
    /// saved looks. Failure in one library must not hide the other library.
    /// Purchased skins are a separate inventory and aren't exported as designs.
    /// </summary>
    public async Task<IslePilotOverlaySkinDraftsDto> GetSkinLibraryAsync(CancellationToken cancellationToken = default)
    {
        var drafts = new List<IslePilotOverlaySkinDraftDto>();
        var notes = new List<string>();
        var loaded = false;
        try
        {
            drafts.AddRange((await GetDesignsAsync(cancellationToken)).Drafts);
            loaded = true;
        }
        catch (Exception exception) when (IsLibraryReadFailure(exception, cancellationToken))
        {
            notes.Add("Chưa tải được thiết kế màu thường: " + exception.Message);
        }

        try
        {
            var library = await SendAsync<JsonObject>(HttpMethod.Get, "api/glitchcreator/designs", null, cancellationToken);
            if (Bool(library, "signed_in") == false)
                throw new SbtcIslandAuthenticationException("Phiên studio SBTC đã hết hạn. Đăng nhập Steam lại.");
            if (Bool(library, "ok") != true || library?["designs"] is not JsonArray designs)
                throw new InvalidDataException(Text(library, "message") ?? "Server chưa trả danh sách thiết kế glitch.");
            foreach (var row in designs.OfType<JsonObject>())
            {
                // Glitch looks are not ordinary HEX palettes. Keep their metadata
                // visible without clamping or converting their unbounded recipe.
                drafts.Add(new IslePilotOverlaySkinDraftDto
                {
                    Id = row["id"]?.ToString(),
                    Name = Text(row, "name") ?? "Thiết kế glitch",
                    Species = Text(row, "species"),
                    RenderMode = "glitch"
                });
            }
            loaded = true;
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            notes.Add("Kho thiết kế glitch chưa khả dụng hoặc tài khoản chưa được cấp quyền.");
        }
        catch (Exception exception) when (IsLibraryReadFailure(exception, cancellationToken))
        {
            notes.Add("Chưa tải được thiết kế glitch: " + exception.Message);
        }

        if (!loaded) throw new HttpRequestException(string.Join(" ", notes));
        return new() { Drafts = drafts, Message = notes.Count == 0 ? null : string.Join(" ", notes) };
    }

    private static bool IsLibraryReadFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
