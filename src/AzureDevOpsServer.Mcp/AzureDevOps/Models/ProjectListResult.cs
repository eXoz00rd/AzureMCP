namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record ProjectListResult(int Count, IReadOnlyList<TeamProject> Value);