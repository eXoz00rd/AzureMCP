namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record PullRequestReviewer(
    string? DisplayName,
    string? UniqueName,
    int Vote,
    bool? IsRequired,
    bool? IsContainer);
