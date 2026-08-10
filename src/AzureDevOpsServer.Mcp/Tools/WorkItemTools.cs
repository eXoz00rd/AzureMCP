using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class WorkItemTools
{
    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public WorkItemTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "query_work_items")]
    [Description("Runs a WIQL query and returns matching work item references. Scopes the query to the given project, the default project, or the whole collection.")]
    public Task<WiqlQueryResult> QueryWorkItemsAsync(
        [Description("WIQL query text, for example: SELECT [System.Id] FROM WorkItems WHERE [System.State] = 'Active'.")]
        string wiql,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        var effectiveProject = string.IsNullOrWhiteSpace(project) ? _options.Value.DefaultProject : project;
        return _client.QueryWorkItemsAsync(wiql, effectiveProject, cancellationToken);
    }

    [McpServerTool(Name = "get_work_item")]
    [Description("Gets a single work item with all of its fields.")]
    public Task<WorkItem> GetWorkItemAsync(
        [Description("Work item id.")]
        int id,
        CancellationToken cancellationToken)
    {
        return _client.GetWorkItemAsync(id, cancellationToken);
    }
}
