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
    private SteamAccountChoice? _selectedWebsiteChoice;
    private bool _updatingSteamAccountSelector;
    private readonly HomeServerSelectionStore _serverSelectionStore = new();
    private string? _preferredServerId;

    private async void SteamLoginPanel_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _preferredServerId = _serverSelectionStore.Load();
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

        // A saved website session (SBTC Island, EraGaming, PANDORA) needs no Steam
        // handshake: the site cookie is enough.
        if (_selectedWebsiteChoice is { WebsiteCookie: { } websiteCookie, Source: { } websiteSource })
        {
            try
            {
                var websiteOverlay = new MainWindow(websiteSource, websiteCookie);
                Application.Current.MainWindow = websiteOverlay;
                websiteOverlay.Show();
                Close();
            }
            catch (Exception exception)
            {
                SourceStatusLabel.Text = $"Không mở được overlay: {FriendlyError(exception)}";
            }

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

    private async void RemoveWebsiteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWebsiteChoice?.Source is not { } source)
        {
            return;
        }

        try
        {
            await _websiteSessions.RemoveAsync(source.Id, _shutdown.Token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            SourceStatusLabel.Text = "Không xóa được phiên đã lưu.";
            return;
        }

        _selectedWebsiteChoice = null;
        await ReloadSteamAccountsAsync();
        SourceStatusLabel.Text = $"Đã xóa phiên {source.DisplayName}. Đăng nhập lại để thêm.";
    }

    /// <summary>
    /// Adding an account asks which kind first: an IslePilot network account (Steam), or
    /// a server that runs its own website such as SBTC Island. Both end up in the same
    /// list, the website ones carrying their saved session.
    /// </summary>
    private void AddSteamAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_islePilotConnecting || _connecting)
        {
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = AddSteamAccountButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };

        var network = new System.Windows.Controls.MenuItem { Header = "IslePilot Network (Steam)" };
        network.Click += async (_, _) => await AddSteamAccountAsync();
        menu.Items.Add(network);

        foreach (var source in WebsiteSources)
        {
            var website = new System.Windows.Controls.MenuItem
            {
                Header = $"Website riêng · {source.DisplayName}",
                ToolTip = source.BaseUri.Host
            };
            website.Click += async (_, _) => await AddWebsiteAccountAsync(source);
            menu.Items.Add(website);
        }

        menu.IsOpen = true;
    }

    private async Task AddSteamAccountAsync()
    {
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

    // A website server is added by signing in on its own site once; the session is kept
    // so the server then shows up in the list like any other account.
    private async Task AddWebsiteAccountAsync(TelemetrySourceDefinition source)
    {
        _islePilotConnecting = true;
        SetSteamLoginControlsEnabled(false);
        SourceStatusLabel.Text = $"ĐANG MỞ ĐĂNG NHẬP {source.DisplayName.ToUpperInvariant()}…";
        try
        {
            var loginWindow = new LoginWindow(source, (cookie, token) => ValidateSessionAsync(source, cookie, token))
            {
                Owner = this
            };
            if (loginWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(loginWindow.CookieValue))
            {
                SourceStatusLabel.Text = "Chưa nhận được phiên. Đăng nhập trong cửa sổ vừa mở rồi bấm KIỂM TRA PHIÊN.";
                return;
            }

            await _websiteSessions.SaveAsync(source.Id, loginWindow.CookieValue, _shutdown.Token);
            _selectedWebsiteChoice = null;
            await ReloadSteamAccountsAsync();

            // Select the row that was just added.
            if (SteamAccountSelector.ItemsSource is IEnumerable<SteamAccountChoice> choices)
            {
                var added = choices.FirstOrDefault(choice =>
                    choice.IsWebsiteSession &&
                    string.Equals(choice.Source?.Id, source.Id, StringComparison.OrdinalIgnoreCase));
                if (added is not null)
                {
                    SteamAccountSelector.SelectedItem = added;
                }
            }

            SourceStatusLabel.Text = $"Đã thêm {source.DisplayName}. Chọn nó trong danh sách rồi mở overlay.";
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không thêm được {source.DisplayName}: {FriendlyError(exception)}";
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

        if (choice.IsWebsiteSession)
        {
            RememberServer(choice);
            _islePilotCredentials = null;
            _islePilotSelectedSource = null;
            _selectedWebsiteChoice = choice;
            ApplySteamLoginState(null);
            SourceStatusLabel.Text =
                $"{choice.Source?.DisplayName}: phiên đã lưu. Bấm MỞ OVERLAY để vào bản đồ.";
            return;
        }

        _selectedWebsiteChoice = null;
        if (choice.Credentials is not { } credentials) return;
        RememberServer(choice);
        _islePilotCredentials = credentials;
        _islePilotSelectedSource = choice.Source;
        ApplySteamLoginState(_islePilotCredentials);
        try
        {
            await _islePilotCredentialStore.SelectAsync(credentials.SteamId, _shutdown.Token);
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
            // The garage and the skin editor moved to the server's own site, so the
            // overlay opens with telemetry only.
            var overlay = new MainWindow(realtimeSession, source?.DisplayName ?? "ISLEPILOT");
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
        var websiteSelected = _selectedWebsiteChoice is not null;

        // The card describes whichever account is selected: a Steam session, a saved
        // website session, or nothing yet.
        if (websiteSelected && _selectedWebsiteChoice?.Source is { } websiteSource)
        {
            SteamAccountLabel.Text = websiteSource.DisplayName.ToUpperInvariant();
            SteamLoginDetailLabel.Text = $"WEBSITE · {websiteSource.BaseUri.Host} · phiên đã lưu";
        }
        else
        {
            SteamAccountLabel.Text = authenticated
                ? AccountDisplayName(credentials!)
                : "CHƯA ĐĂNG NHẬP STEAM";
            var sourceLabel = _islePilotSelectedSource?.AccountLabel;
            SteamLoginDetailLabel.Text = authenticated
                ? sourceLabel is null
                    ? $"SteamID ••••{credentials!.SteamId[^4..]} · mã hóa Windows DPAPI"
                    : $"{sourceLabel} · SteamID ••••{credentials!.SteamId[^4..]}"
                : "Một phiên cho mọi server đã cài IslePilot";
        }

        SteamLoginActionLabel.Text = authenticated || websiteSelected ? "MỞ OVERLAY  →" : "ĐĂNG NHẬP  →";
        LogoutSteamButton.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
        RemoveWebsiteSessionButton.Visibility = websiteSelected && _selectedWebsiteChoice?.Credentials is null
            ? Visibility.Visible
            : Visibility.Collapsed;
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

        IReadOnlyDictionary<string, string> sessions;
        try
        {
            sessions = await _websiteSessions.LoadAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }

        var choices = BuildAccountChoices(accounts, sessions);

        _updatingSteamAccountSelector = true;
        try
        {
            SteamAccountSelector.ItemsSource = choices;
            var selected = SelectSavedServer(choices, _preferredServerId, _islePilotCredentials?.SteamId);
            SteamAccountSelector.SelectedItem = selected;
            _selectedWebsiteChoice = selected?.IsWebsiteSession == true ? selected : null;
            _islePilotCredentials = selected?.Credentials;
            _islePilotSelectedSource = selected?.IsWebsiteSession == false ? selected.Source : null;
        }
        finally
        {
            _updatingSteamAccountSelector = false;
        }

        ApplySteamLoginState(_islePilotCredentials);
    }

    private void RememberServer(SteamAccountChoice choice)
    {
        _preferredServerId = choice.Source?.Id ?? HomeServerSelectionStore.IslePilot;
        _serverSelectionStore.Save(_preferredServerId);
    }

    internal static SteamAccountChoice? SelectSavedServer(
        IReadOnlyList<SteamAccountChoice> choices, string? sourceId, string? steamId)
    {
        bool SameServer(SteamAccountChoice choice) => string.Equals(
            choice.Source?.Id ?? HomeServerSelectionStore.IslePilot, sourceId, StringComparison.OrdinalIgnoreCase);
        bool SameAccount(SteamAccountChoice choice) => choice.Credentials is { } account &&
            string.Equals(account.SteamId, steamId, StringComparison.Ordinal);
        return choices.FirstOrDefault(choice => SameServer(choice) && SameAccount(choice))
            ?? choices.FirstOrDefault(SameServer)
            ?? choices.FirstOrDefault(SameAccount)
            ?? choices.FirstOrDefault();
    }

    // A saved account can reach the IslePilot network and every self-hosted server,
    // so the list offers one entry per host and the label names it.
    internal static IReadOnlyList<SteamAccountChoice> BuildAccountChoices(
        IReadOnlyList<IslePilotOverlayAuthResult> accounts,
        IReadOnlyDictionary<string, string>? websiteSessions = null)
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

        // Servers that run their own website (SBTC Island, EraGaming, PANDORA) keep their
        // session in the website vault, so a saved login shows up here as its own row.
        foreach (var sources in WebsiteSources)
        {
            if (websiteSessions is not null &&
                websiteSessions.TryGetValue(sources.Id, out var cookie) &&
                !string.IsNullOrWhiteSpace(cookie))
            {
                choices.Add(new SteamAccountChoice(null, sources, cookie));
            }
        }

        return choices;
    }

    /// <summary>The servers that are reached through their own site instead of IslePilot.</summary>
    internal static IReadOnlyList<TelemetrySourceDefinition> WebsiteSources { get; } =
        TelemetrySourceDefinition.All
            .Where(source => source.Kind is TelemetrySourceKind.EraGaming
                or TelemetrySourceKind.Pandora
                or TelemetrySourceKind.SbtcIsland)
            .ToArray();

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
        IslePilotOverlayAuthResult? Credentials,
        TelemetrySourceDefinition? Source,
        string? WebsiteCookie = null)
    {
        public bool IsWebsiteSession => Credentials is null && WebsiteCookie is not null;

        public string Title => Credentials is not null
            ? AccountDisplayName(Credentials)
            : Source?.DisplayName ?? "NGUỒN WEBSITE";

        public string Detail => Credentials is not null
            ? $"STEAM · {Source?.AccountLabel ?? "ISLEPILOT"}"
            : $"WEBSITE · {Source?.BaseUri.Host} · phiên đã lưu";
    }
}
