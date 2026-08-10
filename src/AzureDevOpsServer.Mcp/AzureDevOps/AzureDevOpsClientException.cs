namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed class AzureDevOpsClientException : Exception
{
    public AzureDevOpsClientException(string message)
        : base(message)
    {
    }
}