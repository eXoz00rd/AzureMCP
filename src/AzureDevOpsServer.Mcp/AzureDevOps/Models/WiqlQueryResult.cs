namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WiqlQueryResult(IReadOnlyList<WorkItemReference> WorkItems);
