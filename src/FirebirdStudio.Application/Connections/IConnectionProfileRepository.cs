using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Application.Connections;

public interface IConnectionProfileRepository
{
    Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}
