namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record LimitedList<T>(IReadOnlyList<T> Items, bool Truncated);
