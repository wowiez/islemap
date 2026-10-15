using System.Net.Http;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.App;

/// <summary>
/// F8 garage and skin pages for an IslePilot server reached through its own website
/// (SDVN #3), on the saved website session instead of the overlay token.
/// </summary>
internal static class WebsiteGuideGarageApi
{
    public static GuideGarageApi Create(HttpClient httpClient, TelemetrySourceDefinition source, string cookie)
    {
        var client = new IslePilotWebGarageClient(
            httpClient,
            source.BaseUri,
            source.ServerSlug ?? throw new InvalidOperationException("Website garage requires a server slug."),
            cookie);
        var label = source.ShortName;
        string? skinCommandId = null;

        async Task<SbtcSkinAccess> Access(CancellationToken token)
        {
            var allowed = await client.CanSetSkinAsync(token);
            return new SbtcSkinAccess(allowed, Advanced: true, LiveSpecies: string.Empty, GenesEnabled: false, Cost: null, Balance: null,
                allowed
                    ? $"{label} · skin được gửi tới Dino đang chơi qua website {source.BaseUri.Host}."
                    : $"{label} chưa cho tài khoản này đổi skin.");
        }

        async Task<SbtcPreparedSkinApply> Prepare(string species, IslePilotOverlayGaragePaletteDto palette,
            int theme, int pattern, int variation, CancellationToken token)
        {
            var access = await Access(token);
            if (!access.Enabled) throw new InvalidOperationException(access.Message);
            // The recipe only carries the values; the website takes them unchanged, so keep
            // the editor's variation instead of SBTC's 2/8/16 rule.
            var recipe = SbtcSkinRecipe.FromPalette(palette, pattern, variation, theme, advanced: true) with { Variation = variation };
            return new SbtcPreparedSkinApply(species, recipe, access);
        }

        async Task<IslePilotOverlaySkinApplyDto> Apply(SbtcPreparedSkinApply prepared, CancellationToken token)
        {
            var recipe = prepared.Recipe;
            return Delivery(await client.ApplySkinAsync(prepared.Species, recipe.ToPalette(), prepared.Female ?? true,
                recipe.Theme ?? 0, recipe.Pattern, recipe.Variation, token));
        }

        async Task<IslePilotOverlaySkinApplyDto> ApplyDraft(string species, IslePilotOverlaySkinDraftPayloadDto draft, CancellationToken token)
        {
            var result = await client.ApplySkinDraftAsync(species, draft, cancellationToken: token);
            return Delivery(result);
        }

        IslePilotOverlaySkinApplyDto Delivery(IslePilotOverlaySkinApplyDto result)
        {
            if (!result.Accepted) return result with { Error = result.Error ?? $"{label} từ chối skin." };
            skinCommandId = result.CommandId;
            return skinCommandId is null
                ? result with { Message = result.Message ?? $"Đã gửi skin tới {label}. Vào game để kiểm tra." }
                : result with { Pending = true, Message = $"Đã gửi skin · đang chờ {label} xác nhận…" };
        }

        async Task<IslePilotOverlaySkinApplyDto> CheckDelivery(CancellationToken token)
        {
            if (skinCommandId is null)
                return new IslePilotOverlaySkinApplyDto { Ok = true, Message = "Không có lệnh skin nào đang chờ." };
            var status = await client.GetCommandStatusAsync(skinCommandId, token);
            return status.Status?.Trim().ToLowerInvariant() switch
            {
                "done" => new IslePilotOverlaySkinApplyDto { Ok = true, Message = $"{label} đã áp skin." },
                "failed" => new IslePilotOverlaySkinApplyDto { Ok = false, Error = status.Error ?? $"{label} không áp được skin." },
                _ => new IslePilotOverlaySkinApplyDto { Ok = true, Pending = true, Message = $"{label} đang xử lý skin…" }
            };
        }

        return new GuideGarageApi(
            client.GetGarageAsync,
            (_, token) => client.ParkAsync(token),
            client.RestoreAsync,
            client.GetCommandStatusAsync,
            (_, token) => client.GetSkinDraftsAsync(token),
            // Draft saving and the 3D viewer stay on the website (StudioFeatures = false hides them).
            (_, _, _, _, _, _, _, _, _) => throw new NotSupportedException($"Lưu thiết kế trên web {source.BaseUri.Host}."),
            Prepare,
            Apply,
            Access,
            CheckDelivery,
            (_, _) => throw new NotSupportedException("Model 3D chỉ có cho SBTC."),
            _ => Task.FromResult(new SbtcVaultParkTerms(false, null, null, null,
                $"Cất Dino đang chơi vào Garage {label}? Nếu server bắt chờ vài giây, hãy đứng yên trong game tới khi xong.")))
        {
            Label = label,
            StudioUri = new Uri(source.BaseUri, "skin"),
            ShowGarage = true,
            StudioFeatures = false,
            ApplyDraft = ApplyDraft
        };
    }
}
