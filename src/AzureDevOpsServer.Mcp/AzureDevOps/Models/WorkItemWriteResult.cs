namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WorkItemWriteResult(int Id, int Rev, IReadOnlyList<string> ChangedFields, bool Success);
