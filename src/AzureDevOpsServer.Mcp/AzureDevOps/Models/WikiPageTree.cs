namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WikiPageTree(string Path, IReadOnlyList<WikiPage>? SubPages, int TotalPages, bool Truncated);
