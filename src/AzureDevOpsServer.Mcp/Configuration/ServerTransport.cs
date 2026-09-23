namespace AzureDevOpsServer.Mcp.Configuration;

public enum ServerTransport
{
    Stdio,
    Http
}

public static class ServerTransports
{
    private static readonly Dictionary<string, ServerTransport> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stdio"] = ServerTransport.Stdio,
        ["http"] = ServerTransport.Http
    };

    public static ServerTransport Resolve(string? requested)
    {
        if (TryResolve(requested, out var transport))
        {
            return transport;
        }

        throw new InvalidOperationException(
            $"{AzureDevOpsServerOptions.TransportVariable} has an unknown value: {requested}. " +
            $"Valid values are: {string.Join(", ", Registry.Keys)}."
        );
    }

    public static bool TryResolve(string? requested, out ServerTransport transport)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            transport = ServerTransport.Stdio;
            return true;
        }

        return Registry.TryGetValue(requested.Trim(), out transport);
    }
}
