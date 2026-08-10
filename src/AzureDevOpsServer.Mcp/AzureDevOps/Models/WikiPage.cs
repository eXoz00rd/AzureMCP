namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WikiPage(string Path, string? Content, IReadOnlyList<WikiPage>? SubPages);
