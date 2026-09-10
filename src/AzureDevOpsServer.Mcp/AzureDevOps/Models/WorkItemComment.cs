namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WorkItemComment(
    int Id,
    string? Text,
    int? WorkItemId,
    int? Version,
    IdentityRef? CreatedBy,
    DateTimeOffset? CreatedDate,
    IdentityRef? ModifiedBy,
    DateTimeOffset? ModifiedDate,
    bool? IsDeleted,
    IReadOnlyList<WorkItemCommentMention>? Mentions);
