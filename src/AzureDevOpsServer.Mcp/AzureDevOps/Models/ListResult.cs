namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record ListResult<T>(int Count, IReadOnlyList<T> Value);
