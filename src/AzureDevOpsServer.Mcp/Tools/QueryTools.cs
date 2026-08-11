using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class QueryTools
{
    private const int DefaultQueryDepth = 2;
    private const int DefaultNodeDepth = 3;

    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public QueryTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_queries", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Lists the saved work item queries of a project as a folder tree. Requires a project name or ADOS_DEFAULT_PROJECT."
    )]
    public Task<IReadOnlyList<QueryHierarchyItem>> ListQueriesAsync(
        [Description("Folder depth to expand. Defaults to 2.")] int? depth,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetQueriesAsync(EffectiveProject(project), depth ?? DefaultQueryDepth, cancellationToken);
    }

    [McpServerTool(Name = "run_saved_query", ReadOnly = true, UseStructuredContent = true)]
    [Description("Runs a saved work item query by its id and returns the matching work item references.")]
    public Task<WiqlQueryResult> RunSavedQueryAsync(
        [Description("Saved query id from list_queries.")] string queryId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.RunSavedQueryAsync(EffectiveProject(project), queryId, cancellationToken);
    }

    [McpServerTool(Name = "list_work_item_types", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the work item types available in a project.")]
    public Task<IReadOnlyList<WorkItemType>> ListWorkItemTypesAsync(
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")] string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetWorkItemTypesAsync(EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_work_item_states", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the valid states of a work item type, for example for Bug or User Story.")]
    public Task<IReadOnlyList<WorkItemState>> ListWorkItemStatesAsync(
        [Description("Work item type name.")] string type,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetWorkItemStatesAsync(EffectiveProject(project), type, cancellationToken);
    }

    [McpServerTool(Name = "list_iterations", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the iteration path tree of a project including sprint start and finish dates.")]
    public Task<ClassificationNode> ListIterationsAsync(
        [Description("Tree depth to expand. Defaults to 3.")] int? depth,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetClassificationNodesAsync(
            EffectiveProject(project),
            "Iterations",
            depth ?? DefaultNodeDepth,
            cancellationToken
        );
    }

    [McpServerTool(Name = "list_areas", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the area path tree of a project.")]
    public Task<ClassificationNode> ListAreasAsync(
        [Description("Tree depth to expand. Defaults to 3.")] int? depth,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetClassificationNodesAsync(
            EffectiveProject(project),
            "Areas",
            depth ?? DefaultNodeDepth,
            cancellationToken
        );
    }

    private string? EffectiveProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
    }
}