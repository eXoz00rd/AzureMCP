namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitPullRequest(
    int PullRequestId,
    string Title,
    string? Description,
    string Status,
    string SourceRefName,
    string TargetRefName,
    IdentityRef? CreatedBy);