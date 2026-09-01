namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitTreeItem(string Path, bool? IsFolder, string? GitObjectType);
