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
    private IReadOnlyList<IslePilotOverlayAuthResult> _islePilotAccounts = [];
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
        ApplySteamLoginState(_islePilotCredentials);
        try
        {
            await _islePilotCredentialStore.SelectAsync(choice.Credentials.SteamId, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async Task<IslePilotOverlayAuthResult?> PromptForSteamAccountAsync()
    {
        var loginWindow = new IslePilotSteamLoginWindow { Owner = this };
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
                    new IslePilotOverlayOptions { OverlayToken = credentials.OverlayToken });
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
        var realtimeSession = IslePilotRealtimeSession.Create(new IslePilotOverlayOptions
        {
            OverlayToken = credentials.OverlayToken,
            PersonaName = credentials.PersonaName
        });
        try
        {
            var garageApi = new GuideGarageApi(
                realtimeSession.GetGarageAsync,
                realtimeSession.ParkGarageDinoAsync,
                realtimeSession.RestoreGarageDinoAsync,
                realtimeSession.GetGarageCommandStatusAsync);
            var overlay = new MainWindow(realtimeSession, "ISLEPILOT", garageApi);
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
        SteamLoginDetailLabel.Text = authenticated
            ? $"SteamID ••••{credentials!.SteamId[^4..]} · mã hóa Windows DPAPI"
            : "Một phiên cho mọi server đã cài IslePilot";
        SteamLoginActionLabel.Text = authenticated ? "MỞ OVERLAY  →" : "ĐĂNG NHẬP  →";
        LogoutSteamButton.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
        SteamAccountControls.Visibility = _islePilotAccounts.Count > 0
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
        _islePilotAccounts = await _islePilotCredentialStore.LoadAllAsync(_shutdown.Token);
        _islePilotCredentials = await _islePilotCredentialStore.LoadAsync(_shutdown.Token);
        var choices = _islePilotAccounts.Select(account => new SteamAccountChoice(account)).ToArray();

        _updatingSteamAccountSelector = true;
        try
        {
            SteamAccountSelector.ItemsSource = choices;
            SteamAccountSelector.SelectedItem = choices.FirstOrDefault(choice => string.Equals(
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

    private static string AccountDisplayName(IslePilotOverlayAuthResult credentials) =>
        !string.IsNullOrWhiteSpace(credentials.PersonaName)
            ? $"{credentials.SteamId} ({credentials.PersonaName})"
            : credentials.SteamId;

    private sealed record SteamAccountValidation(
        IslePilotOverlayAuthValidationState State,
        IslePilotOverlayAuthResult Credentials);

    private sealed record SteamAccountChoice(IslePilotOverlayAuthResult Credentials)
    {
        public string Title => AccountDisplayName(Credentials);
        public string Detail => "STEAM · ISLEPILOT";
    }
}
