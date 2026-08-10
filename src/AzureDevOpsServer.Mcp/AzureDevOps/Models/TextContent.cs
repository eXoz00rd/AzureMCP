namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record TextContent(string Content, int TotalChars, bool Truncated);
