using System.Text;
using Latchkey;
using FirebirdStudio.Infrastructure.Connections;

namespace FirebirdStudio.Tests.Infrastructure;

public sealed class LatchkeySecureCredentialStoreTests
{
    [Fact]
    public async Task SaveGetAndDeleteUseStableProfileScopedKey()
    {
        var fakeStore = new FakeLatchkey();
        var credentialStore = new LatchkeySecureCredentialStore(fakeStore);
        var profileId = Guid.NewGuid();

        await credentialStore.SavePasswordAsync(profileId, "pāssword-秘密");
        var password = await credentialStore.GetPasswordAsync(profileId);
        var key = $"{profileId:N}/firebird-password";

        Assert.Equal("pāssword-秘密", password);
        Assert.True(fakeStore.Contains(key));

        await credentialStore.DeletePasswordAsync(profileId);

        Assert.False(fakeStore.Contains(key));
        Assert.Null(await credentialStore.GetPasswordAsync(profileId));
    }

    [Fact]
    public async Task MissingCredentialReturnsNull()
    {
        var credentialStore = new LatchkeySecureCredentialStore(new FakeLatchkey());

        var password = await credentialStore.GetPasswordAsync(Guid.NewGuid());

        Assert.Null(password);
    }

    [Fact]
    public async Task RetrievedSecretBufferIsZeroedAfterDecoding()
    {
        var fakeStore = new FakeLatchkey();
        var credentialStore = new LatchkeySecureCredentialStore(fakeStore);
        var profileId = Guid.NewGuid();
        await credentialStore.SavePasswordAsync(profileId, "temporary-secret");

        _ = await credentialStore.GetPasswordAsync(profileId);

        Assert.NotNull(fakeStore.LastReturnedBytes);
        Assert.All(fakeStore.LastReturnedBytes!, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task EmptyProfileIdentifierIsRejected()
    {
        var credentialStore = new LatchkeySecureCredentialStore(new FakeLatchkey());

        await Assert.ThrowsAsync<ArgumentException>(() => credentialStore.SavePasswordAsync(Guid.Empty, "secret"));
        await Assert.ThrowsAsync<ArgumentException>(() => credentialStore.GetPasswordAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => credentialStore.DeletePasswordAsync(Guid.Empty));
    }

    [Fact]
    public async Task PreCancelledSaveDoesNotWriteCredential()
    {
        var fakeStore = new FakeLatchkey();
        var credentialStore = new LatchkeySecureCredentialStore(fakeStore);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => credentialStore.SavePasswordAsync(Guid.NewGuid(), "secret", cancellation.Token));
        Assert.Empty(fakeStore.Keys);
    }

    private sealed class FakeLatchkey : ILatchkey
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public byte[]? LastReturnedBytes { get; private set; }
        public string[] Keys
        {
            get
            {
                lock (_sync)
                    return _entries.Keys.ToArray();
            }
        }

        public void Set(string key, string value) => Set(key, Encoding.UTF8.GetBytes(value));

        public void Set(string key, ReadOnlySpan<byte> value)
        {
            lock (_sync)
            {
                if (_entries.Remove(key, out var previous))
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(previous);
                _entries[key] = value.ToArray();
            }
        }

        public string? Get(string key)
        {
            var bytes = GetBytes(key);
            return bytes is null ? null : Encoding.UTF8.GetString(bytes);
        }

        public byte[]? GetBytes(string key)
        {
            lock (_sync)
            {
                if (!_entries.TryGetValue(key, out var value))
                    return null;

                LastReturnedBytes = value.ToArray();
                return LastReturnedBytes;
            }
        }

        public bool Delete(string key)
        {
            lock (_sync)
            {
                if (!_entries.Remove(key, out var value))
                    return false;

                System.Security.Cryptography.CryptographicOperations.ZeroMemory(value);
                return true;
            }
        }

        public bool Contains(string key)
        {
            lock (_sync)
                return _entries.ContainsKey(key);
        }

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Set(key, value);
            return ValueTask.CompletedTask;
        }

        public ValueTask SetAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Set(key, value.Span);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Get(key));
        }

        public ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GetBytes(key));
        }

        public ValueTask<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Delete(key));
        }

        public ValueTask<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Contains(key));
        }
    }
}
