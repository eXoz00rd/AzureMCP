namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitCommitDetails(GitCommit Commit, IReadOnlyList<GitChange> Changes);