namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record Release(
    int Id,
    string Name,
    string Status,
    DateTimeOffset? CreatedOn,
    ReleaseDefinition? ReleaseDefinition,
    IReadOnlyList<ReleaseEnvironment>? Environments);
