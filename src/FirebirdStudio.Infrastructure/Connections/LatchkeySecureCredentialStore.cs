using System.Security.Cryptography;
using System.Text;
using FirebirdStudio.Application.Connections;
using Latchkey;

namespace FirebirdStudio.Infrastructure.Connections;

/// <summary>
/// Stores Firebird passwords in the operating system credential facility through Latchkey.
/// Latchkey's Auto backend uses Windows Credential Manager, macOS Keychain, or Linux Secret
/// Service and does not silently fall back to a plaintext file.
/// </summary>
public sealed class LatchkeySecureCredentialStore : ISecureCredentialStore
{
    private const string ServiceName = "com.firebirdstudio.desktop";
    private const string SecretSuffix = "/firebird-password";
    private readonly Func<ILatchkey> _storeFactory;
    private readonly object _storeSync = new();
    private ILatchkey? _store;

    public LatchkeySecureCredentialStore()
        : this(() => LatchkeyFactory.Create(ServiceName))
    {
    }

    internal LatchkeySecureCredentialStore(ILatchkey store)
        : this(() => store ?? throw new ArgumentNullException(nameof(store)))
    {
    }

    private LatchkeySecureCredentialStore(Func<ILatchkey> storeFactory)
    {
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
    }

    private ILatchkey GetStore()
    {
        lock (_storeSync)
            return _store ??= _storeFactory();
    }

    public async Task SavePasswordAsync(
        Guid profileId,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        ArgumentNullException.ThrowIfNull(password);

        var secretBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            // Latchkey's native credential APIs are synchronous even through its ValueTask API.
            // Move the native call off the UI thread; cancellation can prevent queueing but cannot
            // interrupt a native OS prompt or credential operation after it has started.
            await Task.Run(() => GetStore().Set(GetKey(profileId), secretBytes), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    public async Task<string?> GetPasswordAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);

        var secretBytes = await Task.Run(
            () => GetStore().GetBytes(GetKey(profileId)), cancellationToken).ConfigureAwait(false);
        if (secretBytes is null)
            return null;

        try
        {
            return Encoding.UTF8.GetString(secretBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    public async Task DeletePasswordAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        await Task.Run(() => GetStore().Delete(GetKey(profileId)), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string GetKey(Guid profileId) => $"{profileId:N}{SecretSuffix}";

    private static void ValidateProfileId(Guid profileId)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));
    }
}
