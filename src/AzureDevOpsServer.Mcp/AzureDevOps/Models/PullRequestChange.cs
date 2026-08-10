namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record PullRequestChange(string ChangeType, PullRequestChangeItem Item);