namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitUserDate(string? Name, string? Email, DateTimeOffset? Date);
