namespace AzureDevOpsServer.Mcp.AzureDevOps;

internal static class ExceptionChain
{
    // HttpClient reports a failed connection as an HttpRequestException and keeps the cause underneath it.
    public static TException? Inner<TException>(Exception? exception)
        where TException : Exception
    {
        for (var current = exception?.InnerException; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }
}
