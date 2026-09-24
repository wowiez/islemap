using System.IO;
using System.Net.Http;
using System.Windows;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private readonly IslePilotCredentialStore _islePilotCredentialStore = new(
        AppPaths.IslePilotCredential);
    private TelemetrySourceDefinition? _islePilotSelectedSource;
    private bool _hostedCredentialsImported;
    private IslePilotOverlayAuthResult? _islePilotCredentials;
    private bool _islePilotConnecting;
    private bool _updatingSteamAccountSelector;

    private async void SteamLoginPanel_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ReloadSteamAccountsAsync();
            if (_islePilotCredentials is { } credentials &&
                string.IsNullOrWhiteSpace(credentials.PersonaName))
            {
                var validation = await ValidateIslePilotCredentialsAsync(
                    credentials,
                    _islePilotSelectedSource?.BaseUri);
                if (validation.State == IslePilotOverlayAuthValidationState.Valid)
                {
                    await _islePilotCredentialStore.SaveAsync(validation.Credentials, _shutdown.Token);
                    await ReloadSteamAccountsAsync();
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không đọc được phiên Steam đã lưu: {FriendlyError(exception)}";
            ApplySteamLoginState(null);
        }
    }

    private async void SteamLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_islePilotConnecting || _connecting)
        {
            return;
        }

        _islePilotConnecting = true;
        SetSteamLoginControlsEnabled(false);
        try
        {
            if (_islePilotSelectedSource is { } hostedSource)
            {
                // The selected account belongs to a server with its own IslePilot
                // host, so reconnect through that host instead of the network.
                await ConnectHostedIslePilotAsync(hostedSource);
                return;
            }

            var credentials = _islePilotCredentials
                ?? await _islePilotCredentialStore.LoadAsync(_shutdown.Token);
            if (credentials is not null)
            {
                SourceStatusLabel.Text = "ĐANG XÁC MINH PHIÊN ISLEPILOT…";
                var savedValidation = await ValidateIslePilotCredentialsAsync(credentials);
                if (savedValidation.State == IslePilotOverlayAuthValidationState.Invalid)
                {
                    credentials = null;
                    SourceStatusLabel.Text = "Phiên cần xác thực lại. Tài khoản đã lưu vẫn được giữ.";
                }
                else if (savedValidation.State == IslePilotOverlayAuthValidationState.Valid)
                {
                    credentials = savedValidation.Credentials;
                    await _islePilotCredentialStore.SaveAsync(credentials, _shutdown.Token);
                    await ReloadSteamAccountsAsync();
                }
            }

            if (credentials is null)
            {
                credentials = await PromptForSteamAccountAsync();
                if (credentials is null)
                {
                    SourceStatusLabel.Text = "Chưa đăng nhập Steam. Không có token nào được lưu.";
                    return;
                }
            }

            SourceStatusLabel.Text = "ĐANG KHỞI TẠO ISLEPILOT REALTIME…";
            await OpenIslePilotOverlayAsync(credentials);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không kết nối được IslePilot: {FriendlyError(exception)}";
        }
        finally
        {
            _islePilotConnecting = false;
            SetSteamLoginControlsEnabled(true);
        }
    }

    private async void LogoutSteamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_islePilotConnecting || _islePilotCredentials is null)
        {
            return;
        }

        var removedName = AccountDisplayName(_islePilotCredentials);
        await _islePilotCredentialStore.RemoveAsync(_islePilotCredentials.SteamId, _shutdown.Token);
        _islePilotSelectedSource = null;
        await ReloadSteamAccountsAsync();
        SourceStatusLabel.Text = $"Đã xóa {removedName} khỏi danh sách tài khoản đã lưu.";
    }

    private async void AddSteamAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_islePilotConnecting || _connecting)
        {
            return;
        }

        _islePilotConnecting = true;
        SetSteamLoginControlsEnabled(false);
        try
        {
            var credentials = await PromptForSteamAccountAsync(_islePilotSelectedSource);
            if (credentials is not null)
            {
                await ReloadSteamAccountsAsync();
                SourceStatusLabel.Text = $"Đã thêm {AccountDisplayName(credentials)}. Chọn tài khoản rồi mở overlay.";
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không thêm được tài khoản Steam: {FriendlyError(exception)}";
        }
        finally
        {
            _islePilotConnecting = false;
            SetSteamLoginControlsEnabled(true);
        }
    }

    private async void SteamAccountSelector_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingSteamAccountSelector ||
            SteamAccountSelector.SelectedItem is not SteamAccountChoice choice)
        {
            return;
        }

        _islePilotCredentials = choice.Credentials;
        _islePilotSelectedSource = choice.Source;
        ApplySteamLoginState(_islePilotCredentials);
        try
        {
            await _islePilotCredentialStore.SelectAsync(choice.Credentials.SteamId, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    // Older builds kept one vault per self-hosted server. The token is account-wide,
    // so those files are folded back into the shared vault once and then removed,
    // which keeps an already signed-in server working without a new Steam login.
    private async Task ImportHostedCredentialsAsync()
    {
        if (_hostedCredentialsImported)
        {
            return;
        }

        _hostedCredentialsImported = true;
        await ImportHostedCredentialsAsync(
            TelemetrySourceDefinition.All
                .Where(source => source.Kind == TelemetrySourceKind.IslePilotHosted)
                .Select(source => AppPaths.IslePilotCredentialFor(source.Id)),
            _islePilotCredentialStore,
            _shutdown.Token);
    }

    internal static async Task ImportHostedCredentialsAsync(
        IEnumerable<string> hostedVaultPaths,
        IslePilotCredentialStore sharedStore,
        CancellationToken cancellationToken)
    {
        foreach (var path in hostedVaultPaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var hostedStore = new IslePilotCredentialStore(path);
            foreach (var account in await hostedStore.LoadAllAsync(cancellationToken))
            {
                await sharedStore.SaveAsync(account, cancellationToken);
            }

            try
            {
                hostedStore.Clear();
            }
            catch (IOException)
            {
                // The shared vault already holds the account, so a locked file is fine.
            }
        }
    }

    // Servers that host their own IslePilot instance (custom domain) sign in on
    // that host and keep their own overlay token, so they are reusable later
    // without touching the network session.
    // The overlay token belongs to the Steam account, not to one host, so both the
    // network and a self-hosted server reuse the same saved session. Only the base
    // URI differs, which is why each host gets its own entry in the account list.
    private async Task ConnectHostedIslePilotAsync(TelemetrySourceDefinition source)
    {
        _islePilotSelectedSource = source;
        var credentials = _islePilotCredentials ?? await _islePilotCredentialStore.LoadAsync(_shutdown.Token);
        if (credentials is not null)
        {
            SourceStatusLabel.Text = $"ĐANG XÁC MINH PHIÊN {source.ShortName}…";
            var validation = await ValidateIslePilotCredentialsAsync(credentials, source.BaseUri);
            if (validation.State == IslePilotOverlayAuthValidationState.Invalid)
            {
                SourceStatusLabel.Text = $"Phiên đã hết hạn cho {source.BaseUri.Host}. Đăng nhập Steam lại là dùng được cho mọi server.";
                credentials = null;
            }
            else if (validation.State == IslePilotOverlayAuthValidationState.Valid)
            {
                credentials = validation.Credentials;
                await _islePilotCredentialStore.SaveAsync(credentials, _shutdown.Token);
            }
        }

        if (credentials is null)
        {
            credentials = await PromptForSteamAccountAsync(source);
            if (credentials is null)
            {
                SourceStatusLabel.Text = $"Chưa đăng nhập {source.BaseUri.Host}. Không có token nào được lưu.";
                return;
            }
        }

        SourceStatusLabel.Text = $"ĐANG KHỞI TẠO {source.ShortName} REALTIME…";
        await OpenIslePilotOverlayAsync(credentials, source);
    }

    private async Task<IslePilotOverlayAuthResult?> PromptForSteamAccountAsync(TelemetrySourceDefinition? source = null)
    {
        var loginWindow = new IslePilotSteamLoginWindow(
            source?.BaseUri ?? IslePilotOverlayOptions.DefaultServiceBaseUri)
        {
            Owner = this
        };
        if (loginWindow.ShowDialog() != true || loginWindow.Credentials is null)
        {
            return null;
        }

        SourceStatusLabel.Text = "ĐÃ NHẬN PHIÊN · ĐANG XÁC MINH /ME…";
        var validation = await ValidateIslePilotCredentialsAsync(
            loginWindow.Credentials,
            source?.BaseUri ?? IslePilotOverlayOptions.DefaultServiceBaseUri);
        if (validation.State == IslePilotOverlayAuthValidationState.Invalid)
        {
            SourceStatusLabel.Text = "IslePilot từ chối phiên vừa đăng nhập. Hãy thử lại.";
            return null;
        }

        var credentials = validation.Credentials;
        await _islePilotCredentialStore.SaveAsync(credentials, _shutdown.Token);
        _islePilotCredentials = credentials;
        if (source is not null)
        {
            _islePilotSelectedSource = source;
        }

        return credentials;
    }

    private async Task<SteamAccountValidation> ValidateIslePilotCredentialsAsync(
        IslePilotOverlayAuthResult credentials,
        Uri? serviceBaseUri = null)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var apiClient = new IslePilotOverlayApiClient(
                    httpClient,
                    new IslePilotOverlayOptions
                    {
                        OverlayToken = credentials.OverlayToken,
                        ServiceBaseUri = serviceBaseUri ?? IslePilotOverlayOptions.DefaultServiceBaseUri
                    });
                var me = await apiClient.GetMeAsync(_shutdown.Token);
                var personaName = me.PersonaName ?? me.Name ?? credentials.PersonaName;
                return new SteamAccountValidation(
                    IslePilotOverlayAuthValidationState.Valid,
                    credentials with { PersonaName = personaName });
            }
            catch (IslePilotOverlayAuthenticationException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _shutdown.Token);
            }
            catch (IslePilotOverlayAuthenticationException)
            {
                return new SteamAccountValidation(IslePilotOverlayAuthValidationState.Invalid, credentials);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException ||
                exception is OperationCanceledException && !_shutdown.IsCancellationRequested)
            {
                return new SteamAccountValidation(IslePilotOverlayAuthValidationState.Unavailable, credentials);
            }
        }

        return new SteamAccountValidation(IslePilotOverlayAuthValidationState.Unavailable, credentials);
    }

    private async Task OpenIslePilotOverlayAsync(
        IslePilotOverlayAuthResult credentials,
        TelemetrySourceDefinition? source = null)
    {
        var serviceBaseUri = source?.BaseUri ?? IslePilotOverlayOptions.DefaultServiceBaseUri;
        var realtimeSession = IslePilotRealtimeSession.Create(new IslePilotOverlayOptions
        {
            OverlayToken = credentials.OverlayToken,
            PersonaName = credentials.PersonaName,
            ServiceBaseUri = serviceBaseUri,
            WebSocketUri = WebSocketUriFor(serviceBaseUri)
        });
        try
        {
            var garageApi = new GuideGarageApi(
                realtimeSession.GetGarageAsync,
                realtimeSession.ParkGarageDinoAsync,
                realtimeSession.RestoreGarageDinoAsync,
                realtimeSession.GetGarageCommandStatusAsync,
                realtimeSession.GetSkinDraftsAsync,
                realtimeSession.SaveSkinDraftAsync,
                realtimeSession.ApplySkinPaletteAsync,
                realtimeSession.ApplySkinDraftAsync);
            var overlay = new MainWindow(
                realtimeSession,
                source?.DisplayName ?? "ISLEPILOT",
                garageApi);
            Application.Current.MainWindow = overlay;
            overlay.Show();
            Close();
        }
        catch
        {
            await realtimeSession.DisposeAsync();
            throw;
        }
    }

    private void ApplySteamLoginState(IslePilotOverlayAuthResult? credentials)
    {
        var authenticated = credentials is not null;
        if (authenticated)
        {
        }

        SteamAccountLabel.Text = authenticated
            ? AccountDisplayName(credentials!)
            : "CHƯA ĐĂNG NHẬP STEAM";
        var sourceLabel = _islePilotSelectedSource?.AccountLabel;
        SteamLoginDetailLabel.Text = authenticated
            ? sourceLabel is null
                ? $"SteamID ••••{credentials!.SteamId[^4..]} · mã hóa Windows DPAPI"
                : $"{sourceLabel} · SteamID ••••{credentials!.SteamId[^4..]}"
            : "Một phiên cho mọi server đã cài IslePilot";
        SteamLoginActionLabel.Text = authenticated ? "MỞ OVERLAY  →" : "ĐĂNG NHẬP  →";
        LogoutSteamButton.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
        SteamAccountControls.Visibility = SteamAccountSelector.Items.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetSteamLoginControlsEnabled(bool enabled)
    {
        SteamLoginButton.IsEnabled = enabled;
        LogoutSteamButton.IsEnabled = enabled && _islePilotCredentials is not null;
        AddSteamAccountButton.IsEnabled = enabled;
        SteamAccountSelector.IsEnabled = enabled;
    }

    private async Task ReloadSteamAccountsAsync()
    {
        await ImportHostedCredentialsAsync();

        var accounts = await _islePilotCredentialStore.LoadAllAsync(_shutdown.Token);
        if (_islePilotCredentials is null && accounts.Count > 0)
        {
            _islePilotCredentials = await _islePilotCredentialStore.LoadAsync(_shutdown.Token);
        }

        var choices = BuildAccountChoices(accounts);

        _updatingSteamAccountSelector = true;
        try
        {
            SteamAccountSelector.ItemsSource = choices;
            SteamAccountSelector.SelectedItem =
                choices.FirstOrDefault(choice =>
                    string.Equals(choice.Credentials.SteamId, _islePilotCredentials?.SteamId, StringComparison.Ordinal) &&
                    string.Equals(choice.Source?.Id, _islePilotSelectedSource?.Id, StringComparison.OrdinalIgnoreCase))
                ?? choices.FirstOrDefault(choice => string.Equals(
                    choice.Credentials.SteamId,
                    _islePilotCredentials?.SteamId,
                    StringComparison.Ordinal));
        }
        finally
        {
            _updatingSteamAccountSelector = false;
        }

        ApplySteamLoginState(_islePilotCredentials);
    }

    // A saved account can reach the IslePilot network and every self-hosted server,
    // so the list offers one entry per host and the label names it.
    internal static IReadOnlyList<SteamAccountChoice> BuildAccountChoices(
        IReadOnlyList<IslePilotOverlayAuthResult> accounts)
    {
        var hostedSources = TelemetrySourceDefinition.All
            .Where(source => source.Kind == TelemetrySourceKind.IslePilotHosted)
            .ToArray();
        var choices = new List<SteamAccountChoice>(accounts.Count * (1 + hostedSources.Length));
        foreach (var account in accounts)
        {
            choices.Add(new SteamAccountChoice(account, null));
            choices.AddRange(hostedSources.Select(source => new SteamAccountChoice(account, source)));
        }

        return choices;
    }

    private static Uri WebSocketUriFor(Uri serviceBaseUri) => new UriBuilder(
        string.Equals(serviceBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
        serviceBaseUri.Host)
    {
        Path = "ows"
    }.Uri;

    private static string AccountDisplayName(IslePilotOverlayAuthResult credentials) =>
        !string.IsNullOrWhiteSpace(credentials.PersonaName)
            ? $"{credentials.SteamId} ({credentials.PersonaName})"
            : credentials.SteamId;

    private sealed record SteamAccountValidation(
        IslePilotOverlayAuthValidationState State,
        IslePilotOverlayAuthResult Credentials);

    internal sealed record SteamAccountChoice(
        IslePilotOverlayAuthResult Credentials,
        TelemetrySourceDefinition? Source)
    {
        public string Title => AccountDisplayName(Credentials);

        public string Detail => $"STEAM · {Source?.AccountLabel ?? "ISLEPILOT"}";
    }
}
