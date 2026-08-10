namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record QueryHierarchyItem(
    Guid Id,
    string Name,
    string? Path,
    bool? IsFolder,
    IReadOnlyList<QueryHierarchyItem>? Children);