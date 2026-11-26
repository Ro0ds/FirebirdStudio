using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.Loader;
using FirebirdSql.Data.FirebirdClient;

namespace FirebirdStudio.Infrastructure.Connections;

internal static class FirebirdDbConnectionFactory
{
    private const string ProviderResource = "FirebirdStudio.FirebirdClient.dll";
    private static readonly ConcurrentDictionary<string, Lazy<Type>> EmbeddedProviders = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static DbConnection Create(string connectionString)
    {
        var options = new FbConnectionStringBuilder(connectionString);
        if (options.ServerType != FbServerType.Embedded)
            return new FbConnection(connectionString);

        var provider = EmbeddedProviders.GetOrAdd(options.ClientLibrary,
            library => new Lazy<Type>(() => LoadEmbeddedProvider(library))).Value;
        return (DbConnection)Activator.CreateInstance(provider, connectionString)!;
    }

    private static Type LoadEmbeddedProvider(string library)
    {
        // The provider caches native entry-point availability by symbol name. Each library
        // needs a separate provider context so queries keep working after switching engines.
        var context = new AssemblyLoadContext($"Firebird embedded: {library}");
        using var image = typeof(FirebirdDbConnectionFactory).Assembly.GetManifestResourceStream(ProviderResource)
            ?? throw new InvalidOperationException("The embedded Firebird provider resource is missing.");
        return context.LoadFromStream(image).GetType(typeof(FbConnection).FullName!, throwOnError: true)!;
    }
}
