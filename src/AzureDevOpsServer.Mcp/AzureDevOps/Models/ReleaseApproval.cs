namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record ReleaseApproval(
    int Id,
    string? Status,
    string? ApprovalType,
    IdentityRef? Approver,
    ReleaseShallowReference? Release,
    ReleaseShallowReference? ReleaseEnvironment,
    string? Comments);
