namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record Build(
    int Id,
    string BuildNumber,
    string Status,
    string? Result,
    string SourceBranch,
    BuildDefinition? Definition,
    DateTimeOffset? QueueTime,
    DateTimeOffset? FinishTime);
