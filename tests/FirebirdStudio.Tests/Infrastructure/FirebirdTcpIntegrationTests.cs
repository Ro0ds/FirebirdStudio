using System.Globalization;
using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
using FirebirdStudio.Application.Connections;
using FirebirdStudio.Application.Sql;
using FirebirdStudio.Core.Connections;
using FirebirdStudio.Core.Database;
using FirebirdStudio.Infrastructure.Connections;

namespace FirebirdStudio.Tests.Infrastructure;

public sealed class FirebirdTcpIntegrationTests
{
    private const int FirebirdOperationCancelled = 335544794;

    public static bool IsEnabled =>
        Environment.GetEnvironmentVariable("FIREBIRD_TEST_VERSION") is not null;

    [Fact(Skip = "Set FIREBIRD_TEST_VERSION to run against a dedicated TCP/IP server.",
        SkipUnless = nameof(IsEnabled))]
    [Trait("Category", "FirebirdIntegration")]
    public async Task ExercisesRealServerOverTcp()
    {
        var version = GetRequiredSetting("FIREBIRD_TEST_VERSION");
        Assert.Contains(version, new[] { "5.0", "4.0", "3.0", "2.5" });
        var directory = GetRequiredSetting("FIREBIRD_TEST_DIRECTORY");
        // Interpret the directory using the server's conventions, including when
        // the test runner and Firebird server are on different operating systems.
        var absoluteWindowsPath = directory.StartsWith(@"\\", StringComparison.Ordinal) ||
            (directory.Length >= 3 && char.IsAsciiLetter(directory[0]) &&
                directory[1] == ':' && directory[2] is '\\' or '/');
        Assert.True(directory.StartsWith('/') || absoluteWindowsPath,
            "FIREBIRD_TEST_DIRECTORY must be an absolute path on the Firebird server.");
        var separator = directory.Contains('/') ? '/' : '\\';
        var profile = new ConnectionProfile
        {
            Name = "Firebird Studio TCP integration",
            ServerType = ConnectionServerType.Remote,
            Host = Environment.GetEnvironmentVariable("FIREBIRD_TEST_HOST") ?? "127.0.0.1",
            Port = int.Parse(Environment.GetEnvironmentVariable("FIREBIRD_TEST_PORT") ?? "3050",
                CultureInfo.InvariantCulture),
            UserName = Environment.GetEnvironmentVariable("FIREBIRD_TEST_USER") ?? "SYSDBA",
            Database = $"{directory.TrimEnd('/', '\\')}{separator}studio-it-{Guid.NewGuid():N}.fdb",
            Pooling = false
        };
        var password = GetRequiredSetting("FIREBIRD_TEST_PASSWORD");
        var connectionString = FirebirdConnectionStringFactory.Build(profile, password);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var ct = timeout.Token;

        await FirebirdDatabaseCreator.CreateAsync(profile, password, ct);
        try
        {
            await using var sessions = new FirebirdSessionManager();
            var session = await sessions.ConnectAsync(profile, password, ct);
            Assert.True(session.Succeeded, session.Message);
            Assert.NotNull(session.Session);
            Assert.Matches($@"\bV{Regex.Escape(version)}\.", session.Session.ServerVersion);
            TestContext.Current.TestOutputHelper?.WriteLine(session.Session.ServerVersion);
            Assert.Equal(profile.Id, Assert.Single(sessions.GetActiveSessions()).ProfileId);

            Task<SqlExecutionResult> Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null) =>
                sessions.ExecuteSqlAsync(profile.Id, sql, ct, parameters);

            await Execute("CREATE TABLE STUDIO_IT (ID INTEGER NOT NULL PRIMARY KEY, LABEL VARCHAR(40))");
            await Execute("CREATE EXCEPTION STUDIO_IT_FAILURE 'forced integration rollback'");

            var categories = await sessions.GetMetadataAsync(profile.Id, ct);
            Assert.Contains(categories, item => item.Name == "Tables");
            var tables = await sessions.GetMetadataPageAsync(profile.Id, "Tables", 0, 20, ct);
            Assert.Contains(tables, item => item.Name == "STUDIO_IT");
            var search = await sessions.SearchMetadataAsync(profile.Id, "studio_it", ct, "Tables");
            Assert.Equal("STUDIO_IT", Assert.Single(search).Name);
            var systemTables = await sessions.SearchMetadataAsync(profile.Id, "rdb$relations", ct,
                DatabaseMetadataObject.SystemTablesKind);
            Assert.Contains(systemTables, item => item.Name == "RDB$RELATIONS");
            var details = await sessions.GetMetadataObjectDetailsAsync(profile.Id, "Tables", "STUDIO_IT", ct);
            var columns = details.Where(item => item.Kind == "Column").ToArray();
            Assert.Equal(new[] { "ID", "LABEL" }, columns.Select(item => item.Name));
            var constraints = Assert.Single(details, item => item.Kind == "Constraint group").Children;
            Assert.NotNull(constraints);
            var ddl = FirebirdDdlPreview.BuildTableDdl("STUDIO_IT", columns, constraints);
            Assert.Contains("CREATE TABLE", ddl);
            Assert.Contains("PRIMARY KEY", ddl);

            await Execute("INSERT INTO STUDIO_IT (ID, LABEL) VALUES (@id, @label)",
                new Dictionary<string, object?> { ["id"] = 7, ["label"] = "ação" });

            await using var observer = new FbConnection(connectionString);
            await observer.OpenAsync(ct);
            Assert.Equal("ação", await ReadLabelAsync(observer, 7, ct));

            var error = await Assert.ThrowsAsync<FbException>(() => Execute(
                """
                EXECUTE BLOCK AS
                BEGIN
                    INSERT INTO STUDIO_IT (ID, LABEL) VALUES (8, 'rolled back');
                    EXCEPTION STUDIO_IT_FAILURE;
                END
                """));
            Assert.Contains("forced integration rollback", error.Message);
            Assert.Null(await ReadLabelAsync(observer, 8, ct));

            await VerifyRunningCancellationAsync(sessions, profile.Id, observer, ct);
            var result = await Execute("SELECT LABEL FROM STUDIO_IT WHERE ID = @id",
                new Dictionary<string, object?> { ["id"] = 7 });
            Assert.Equal("ação", Assert.Single(Assert.Single(result.Rows)));
            await sessions.DisconnectAsync(profile.Id, ct);
            Assert.Empty(sessions.GetActiveSessions());
        }
        finally
        {
            FbConnection.DropDatabase(connectionString);
        }
    }

    private static async Task VerifyRunningCancellationAsync(
        FirebirdSessionManager sessions, Guid profileId, FbConnection observer, CancellationToken ct)
    {
        var attachment = await sessions.ExecuteSqlAsync(profileId,
            "SELECT CURRENT_CONNECTION FROM RDB$DATABASE", ct);
        var attachmentId = long.Parse(Assert.Single(Assert.Single(attachment.Rows)), CultureInfo.InvariantCulture);

        await sessions.ExecuteSqlAsync(profileId, "CREATE TABLE STUDIO_IT_CANCEL (ID INTEGER NOT NULL)", ct);
        await sessions.ExecuteSqlAsync(profileId,
            """
            EXECUTE BLOCK AS
                DECLARE VARIABLE I INTEGER = 1;
            BEGIN
                WHILE (I <= 256) DO
                BEGIN
                    INSERT INTO STUDIO_IT_CANCEL (ID) VALUES (:I);
                    I = I + 1;
                END
            END
            """, ct);

        using var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var query = Task.Run(() => sessions.ExecuteSqlAsync(profileId,
            """
            SELECT SUM(CAST(a.ID AS BIGINT) * b.ID * c.ID * d.ID)
            FROM STUDIO_IT_CANCEL a CROSS JOIN STUDIO_IT_CANCEL b
            CROSS JOIN STUDIO_IT_CANCEL c CROSS JOIN STUDIO_IT_CANCEL d
            """, queryCancellation.Token), ct);
        try
        {
            using var observationTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            observationTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            await using var active = new FbCommand(
                "SELECT COUNT(*) FROM MON$STATEMENTS WHERE MON$ATTACHMENT_ID = @id AND MON$STATE = 1",
                observer);
            active.Parameters.AddWithValue("@id", attachmentId);
            while (Convert.ToInt32(await active.ExecuteScalarAsync(observationTimeout.Token),
                       CultureInfo.InvariantCulture) == 0)
            {
                Assert.False(query.IsCompleted, "The query ended before Firebird reported it as active.");
                await Task.Delay(50, observationTimeout.Token);
            }

            await queryCancellation.CancelAsync();
            var exception = await Record.ExceptionAsync(() => query.WaitAsync(TimeSpan.FromSeconds(15), ct));
            Assert.True(exception is OperationCanceledException ||
                exception is FbException { ErrorCode: FirebirdOperationCancelled },
                $"Expected Firebird cancellation, got {exception?.GetType().Name ?? "successful completion"}.");
        }
        finally
        {
            if (!query.IsCompleted)
            {
                // Unblock session disposal if the provider failed to cancel the statement.
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var stop = new FbCommand(
                    "DELETE FROM MON$STATEMENTS WHERE MON$ATTACHMENT_ID = @id", observer);
                stop.Parameters.AddWithValue("@id", attachmentId);
                await stop.ExecuteNonQueryAsync(cleanupTimeout.Token);
                await Record.ExceptionAsync(() => query.WaitAsync(cleanupTimeout.Token));
            }
        }
    }

    private static async Task<string?> ReadLabelAsync(FbConnection observer, int id, CancellationToken ct)
    {
        await using var command = new FbCommand("SELECT LABEL FROM STUDIO_IT WHERE ID = @id", observer);
        command.Parameters.AddWithValue("@id", id);
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    private static string GetRequiredSetting(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Set {name} to run the Firebird TCP integration test.");
}
