namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitCommit(string CommitId, string? Comment, GitUserDate? Author, GitUserDate? Committer);
