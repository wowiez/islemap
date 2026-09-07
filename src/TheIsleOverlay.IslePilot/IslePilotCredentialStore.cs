using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotCredentialStore
{
    private const int MaximumCredentialBytes = 1024 * 1024;
    private const int CurrentVaultVersion = 2;

    private static readonly byte[] FileHeader = "ILM1"u8.ToArray();
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes(
        "IsleLiveMap.IslePilotOverlay.v3");
    private static readonly byte[][] LegacyOptionalEntropies =
    [
        Encoding.UTF8.GetBytes(
            string.Join(string.Empty, "Wo", "wiez", ".IsleLiveMap.IslePilotOverlay.v2")),
        Encoding.UTF8.GetBytes(
            string.Join(string.Empty, "K", "Long", "Dev", ".IsleLiveMap.IslePilotOverlay.v1"))
    ];

    private readonly string _credentialPath;

    public IslePilotCredentialStore(string credentialPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialPath);
        _credentialPath = Path.GetFullPath(credentialPath);
    }

    public async Task SaveAsync(
        IslePilotOverlayAuthResult credentials,
        CancellationToken cancellationToken = default)
    {
        Validate(credentials);
        var vault = await LoadVaultAsync(cancellationToken);
        var accounts = vault.Accounts
            .Where(account => !string.Equals(
                account.SteamId,
                credentials.SteamId,
                StringComparison.Ordinal))
            .Append(ToStored(credentials))
            .ToArray();
        await WriteVaultAsync(
            new StoredCredentialVault
            {
                Version = CurrentVaultVersion,
                SelectedSteamId = credentials.SteamId,
                Accounts = accounts
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<IslePilotOverlayAuthResult>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        var vault = await LoadVaultAsync(cancellationToken);
        return vault.Accounts.Select(ToCredentials).ToArray();
    }

    public async Task<bool> MigrateLegacyAsync(
        string legacyCredentialPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyCredentialPath);

        legacyCredentialPath = Path.GetFullPath(legacyCredentialPath);
        if (!File.Exists(legacyCredentialPath))
        {
            return true;
        }

        var legacyVault = await TryLoadVaultFromPathAsync(
            legacyCredentialPath,
            LegacyOptionalEntropies,
            cancellationToken);
        if (legacyVault is null)
        {
            return false;
        }

        var currentVault = await LoadVaultAsync(cancellationToken);
        var accounts = legacyVault.Accounts
            .Concat(currentVault.Accounts)
            .Where(IsValid)
            .GroupBy(account => account.SteamId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var selectedSteamId = currentVault.Accounts.Count > 0
            ? currentVault.SelectedSteamId
            : legacyVault.SelectedSteamId;

        if (accounts.Length > 0)
        {
            await WriteVaultAsync(
                new StoredCredentialVault
                {
                    Version = CurrentVaultVersion,
                    SelectedSteamId = selectedSteamId,
                    Accounts = accounts
                },
                cancellationToken);
        }

        return true;
    }

    public async Task<IslePilotOverlayAuthResult?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var vault = await LoadVaultAsync(cancellationToken);
        var selected = vault.Accounts.FirstOrDefault(account => string.Equals(
            account.SteamId,
            vault.SelectedSteamId,
            StringComparison.Ordinal)) ?? vault.Accounts.FirstOrDefault();
        return selected is null ? null : ToCredentials(selected);
    }

    public async Task SelectAsync(
        string steamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);
        var vault = await LoadVaultAsync(cancellationToken);
        if (!vault.Accounts.Any(account => string.Equals(account.SteamId, steamId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The Steam account is not stored.", nameof(steamId));
        }

        await WriteVaultAsync(vault with { SelectedSteamId = steamId }, cancellationToken);
    }

    public async Task RemoveAsync(
        string steamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);
        var vault = await LoadVaultAsync(cancellationToken);
        var accounts = vault.Accounts
            .Where(account => !string.Equals(account.SteamId, steamId, StringComparison.Ordinal))
            .ToArray();
        if (accounts.Length == 0)
        {
            Clear();
            return;
        }

        var selectedSteamId = string.Equals(vault.SelectedSteamId, steamId, StringComparison.Ordinal)
            ? accounts[0].SteamId
            : vault.SelectedSteamId;
        await WriteVaultAsync(
            vault with { SelectedSteamId = selectedSteamId, Accounts = accounts },
            cancellationToken);
    }

    public void Remove(string steamId) => RemoveAsync(steamId).GetAwaiter().GetResult();

    public void Clear()
    {
        if (File.Exists(_credentialPath))
        {
            File.Delete(_credentialPath);
        }
    }

    private async Task WriteVaultAsync(
        StoredCredentialVault vault,
        CancellationToken cancellationToken)
    {
        var cleartext = JsonSerializer.SerializeToUtf8Bytes(vault);
        byte[]? protectedData = null;
        try
        {
            protectedData = WindowsDataProtection.Protect(cleartext, OptionalEntropy);
            var fileData = new byte[FileHeader.Length + protectedData.Length];
            FileHeader.CopyTo(fileData, 0);
            protectedData.CopyTo(fileData, FileHeader.Length);

            var directory = Path.GetDirectoryName(_credentialPath)
                ?? throw new InvalidOperationException("The credential path has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_credentialPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, fileData, cancellationToken);
                File.Move(temporaryPath, _credentialPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                CryptographicOperations.ZeroMemory(fileData);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
            if (protectedData is not null)
            {
                CryptographicOperations.ZeroMemory(protectedData);
            }
        }
    }

    private async Task<StoredCredentialVault> LoadVaultAsync(
        CancellationToken cancellationToken) =>
        await TryLoadVaultFromPathAsync(_credentialPath, [OptionalEntropy], cancellationToken)
        ?? EmptyVault();

    private static async Task<StoredCredentialVault?> TryLoadVaultFromPathAsync(
        string credentialPath,
        IReadOnlyList<byte[]> entropies,
        CancellationToken cancellationToken)
    {
        byte[] fileData;
        try
        {
            var file = new FileInfo(credentialPath);
            if (!file.Exists
                || file.Length <= FileHeader.Length
                || file.Length > MaximumCredentialBytes)
            {
                return null;
            }

            fileData = await File.ReadAllBytesAsync(credentialPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            if (!fileData.AsSpan(0, FileHeader.Length).SequenceEqual(FileHeader))
            {
                return null;
            }

            foreach (var entropy in entropies)
            {
                byte[]? cleartext = null;
                try
                {
                    cleartext = WindowsDataProtection.Unprotect(
                        fileData.AsSpan(FileHeader.Length),
                        entropy);
                    var vault = JsonSerializer.Deserialize<StoredCredentialVault>(cleartext);
                    if (vault is { Version: CurrentVaultVersion, Accounts: not null })
                    {
                        return Normalize(vault);
                    }

                    // Backward-compatible migration from the original single-account
                    // ILM1 payload. It is rewritten as a v2 vault on the next save.
                    var legacy = JsonSerializer.Deserialize<StoredCredential>(cleartext);
                    if (legacy is not null && IsValid(legacy))
                    {
                        return new StoredCredentialVault
                        {
                            Version = CurrentVaultVersion,
                            SelectedSteamId = legacy.SteamId,
                            Accounts = [legacy]
                        };
                    }
                }
                catch (CryptographicException)
                {
                }
                catch (JsonException)
                {
                }
                finally
                {
                    if (cleartext is not null)
                    {
                        CryptographicOperations.ZeroMemory(cleartext);
                    }
                }
            }

            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileData);
        }
    }

    private static StoredCredentialVault Normalize(StoredCredentialVault vault)
    {
        var accounts = vault.Accounts
            .Where(IsValid)
            .GroupBy(account => account.SteamId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var selected = accounts.Any(account => string.Equals(
            account.SteamId,
            vault.SelectedSteamId,
            StringComparison.Ordinal))
            ? vault.SelectedSteamId
            : accounts.FirstOrDefault()?.SteamId;
        return new StoredCredentialVault
        {
            Version = CurrentVaultVersion,
            SelectedSteamId = selected,
            Accounts = accounts
        };
    }

    private static StoredCredentialVault EmptyVault() => new()
    {
        Version = CurrentVaultVersion,
        Accounts = []
    };

    private static void Validate(IslePilotOverlayAuthResult credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (!IslePilotOverlayAuthService.IsValidCredentials(credentials.SteamId, credentials.OverlayToken))
        {
            throw new ArgumentException("The IslePilot credentials are invalid.", nameof(credentials));
        }
    }

    private static bool IsValid(StoredCredential credential) =>
        IslePilotOverlayAuthService.IsValidCredentials(credential.SteamId, credential.OverlayToken);

    private static StoredCredential ToStored(IslePilotOverlayAuthResult credentials) => new(
        credentials.SteamId,
        credentials.OverlayToken,
        credentials.PersonaName);

    private static IslePilotOverlayAuthResult ToCredentials(StoredCredential stored) => new(
        stored.SteamId,
        stored.OverlayToken,
        stored.PersonaName);

    private sealed record StoredCredentialVault
    {
        public int Version { get; init; }
        public string? SelectedSteamId { get; init; }
        public IReadOnlyList<StoredCredential> Accounts { get; init; } = [];
    }

    private sealed record StoredCredential(
        string SteamId,
        string OverlayToken,
        string? PersonaName = null);
}

internal static class WindowsDataProtection
{
    private const uint CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(
        ReadOnlySpan<byte> cleartext,
        ReadOnlySpan<byte> optionalEntropy) =>
        Transform(cleartext, optionalEntropy, protect: true);

    public static byte[] Unprotect(
        ReadOnlySpan<byte> protectedData,
        ReadOnlySpan<byte> optionalEntropy) =>
        Transform(protectedData, optionalEntropy, protect: false);

    private static byte[] Transform(
        ReadOnlySpan<byte> input,
        ReadOnlySpan<byte> optionalEntropy,
        bool protect)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is required for IslePilot credentials.");
        }

        var inputBytes = input.ToArray();
        var entropyBytes = optionalEntropy.ToArray();
        var inputBlob = AllocateBlob(inputBytes);
        var entropyBlob = AllocateBlob(entropyBytes);
        DataBlob outputBlob = default;
        IntPtr description = IntPtr.Zero;

        try
        {
            var succeeded = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    out description,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);

            if (!succeeded)
            {
                var error = Marshal.GetLastWin32Error();
                throw new CryptographicException(new Win32Exception(error).Message);
            }

            if (outputBlob.Data == IntPtr.Zero || outputBlob.Length <= 0)
            {
                throw new CryptographicException("Windows DPAPI returned an empty result.");
            }

            var result = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputBytes);
            CryptographicOperations.ZeroMemory(entropyBytes);
            ZeroAndFreeHGlobal(ref inputBlob);
            ZeroAndFreeHGlobal(ref entropyBlob);
            ZeroAndLocalFree(ref outputBlob);
            if (description != IntPtr.Zero)
            {
                LocalFree(description);
            }
        }
    }

    private static DataBlob AllocateBlob(byte[] data)
    {
        var blob = new DataBlob
        {
            Length = data.Length,
            Data = Marshal.AllocHGlobal(data.Length)
        };
        Marshal.Copy(data, 0, blob.Data, data.Length);
        return blob;
    }

    private static void ZeroAndFreeHGlobal(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
        Marshal.FreeHGlobal(blob.Data);
        blob = default;
    }

    private static void ZeroAndLocalFree(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
        LocalFree(blob.Data);
        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        uint flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        uint flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
