namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record ClassificationNode(
    string Name,
    string? Path,
    ClassificationNodeAttributes? Attributes,
    IReadOnlyList<ClassificationNode>? Children);
