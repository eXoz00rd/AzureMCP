namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record BuildTimeline(IReadOnlyList<TimelineRecord> Records);