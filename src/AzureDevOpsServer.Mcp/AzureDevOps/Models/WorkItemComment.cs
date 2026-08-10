namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WorkItemComment(
    int Id,
    string? Text,
    IdentityRef? CreatedBy,
    DateTimeOffset? CreatedDate);
