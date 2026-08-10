namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record ProjectDetails(
    Guid Id,
    string Name,
    string? Description,
    string State,
    string? Visibility,
    string? Url,
    ProjectCapabilities? Capabilities);
