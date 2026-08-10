namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WorkItemCommentList(int? TotalCount, IReadOnlyList<WorkItemComment>? Comments);
