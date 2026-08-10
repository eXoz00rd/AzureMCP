namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitChange(string ChangeType, GitChangeItem Item);