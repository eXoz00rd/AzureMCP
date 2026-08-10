namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitRepository(Guid Id, string Name, string? DefaultBranch, string? RemoteUrl);
