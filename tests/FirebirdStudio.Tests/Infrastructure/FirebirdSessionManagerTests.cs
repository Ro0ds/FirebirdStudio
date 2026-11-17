using FirebirdSql.Data.FirebirdClient;
using FirebirdStudio.Infrastructure.Connections;
using FirebirdStudio.Core.Database;
using FirebirdStudio.Infrastructure.Connections.Provider;

namespace FirebirdStudio.Tests.Infrastructure;

public sealed class FirebirdSessionManagerTests
{
    [Fact]
    public async Task ConnectRejectsDuplicateProfileAndDisconnectDisposesConnection()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();

        var connected = await manager.ConnectAsync(profile, "secret");
        var duplicate = await manager.ConnectAsync(profile, "secret");

        Assert.True(connected.Succeeded);
        Assert.Equal("5.0.0", connected.Session!.ServerVersion);
        Assert.False(duplicate.Succeeded);
        Assert.Single(manager.GetActiveSessions());
        Assert.Single(factory.CreatedConnections);

        await manager.DisconnectAsync(profile.Id);

        Assert.Empty(manager.GetActiveSessions());
        Assert.True(factory.CreatedConnections[0].IsDisposed);
    }

    [Fact]
    public async Task FailedOpenDisposesConnectionAndRedactsPassword()
    {
        var factory = new FakeConnectionFactory
        {
            OpenException = new InvalidOperationException("Authentication failed for password=secret-value")
        };
        await using var manager = new FirebirdSessionManager(factory);

        var result = await manager.ConnectAsync(TestProfile.Create(), "secret-value");

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.False(result.Message.Contains("secret-value", StringComparison.Ordinal));
        Assert.True(result.Message.Contains("[REDACTED]", StringComparison.Ordinal));
        Assert.NotNull(result.Diagnostics);
        Assert.False(result.Diagnostics!.Contains("secret-value", StringComparison.Ordinal));
        Assert.Contains("InvalidOperationException", result.Diagnostics, StringComparison.Ordinal);
        Assert.Empty(manager.GetActiveSessions());
        Assert.True(Assert.Single(factory.CreatedConnections).IsDisposed);
    }


    [Fact]
    public async Task ProviderConstructionFailureReturnsFailureWithoutCreatingSession()
    {
        var factory = new FakeConnectionFactory
        {
            CreateException = new InvalidOperationException("provider construction failed")
        };
        await using var manager = new FirebirdSessionManager(factory);

        var result = await manager.ConnectAsync(TestProfile.Create(), "secret");

        Assert.False(result.Succeeded);
        Assert.Contains("provider construction failed", result.Message, StringComparison.Ordinal);
        Assert.Empty(factory.CreatedConnections);
        Assert.Empty(manager.GetActiveSessions());
    }

    [Fact]
    public async Task FailedOpenReportsCleanupFailureWithoutLeakingPassword()
    {
        var factory = new FakeConnectionFactory
        {
            OpenException = new InvalidOperationException("authentication failed: secret-value"),
            DisposeException = new IOException("cleanup failed for secret-value")
        };
        await using var manager = new FirebirdSessionManager(factory);

        var result = await manager.ConnectAsync(TestProfile.Create(), "secret-value");

        Assert.False(result.Succeeded);
        Assert.False(result.Message.Contains("secret-value", StringComparison.Ordinal));
        Assert.Contains("Connection cleanup also failed", result.Message, StringComparison.Ordinal);
        Assert.NotNull(result.Diagnostics);
        Assert.False(result.Diagnostics!.Contains("secret-value", StringComparison.Ordinal));
        Assert.Contains("Connection cleanup failure", result.Diagnostics, StringComparison.Ordinal);
        Assert.Empty(manager.GetActiveSessions());
    }

    [Fact]
    public async Task CancellationDuringOpenDisposesConnectionAndPropagatesCancellation()
    {
        var factory = new FakeConnectionFactory { BlockOpenUntilCancelled = true };
        await using var manager = new FirebirdSessionManager(factory);
        using var cancellation = new CancellationTokenSource();

        var connectTask = manager.ConnectAsync(TestProfile.Create(), "secret", cancellation.Token);
        await factory.OpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connectTask);
        Assert.True(Assert.Single(factory.CreatedConnections).IsDisposed);
        Assert.Empty(manager.GetActiveSessions());
    }

    [Fact]
    public async Task ManagerDisposalAttemptsEverySessionEvenWhenOneDisposeFails()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        await manager.ConnectAsync(TestProfile.Create("First"), "secret");
        await manager.ConnectAsync(TestProfile.Create("Second"), "secret");
        factory.CreatedConnections[0].DisposeException = new IOException("synthetic dispose failure");

        var exception = await Assert.ThrowsAsync<AggregateException>(() => manager.DisposeAsync().AsTask());

        Assert.Single(exception.InnerExceptions);
        Assert.Equal(1, factory.CreatedConnections[0].DisposeAttempts);
        Assert.False(factory.CreatedConnections[0].IsDisposed);
        Assert.Equal(1, factory.CreatedConnections[1].DisposeAttempts);
        Assert.True(factory.CreatedConnections[1].IsDisposed);
        Assert.Empty(manager.GetActiveSessions());
    }


    [Fact]
    public async Task FailedDisconnectKeepsSessionVisibleForRetry()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();
        await manager.ConnectAsync(profile, "secret");
        var connection = Assert.Single(factory.CreatedConnections);
        connection.DisposeException = new IOException("synthetic close failure");

        await Assert.ThrowsAsync<IOException>(() => manager.DisconnectAsync(profile.Id));

        Assert.Single(manager.GetActiveSessions());
        Assert.Equal(1, connection.DisposeAttempts);
        Assert.False(connection.IsDisposed);
        connection.DisposeException = null;
    }

    [Fact]
    public async Task ActiveSessionUsesConfiguredPooling()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create(pooling: true);

        await manager.ConnectAsync(profile, "secret");

        var connectionString = Assert.Single(factory.ConnectionStrings);
        var builder = new FbConnectionStringBuilder(connectionString);
        Assert.True(Convert.ToBoolean(builder["Pooling"]));
    }

    [Fact]
    public async Task MetadataCannotBeReadWithoutAnActiveSession()
    {
        await using var manager = new FirebirdSessionManager(new FakeConnectionFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.GetMetadataAsync(TestProfile.Create().Id));
    }

    [Fact]
    public async Task MetadataIsReadThroughTheActiveSession()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();
        var column = new DatabaseMetadataObject(
            "Column", "CUSTOMER_ID", "CUSTOMER", "Domain: D_INTEGER; type code: 8; length: 4; not null");
        var keyColumn = new DatabaseMetadataObject(
            "Constraint column", "CUSTOMER_ID", "CUSTOMER", "Key position: 0");
        var primaryKey = new DatabaseMetadataObject(
            "Constraint", "PK_CUSTOMER", "CUSTOMER", "Type: PRIMARY KEY; index: PK_CUSTOMER", [keyColumn]);
        var constraintGroup = new DatabaseMetadataObject(
            "Constraint group", "Constraints (1)", "CUSTOMER", "Primary keys, unique keys, foreign keys and check constraints", [primaryKey]);
        var expected = new DatabaseMetadataObject(
            "Tables", "CUSTOMER", null, "1 column(s); 1 constraint(s)", [column, constraintGroup]);
        factory.Metadata = [expected];
        await manager.ConnectAsync(profile, "secret");

        var actual = await manager.GetMetadataAsync(profile.Id);

        var actualTable = Assert.Single(actual);
        Assert.Equal(expected, actualTable);
        var actualConstraintGroup = Assert.Single(actualTable.Children!.Where(child => child.Kind == "Constraint group"));
        var actualPrimaryKey = Assert.Single(actualConstraintGroup.Children!);
        Assert.Equal("PK_CUSTOMER", actualPrimaryKey.Name);
        Assert.Equal("CUSTOMER_ID", Assert.Single(actualPrimaryKey.Children!).Name);
        Assert.Single(factory.CreatedConnections);
        Assert.False(factory.CreatedConnections[0].IsDisposed);
    }

    [Fact]
    public async Task MetadataIncludesExpandableStandaloneIndexSegments()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();
        var firstSegment = new DatabaseMetadataObject(
            "Index segment", "CUSTOMER_ID", "IDX_CUSTOMER", "Position: 0; selectivity: 0.001");
        var secondSegment = new DatabaseMetadataObject(
            "Index segment", "ACTIVE", "IDX_CUSTOMER", "Position: 1; selectivity: 0.5");
        factory.Metadata =
        [
            new DatabaseMetadataObject(
                "Indexes", "IDX_CUSTOMER", "CUSTOMER", "Segments: 2; unique: 0; inactive: 0; 2 segment(s)",
                [firstSegment, secondSegment])
        ];
        await manager.ConnectAsync(profile, "secret");

        var actual = await manager.GetMetadataAsync(profile.Id);

        var index = Assert.Single(actual);
        Assert.Equal("IDX_CUSTOMER", index.Name);
        Assert.Equal("CUSTOMER_ID", index.Children![0].Name);
        Assert.Equal("ACTIVE", index.Children[1].Name);
        Assert.Contains("Position: 0", index.Children[0].Details);
    }

    [Fact]
    public async Task MetadataPageIsReadThroughTheActiveSession()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();
        factory.Metadata =
        [
            new DatabaseMetadataObject("Domains", "D_FIRST", null, "type: 8"),
            new DatabaseMetadataObject("Domains", "D_SECOND", null, "type: 8")
        ];
        await manager.ConnectAsync(profile, "secret");

        var page = await manager.GetMetadataPageAsync(profile.Id, "Domains", 1, 1);

        Assert.Equal("D_SECOND", Assert.Single(page).Name);
    }

    [Fact]
    public async Task LazyMetadataDetailsAndCompletionMetadataAreForwardedThroughTheActiveSession()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();
        var expected = new DatabaseMetadataObject("Column", "CUSTOMER_ID", "CUSTOMER", "");
        factory.Metadata = [expected];
        await manager.ConnectAsync(profile, "secret");

        Assert.Equal(expected, Assert.Single(await manager.GetMetadataObjectDetailsAsync(profile.Id, "Tables", "CUSTOMER")));
        Assert.Equal(expected, Assert.Single(await manager.GetSqlCompletionMetadataAsync(profile.Id)));
        Assert.Equal(expected, Assert.Single(await manager.GetSqlCompletionDomainsAsync(profile.Id)));
        var page = await manager.GetTableDataPageAsync(profile.Id, "CUSTOMER", 200, 200);
        Assert.Equal(200, page.Offset);
        Assert.True(page.HasPrevious);
        Assert.False(page.HasNext);
        Assert.False(page.HasStableOrder);

        object?[] cursor = [42];
        var previousPage = await manager.GetTableDataPageAsync(
            profile.Id, "CUSTOMER", 0, 200, cursor, previous: true);
        Assert.True(previousPage.HasStableOrder);
        Assert.True(previousPage.HasPrevious);
        Assert.Equal(42, Assert.Single(previousPage.FirstKeyValues!));
    }

    [Fact]
    public async Task SqlExecutionIsForwardedThroughTheActiveSession()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();
        await manager.ConnectAsync(profile, "secret");

        var result = await manager.ExecuteSqlAsync(profile.Id, "UPDATE CUSTOMER SET ACTIVE = 1");

        Assert.Equal(1, result.RowsAffected);
    }

    [Fact]
    public async Task MetadataPreservesExpandableProcedureParametersAndSource()
    {
        var factory = new FakeConnectionFactory();
        await using var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();
        var input = new DatabaseMetadataObject(
            "Procedure parameter", "P_CUSTOMER_ID", "P_CUSTOMER",
            "Input; position: 0; domain: D_INTEGER; type code: 8; subtype: 0; length: 4 byte(s); scale: 0; not null");
        var output = new DatabaseMetadataObject(
            "Procedure parameter", "O_CUSTOMER_NAME", "P_CUSTOMER",
            "Output; position: 0; domain: D_VARCHAR_100; type code: 37; subtype: 0; length: 100 byte(s); scale: 0; nullable");
        var parameterGroup = new DatabaseMetadataObject(
            "Procedure parameter group", "Parameters (2)", "P_CUSTOMER",
            "Input and output parameters ordered by direction and position", [input, output]);
        var source = new DatabaseMetadataObject(
            "Procedure source", "Source", "P_CUSTOMER", "AS BEGIN SUSPEND; END");
        factory.Metadata =
        [
            new DatabaseMetadataObject(
                "Procedures", "P_CUSTOMER", null,
                "Input parameters: 1; output parameters: 1; 2 detail group(s)", [parameterGroup, source])
        ];
        await manager.ConnectAsync(profile, "secret");

        var actual = Assert.Single(await manager.GetMetadataAsync(profile.Id));
        Assert.Equal("Procedures", actual.Kind);
        Assert.Equal("P_CUSTOMER", actual.Name);
        var actualParameterGroup = Assert.Single(actual.Children!.Where(item => item.Kind == "Procedure parameter group"));
        Assert.Equal(2, actualParameterGroup.Children!.Count);
        Assert.Contains("Input", actualParameterGroup.Children[0].Details, StringComparison.Ordinal);
        Assert.Contains("Output", actualParameterGroup.Children[1].Details, StringComparison.Ordinal);
        Assert.Equal("AS BEGIN SUSPEND; END", Assert.Single(actual.Children.Where(item => item.Kind == "Procedure source")).Details);
    }

    [Fact]
    public async Task ManagerSearchesMetadataOnTheSelectedSession()
    {
        var expected = new DatabaseMetadataObject("Tables", "CUSTOMER", null, "User table");
        var column = new DatabaseMetadataObject("Column", "CUSTOMER_ID", "CUSTOMER", "Column in table: CUSTOMER");
        var factory = new FakeConnectionFactory { Metadata = [expected, column] };
        var manager = new FirebirdSessionManager(factory);
        var profile = TestProfile.Create();
        await manager.ConnectAsync(profile, "secret");

        var actual = await manager.SearchMetadataAsync(profile.Id, "customer", kindFilter: "Tables");

        Assert.Equal(expected, Assert.Single(actual));
        Assert.Equal(column, Assert.Single(await manager.SearchMetadataAsync(profile.Id, "customer_id", kindFilter: "Column")));
        Assert.Empty(await manager.SearchMetadataAsync(profile.Id, "missing"));
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task ManagerCannotReconnectAfterDisposal()
    {
        var manager = new FirebirdSessionManager(new FakeConnectionFactory());
        await manager.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => manager.ConnectAsync(TestProfile.Create(), "secret"));
    }

    private sealed class FakeConnectionFactory : IFirebirdProviderConnectionFactory
    {
        public List<FakeConnection> CreatedConnections { get; } = [];
        public List<string> ConnectionStrings { get; } = [];
        public Exception? CreateException { get; init; }
        public Exception? OpenException { get; init; }
        public Exception? DisposeException { get; init; }
        public bool BlockOpenUntilCancelled { get; init; }
        public IReadOnlyList<DatabaseMetadataObject> Metadata { get; set; } = [];
        public TaskCompletionSource OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IFirebirdProviderConnection Create(string connectionString)
        {
            if (CreateException is not null)
                throw CreateException;

            ConnectionStrings.Add(connectionString);
            var connection = new FakeConnection(this);
            CreatedConnections.Add(connection);
            return connection;
        }
    }

    private sealed class FakeConnection(FakeConnectionFactory owner) : IFirebirdProviderConnection
    {
        public bool IsDisposed { get; private set; }
        public int DisposeAttempts { get; private set; }
        public Exception? DisposeException { get; set; } = owner.DisposeException;
        public string ServerVersion => "5.0.0";

        public Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(owner.Metadata);
        }

        public Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataPageAsync(
            string category, int offset, int pageSize, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DatabaseMetadataObject> page = owner.Metadata
                .Where(item => item.Kind == category).Skip(offset).Take(pageSize).ToArray();
            return Task.FromResult(page);
        }

        public Task<IReadOnlyList<DatabaseMetadataObject>> SearchMetadataAsync(
            string searchTerm, string? kindFilter, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DatabaseMetadataObject> matches = owner.Metadata
                .Where(item => item.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Where(item => kindFilter is null || item.Kind == kindFilter)
                .ToArray();
            return Task.FromResult(matches);
        }

        public Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataObjectDetailsAsync(
            string kind, string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(owner.Metadata);
        }

        public Task<IReadOnlyList<DatabaseMetadataObject>> ReadSqlCompletionMetadataAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(owner.Metadata);
        }

        public Task<IReadOnlyList<DatabaseMetadataObject>> ReadSqlCompletionDomainsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(owner.Metadata);
        }

        public Task<FirebirdStudio.Application.Connections.DatabaseTableDataPage> ReadTableDataPageAsync(
            string tableName, int offset, int pageSize, IReadOnlyList<object?>? cursorValues,
            bool previous, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new FirebirdStudio.Application.Connections.DatabaseTableDataPage(
                Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), offset, pageSize,
                offset > 0 || cursorValues is not null, false, cursorValues is not null,
                cursorValues, cursorValues));
        }

        public Task<FirebirdStudio.Application.Connections.SqlExecutionResult> ExecuteSqlAsync(
            string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new FirebirdStudio.Application.Connections.SqlExecutionResult(
                Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), 1, false, TimeSpan.Zero));
        }

        public async Task OpenAsync(CancellationToken cancellationToken)
        {
            owner.OpenStarted.TrySetResult();
            if (owner.BlockOpenUntilCancelled)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            if (owner.OpenException is not null)
                throw owner.OpenException;
        }

        public ValueTask DisposeAsync()
        {
            DisposeAttempts++;
            var disposalException = DisposeException;
            if (disposalException is not null)
                return new ValueTask(Task.FromException(disposalException));

            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
