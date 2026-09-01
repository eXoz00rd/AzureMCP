namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record PullRequestThread(
    int Id,
    string? Status,
    PullRequestThreadContext? ThreadContext,
    IReadOnlyList<PullRequestComment>? Comments);
