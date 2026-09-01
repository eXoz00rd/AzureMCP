using System.Text.Json;

namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WorkItem(
    int Id,
    int Rev,
    IReadOnlyDictionary<string, JsonElement> Fields,
    string Url,
    IReadOnlyList<WorkItemRelation>? Relations);
