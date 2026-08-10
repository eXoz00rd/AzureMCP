namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitFileContent(
    string Path,
    string? Content,
    int TotalChars,
    bool Truncated,
    bool IsBinary);
