using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FirebirdSql.Data.FirebirdClient;
using FirebirdStudio.Application.Connections;
using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Infrastructure.Connections;

public sealed class FirebirdConnectionTester : IFirebirdConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        var diagnostics = new StringBuilder();
        var total = Stopwatch.StartNew();
        var validationErrors = ConnectionProfileValidator.Validate(profile);
        if (validationErrors.Count > 0)
        {
            diagnostics.AppendLine("[Profile validation] Failed");
            foreach (var error in validationErrors)
                diagnostics.AppendLine($"  - {error}");
            return new ConnectionTestResult(false, "Connection profile validation failed.", Diagnostics: diagnostics.ToString());
        }

        diagnostics.AppendLine("[Profile validation] Passed");
        var currentStage = "DNS resolution";

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(profile.ConnectionTimeoutSeconds));

            if (profile.ServerType == ConnectionServerType.Remote)
            {
                currentStage = "DNS resolution";
                var dnsTimer = Stopwatch.StartNew();
                IPAddress[] addresses;
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(profile.Host, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    diagnostics.AppendLine($"[DNS resolution] Failed after {dnsTimer.ElapsedMilliseconds:N0} ms: {exception.Message}");
                    return Failure("The server host could not be resolved.", diagnostics, total, password, exception);
                }

                diagnostics.AppendLine($"[DNS resolution] Passed in {dnsTimer.ElapsedMilliseconds:N0} ms: {string.Join(", ", addresses.Select(address => address.ToString()))}");
                if (addresses.Length == 0)
                {
                    diagnostics.AppendLine("[TCP reachability] No IP addresses were returned for the host.");
                    return new ConnectionTestResult(false, "The server host did not resolve to any IP addresses.", Diagnostics: diagnostics.ToString());
                }

                currentStage = "TCP reachability";
                var tcpTimer = Stopwatch.StartNew();
                Exception? lastTcpException = null;
                IPAddress? connectedAddress = null;
                foreach (var address in addresses)
                {
                    try
                    {
                        using var client = new TcpClient(address.AddressFamily);
                        await client.ConnectAsync(address, profile.Port, timeout.Token).ConfigureAwait(false);
                        connectedAddress = address;
                        break;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        lastTcpException = exception;
                    }
                }

                if (connectedAddress is null)
                {
                    diagnostics.AppendLine($"[TCP reachability] Failed after {tcpTimer.ElapsedMilliseconds:N0} ms on {profile.Host}:{profile.Port}.");
                    return Failure($"Could not reach {profile.Host}:{profile.Port} over TCP.", diagnostics, total, password, lastTcpException);
                }

                diagnostics.AppendLine($"[TCP reachability] Passed in {tcpTimer.ElapsedMilliseconds:N0} ms via {connectedAddress}:{profile.Port}");
            }
            else
            {
                currentStage = "Embedded client library";
                var libraryPath = profile.ClientLibraryPath;
                var libraryExists = !string.IsNullOrWhiteSpace(libraryPath) && File.Exists(libraryPath);
                diagnostics.AppendLine(libraryExists
                    ? $"[Embedded client library] Found: {libraryPath}"
                    : $"[Embedded client library] The configured file was not found: {libraryPath}");
                if (!libraryExists)
                    return new ConnectionTestResult(false, "The configured embedded client library could not be found.", Diagnostics: diagnostics.ToString());
            }

            currentStage = "Firebird provider handshake and authentication";
            var providerTimer = Stopwatch.StartNew();
            var connectionString = FirebirdConnectionStringFactory.Build(profile, password, poolingOverride: false);
            await using var connection = new FbConnection(connectionString);
            await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
            diagnostics.AppendLine($"[Firebird provider handshake and authentication] Passed in {providerTimer.ElapsedMilliseconds:N0} ms");
            diagnostics.AppendLine($"[Server] Firebird {connection.ServerVersion}");
            diagnostics.AppendLine($"[Total] Completed in {total.ElapsedMilliseconds:N0} ms");

            return new ConnectionTestResult(
                true,
                "Connection established successfully.",
                connection.ServerVersion,
                diagnostics.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            diagnostics.AppendLine($"[{currentStage}] Timed out.");
            diagnostics.AppendLine($"[Connection timeout] The diagnostic exceeded the configured {profile.ConnectionTimeoutSeconds}-second timeout.");
            diagnostics.AppendLine($"[Total] Stopped after {total.ElapsedMilliseconds:N0} ms");
            return new ConnectionTestResult(false, "The connection diagnostic timed out.", Diagnostics: diagnostics.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.AppendLine($"[{currentStage}] Failed");
            diagnostics.AppendLine(SanitizeMessage(exception.ToString(), password));
            diagnostics.AppendLine($"[Total] Failed after {total.ElapsedMilliseconds:N0} ms");
            return new ConnectionTestResult(
                false,
                SanitizeMessage(exception.Message, password),
                Diagnostics: diagnostics.ToString());
        }
    }

    private static ConnectionTestResult Failure(
        string message,
        StringBuilder diagnostics,
        Stopwatch total,
        string password,
        Exception? exception)
    {
        if (exception is not null)
            diagnostics.AppendLine(SanitizeMessage(exception.ToString(), password));
        diagnostics.AppendLine($"[Total] Failed after {total.ElapsedMilliseconds:N0} ms");
        return new ConnectionTestResult(false, message, Diagnostics: diagnostics.ToString());
    }

    internal static string SanitizeMessage(string message, string password) =>
        string.IsNullOrEmpty(password)
            ? message
            : message.Replace(password, "[REDACTED]", StringComparison.Ordinal);
}
