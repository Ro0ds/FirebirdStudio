using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Application.Connections;

public sealed class ConnectionProfileManager(IConnectionProfileRepository repository)
{
    private readonly IConnectionProfileRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    public async Task SaveAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        var errors = ConnectionProfileValidator.Validate(profile);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(profile));

        await _repository.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A valid profile identifier is required.", nameof(profileId));

        return _repository.DeleteAsync(profileId, cancellationToken);
    }
}
