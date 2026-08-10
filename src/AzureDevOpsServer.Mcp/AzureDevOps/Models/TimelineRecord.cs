namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record TimelineRecord(
    Guid Id,
    Guid? ParentId,
    string? Type,
    string? Name,
    string? State,
    string? Result,
    int? ErrorCount,
    int? WarningCount,
    BuildLogRef? Log,
    IReadOnlyList<TimelineIssue>? Issues);