namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitCommitChangesResult(IReadOnlyList<GitChange> Changes);