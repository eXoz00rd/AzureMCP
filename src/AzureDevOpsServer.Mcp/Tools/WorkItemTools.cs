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

    [McpServerTool(Name = "query_work_items", ReadOnly = true)]
    [Description(
        "Runs a WIQL query and returns matching work item references. Scopes the query to the given project, the default project, or the whole collection."
    )]
    public Task<WiqlQueryResult> QueryWorkItemsAsync(
        [Description(
            "WIQL query text, for example: SELECT [System.Id] FROM WorkItems WHERE [System.State] = 'Active'."
        )]
        string wiql,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        var effectiveProject = string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
        return _client.QueryWorkItemsAsync(wiql, effectiveProject, cancellationToken);
    }

    [McpServerTool(Name = "get_work_item", ReadOnly = true)]
    [Description("Gets a single work item with all of its fields.")]
    public Task<WorkItem> GetWorkItemAsync(
        [Description("Work item id.")] int id,
        CancellationToken cancellationToken)
    {
        return _client.GetWorkItemAsync(id, cancellationToken);
    }

    [McpServerTool(Name = "get_work_items", ReadOnly = true)]
    [Description("Gets multiple work items by their ids in one call, including fields and relations.")]
    public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        [Description("Work item ids.")] int[] ids,
        CancellationToken cancellationToken)
    {
        return _client.GetWorkItemsAsync(ids, cancellationToken);
    }

    [McpServerTool(Name = "create_work_item", Destructive = false)]
    [Description("Creates a new work item of the given type. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<WorkItem> CreateWorkItemAsync(
        [Description("Work item type, for example Bug, Task, or User Story.")] string type,
        [Description("Title of the new work item.")]
        string title,
        [Description("Optional additional fields as reference name to value pairs, for example System.Description.")]
        Dictionary<string, string>? fields,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        var allFields = new Dictionary<string, string>
        {
            ["System.Title"] = title
        };
        if (fields is not null)
        {
            foreach (var field in fields)
            {
                allFields[field.Key] = field.Value;
            }
        }

        var effectiveProject = string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
        return _client.CreateWorkItemAsync(effectiveProject, type, allFields, cancellationToken);
    }

    [McpServerTool(Name = "update_work_item", Destructive = true)]
    [Description("Updates fields of an existing work item.")]
    public Task<WorkItem> UpdateWorkItemAsync(
        [Description("Work item id.")] int id,
        [Description("Fields to set as reference name to value pairs, for example System.State or System.AssignedTo.")]
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        return _client.UpdateWorkItemAsync(id, fields, cancellationToken);
    }

    [McpServerTool(Name = "add_work_item_comment", Destructive = false)]
    [Description("Adds a comment to the discussion of a work item.")]
    public Task<WorkItem> AddWorkItemCommentAsync(
        [Description("Work item id.")] int id,
        [Description("Comment text.")] string comment,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["System.History"] = comment
        };
        return _client.UpdateWorkItemAsync(id, fields, cancellationToken);
    }
}