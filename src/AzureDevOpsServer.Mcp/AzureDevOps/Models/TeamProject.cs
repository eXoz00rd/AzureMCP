namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record TeamProject(
    Guid Id,
    string Name,
    string? Description,
    string State,
    string Url);