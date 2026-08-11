using ModelContextProtocol;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed class AzureDevOpsClientException : McpException
{
    public AzureDevOpsClientException(string message)
        : base(message)
    {
    }
}
