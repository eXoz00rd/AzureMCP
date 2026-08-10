namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record PullRequestComment(
    int Id,
    string? Content,
    IdentityRef? Author,
    DateTimeOffset? PublishedDate);