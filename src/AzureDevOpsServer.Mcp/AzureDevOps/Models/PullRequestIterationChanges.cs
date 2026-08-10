namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record PullRequestIterationChanges(IReadOnlyList<PullRequestChange> ChangeEntries);