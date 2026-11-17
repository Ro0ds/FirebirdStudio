using FirebirdStudio.Infrastructure.Connections;

namespace FirebirdStudio.Tests.Infrastructure;

public sealed class JsonConnectionProfileRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"FirebirdStudio.Tests.{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_directory, "connections.json");

    [Fact]
    public async Task SaveAndLoadPersistsProfilesWithoutPasswordFields()
    {
        var repository = new JsonConnectionProfileRepository(FilePath);
        var profile = TestProfile.Create();

        await repository.SaveAsync(profile);
        var loaded = await repository.GetAllAsync();
        var json = await File.ReadAllTextAsync(FilePath);

        Assert.Single(loaded);
        Assert.Equal(profile, loaded[0]);
        Assert.False(json.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SaveWithExistingIdentifierUpdatesProfile()
    {
        var repository = new JsonConnectionProfileRepository(FilePath);
        var profile = TestProfile.Create();
        await repository.SaveAsync(profile);

        await repository.SaveAsync(profile with { Name = "Renamed connection" });

        var loaded = await repository.GetAllAsync();
        Assert.Single(loaded);
        Assert.Equal("Renamed connection", loaded[0].Name);
    }

    [Fact]
    public async Task DeleteRemovesOnlyRequestedProfile()
    {
        var repository = new JsonConnectionProfileRepository(FilePath);
        var first = TestProfile.Create("First");
        var second = TestProfile.Create("Second");
        await repository.SaveAsync(first);
        await repository.SaveAsync(second);

        await repository.DeleteAsync(first.Id);

        var loaded = await repository.GetAllAsync();
        var remaining = Assert.Single(loaded);
        Assert.Equal(second.Id, remaining.Id);
    }

    [Fact]
    public async Task ConcurrentSavesDoNotLoseProfilesWithinOneRepositoryInstance()
    {
        var repository = new JsonConnectionProfileRepository(FilePath);
        var profiles = Enumerable.Range(0, 20)
            .Select(index => TestProfile.Create($"Connection {index}"))
            .ToArray();

        await Task.WhenAll(profiles.Select(profile => repository.SaveAsync(profile)));

        Assert.Equal(profiles.Length, (await repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task MalformedJsonFailsClosed()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{ this is not valid json");
        var repository = new JsonConnectionProfileRepository(FilePath);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => repository.GetAllAsync());
    }

    [Fact]
    public async Task DuplicateProfileIdentifiersFailClosed()
    {
        Directory.CreateDirectory(_directory);
        var duplicateId = Guid.NewGuid();
        var profiles = new[]
        {
            TestProfile.Create("First", duplicateId),
            TestProfile.Create("Second", duplicateId)
        };
        await File.WriteAllTextAsync(FilePath, System.Text.Json.JsonSerializer.Serialize(profiles));
        var repository = new JsonConnectionProfileRepository(FilePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetAllAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
