namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitRepositoryRef(Guid Id, string Name, TeamProjectRef? Project);
