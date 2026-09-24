namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WikiPageContent(string Path, string? Content, int TotalChars, int TotalLines, bool Truncated);
