namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitPullRequest(
    int PullRequestId,
    string Title,
    string? Description,
    string Status,
    string SourceRefName,
    string TargetRefName,
    IdentityRef? CreatedBy,
    GitCommitRef? LastMergeSourceCommit,
    string? MergeStatus,
    bool? IsDraft,
    DateTimeOffset? CreationDate,
    DateTimeOffset? ClosedDate,
    IReadOnlyList<PullRequestReviewer>? Reviewers,
    GitRepositoryRef? Repository);
