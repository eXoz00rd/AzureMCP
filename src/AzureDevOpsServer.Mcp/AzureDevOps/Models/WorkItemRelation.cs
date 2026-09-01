namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WorkItemRelation(string? Rel, string? Url, WorkItemRelationAttributes? Attributes);
