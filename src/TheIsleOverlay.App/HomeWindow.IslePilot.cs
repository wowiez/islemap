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
                var validation = await ValidateIslePilotCredentialsAsync(credentials);
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

        // An SDVN #3 row kept from the overlay-token era: sign in on the website once,
        // then open the overlay with the new session straight away.
        if (_selectedWebsiteChoice is { NeedsWebsiteLogin: true, Source: { } loginSource })
        {
            if (await AddWebsiteAccountAsync(loginSource) is { } newCookie)
            {
                OpenWebsiteOverlay(loginSource, newCookie);
            }

            return;
        }

        // A saved website session (SBTC Island, EraGaming, PANDORA, SDVN #3) needs no
        // Steam handshake: the site cookie is enough.
        if (_selectedWebsiteChoice is { WebsiteCookie: { } websiteCookie, Source: { } websiteSource })
        {
            OpenWebsiteOverlay(websiteSource, websiteCookie);
            return;
        }

        _islePilotConnecting = true;
        SetSteamLoginControlsEnabled(false);
        try
        {
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

    private void OpenWebsiteOverlay(TelemetrySourceDefinition source, string cookie)
    {
        try
        {
            var websiteOverlay = new MainWindow(source, cookie);
            Application.Current.MainWindow = websiteOverlay;
            websiteOverlay.Show();
            Close();
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không mở được overlay: {FriendlyError(exception)}";
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
        await ReloadSteamAccountsAsync();
        SourceStatusLabel.Text = $"Đã xóa {removedName} khỏi danh sách tài khoản đã lưu.";
    }

    private async void RemoveWebsiteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWebsiteChoice is not { Source: { } source } choice)
        {
            return;
        }

        if (choice.NeedsWebsiteLogin)
        {
            // Nothing is stored for this row; forgetting the server drops it from the list.
            ForgetServer(source);
            _selectedWebsiteChoice = null;
            await ReloadSteamAccountsAsync();
            SourceStatusLabel.Text = $"Đã bỏ {source.DisplayName} khỏi danh sách.";
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

        ForgetServer(source);
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
            var credentials = await PromptForSteamAccountAsync();
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
    // so the server then shows up in the list like any other account. Returns the new
    // session cookie, or null when the login was not completed.
    private async Task<string?> AddWebsiteAccountAsync(TelemetrySourceDefinition source)
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
                return null;
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
            return loginWindow.CookieValue;
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

        return null;
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

        if (choice.Credentials is null)
        {
            RememberServer(choice);
            _islePilotCredentials = null;
            _selectedWebsiteChoice = choice;
            ApplySteamLoginState(null);
            SourceStatusLabel.Text = choice.NeedsWebsiteLogin
                ? $"{choice.Source?.DisplayName}: cần đăng nhập lại trên website {choice.Source?.BaseUri.Host}. Bấm ĐĂNG NHẬP."
                : $"{choice.Source?.DisplayName}: phiên đã lưu. Bấm MỞ OVERLAY để vào bản đồ.";
            return;
        }

        _selectedWebsiteChoice = null;
        if (choice.Credentials is not { } credentials) return;
        RememberServer(choice);
        _islePilotCredentials = credentials;
        ApplySteamLoginState(_islePilotCredentials);
        try
        {
            await _islePilotCredentialStore.SelectAsync(credentials.SteamId, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async Task<IslePilotOverlayAuthResult?> PromptForSteamAccountAsync()
    {
        var loginWindow = new IslePilotSteamLoginWindow(IslePilotOverlayOptions.DefaultServiceBaseUri)
        {
            Owner = this
        };
        if (loginWindow.ShowDialog() != true || loginWindow.Credentials is null)
        {
            return null;
        }

        SourceStatusLabel.Text = "ĐÃ NHẬN PHIÊN · ĐANG XÁC MINH /ME…";
        var validation = await ValidateIslePilotCredentialsAsync(loginWindow.Credentials);
        if (validation.State == IslePilotOverlayAuthValidationState.Invalid)
        {
            SourceStatusLabel.Text = "IslePilot từ chối phiên vừa đăng nhập. Hãy thử lại.";
            return null;
        }

        var credentials = validation.Credentials;
        await _islePilotCredentialStore.SaveAsync(credentials, _shutdown.Token);
        _islePilotCredentials = credentials;
        return credentials;
    }

    private async Task<SteamAccountValidation> ValidateIslePilotCredentialsAsync(
        IslePilotOverlayAuthResult credentials)
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
                        ServiceBaseUri = IslePilotOverlayOptions.DefaultServiceBaseUri
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

    private async Task OpenIslePilotOverlayAsync(IslePilotOverlayAuthResult credentials)
    {
        var serviceBaseUri = IslePilotOverlayOptions.DefaultServiceBaseUri;
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
            var overlay = new MainWindow(realtimeSession, "ISLEPILOT");
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

        // The card describes whichever account is selected: a Steam session, a saved
        // website session, or nothing yet.
        if (_selectedWebsiteChoice is { Source: { } websiteSource } websiteChoice)
        {
            SteamAccountLabel.Text = websiteSource.DisplayName.ToUpperInvariant();
            SteamLoginDetailLabel.Text = websiteChoice.Detail;
        }
        else
        {
            SteamAccountLabel.Text = authenticated
                ? AccountDisplayName(credentials!)
                : "CHƯA ĐĂNG NHẬP STEAM";
            SteamLoginDetailLabel.Text = authenticated
                ? $"SteamID ••••{credentials!.SteamId[^4..]} · mã hóa DPAPI"
                : "Một phiên cho mọi server đã cài IslePilot";
        }

        SteamLoginActionLabel.Text = authenticated || _selectedWebsiteChoice?.IsWebsiteSession == true
            ? "MỞ OVERLAY"
            : "ĐĂNG NHẬP";
        LogoutSteamButton.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
        RemoveWebsiteSessionButton.Visibility = _selectedWebsiteChoice is { Credentials: null }
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

        var choices = BuildAccountChoices(accounts, sessions,
            keepSdvnRelogin: string.Equals(_preferredServerId, TelemetrySourceDefinition.Sdvn3.Id, StringComparison.OrdinalIgnoreCase));

        _updatingSteamAccountSelector = true;
        try
        {
            SteamAccountSelector.ItemsSource = choices;
            var selected = SelectSavedServer(choices, _preferredServerId, _islePilotCredentials?.SteamId);
            SteamAccountSelector.SelectedItem = selected;
            _selectedWebsiteChoice = selected is { Credentials: null } ? selected : null;
            _islePilotCredentials = selected?.Credentials;
        }
        finally
        {
            _updatingSteamAccountSelector = false;
        }

        ApplySteamLoginState(_islePilotCredentials);
    }

    private void ForgetServer(TelemetrySourceDefinition source)
    {
        if (!string.Equals(_preferredServerId, source.Id, StringComparison.OrdinalIgnoreCase)) return;
        _preferredServerId = HomeServerSelectionStore.IslePilot;
        _serverSelectionStore.Save(_preferredServerId);
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

    internal static IReadOnlyList<SteamAccountChoice> BuildAccountChoices(
        IReadOnlyList<IslePilotOverlayAuthResult> accounts,
        IReadOnlyDictionary<string, string>? websiteSessions = null,
        bool keepSdvnRelogin = false)
    {
        var choices = accounts.Select(account => new SteamAccountChoice(account, null)).ToList();

        // Servers that run their own website (SBTC Island, EraGaming, PANDORA, SDVN #3)
        // keep their session in the website vault, so a saved login shows up here as its
        // own row.
        foreach (var source in WebsiteSources)
        {
            if (websiteSessions is not null &&
                websiteSessions.TryGetValue(source.Id, out var cookie) &&
                !string.IsNullOrWhiteSpace(cookie))
            {
                choices.Add(new SteamAccountChoice(null, source, cookie));
            }
            else if (source == TelemetrySourceDefinition.Sdvn3 && keepSdvnRelogin)
            {
                // Players who last used SDVN #3 on the overlay token keep the row; SDVN turned
                // that API off, so it now asks for a website login (or can be removed).
                choices.Add(new SteamAccountChoice(null, source));
            }
        }

        return choices;
    }

    /// <summary>The servers that are reached through their own site instead of the IslePilot network.</summary>
    internal static IReadOnlyList<TelemetrySourceDefinition> WebsiteSources { get; } =
        TelemetrySourceDefinition.All
            .Where(source => source.Kind is TelemetrySourceKind.EraGaming
                    or TelemetrySourceKind.Pandora
                    or TelemetrySourceKind.SbtcIsland
                || source == TelemetrySourceDefinition.Sdvn3)
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

        public bool NeedsWebsiteLogin => Credentials is null && WebsiteCookie is null && Source is not null;

        public string Title => Credentials is not null
            ? AccountDisplayName(Credentials)
            : Source?.DisplayName ?? "NGUỒN WEBSITE";

        public string Detail => Credentials is not null
            ? "STEAM · ISLEPILOT"
            : $"WEBSITE · {Source?.BaseUri.Host} · {(NeedsWebsiteLogin ? "cần đăng nhập lại" : "phiên đã lưu")}";
    }
}
