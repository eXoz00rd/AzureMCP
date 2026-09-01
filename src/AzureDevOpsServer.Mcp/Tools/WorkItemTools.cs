using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
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

    [McpServerTool(Name = "query_work_items", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Runs a WIQL query and returns matching work item references. Scopes the query to the given project, the default project, or the whole collection."
    )]
    public Task<WiqlQueryResult> QueryWorkItemsAsync(
        [Description(
            "WIQL query text, for example: SELECT [System.Id] FROM WorkItems WHERE [System.State] = 'Active'."
        )]
        string wiql,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.QueryWorkItemsAsync(wiql, EffectiveProject(project), cancellationToken);
    }

    private string? EffectiveProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
    }

    [McpServerTool(Name = "get_work_item", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Gets a single work item. Returns all fields and relations unless a field list is given; prefer a field list to avoid pulling large HTML descriptions."
    )]
    public Task<WorkItem> GetWorkItemAsync(
        [Description("Work item id.")] int id,
        [Description(
            "Optional field reference names to return, for example System.Title and System.State. Relations are only returned when this is omitted."
        )]
        string[]? fields = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetWorkItemAsync(id, fields, cancellationToken);
    }

    [McpServerTool(Name = "get_work_items", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets multiple work items by their ids in one call. Prefer a field list when fetching many items.")]
    public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        [Description("Work item ids.")] int[] ids,
        [Description("Optional field reference names to return. Relations are only returned when this is omitted.")]
        string[]? fields = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetWorkItemsAsync(ids, fields, cancellationToken);
    }

    [McpServerTool(Name = "list_work_item_comments", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the discussion comments of a work item with their authors and dates.")]
    public Task<WorkItemCommentList> ListWorkItemCommentsAsync(
        [Description("Work item id.")] int id,
        [Description("Maximum number of comments to return. Defaults to 100. Valid range 1-1000.")]
        int? top = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetWorkItemCommentsAsync(
            id,
            EffectiveProject(project),
            ResponseLimits.ResolveTop(top),
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_work_item_revisions", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets the revision history of a work item so field changes over time can be compared.")]
    public Task<IReadOnlyList<WorkItem>> GetWorkItemRevisionsAsync(
        [Description("Work item id.")] int id,
        [Description("Maximum number of revisions to return. Defaults to 100. Valid range 1-1000.")]
        int? top = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetWorkItemRevisionsAsync(id, ResponseLimits.ResolveTop(top), cancellationToken);
    }

    [McpServerTool(Name = "link_work_item", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Links a work item to another work item, or to a commit or pull request by its artifact URL. To link a pull request, prefer link_pull_request_to_work_item, which builds the URL itself."
    )]
    public Task<WorkItem> LinkWorkItemAsync(
        [Description("Work item id that receives the link.")] int id,
        [Description(
            "Link kind: parent, child, related, duplicate, predecessor, successor, or a raw relation name such as ArtifactLink."
        )]
        string linkType,
        [Description("Target work item id. Provide this or targetUrl.")]
        int? targetWorkItemId = null,
        [Description(
            "Target URL for artifact links, for example vstfs:///Git/PullRequestId/{projectId}%2F{repositoryId}%2F{pullRequestId}. Provide this or targetWorkItemId."
        )]
        string? targetUrl = null,
        [Description(
            "Artifact link name required by ArtifactLink relations, for example Pull Request, Fixed in Commit, or Integrated in build. Inferred from a vstfs URL when omitted."
        )]
        string? artifactLinkName = null,
        [Description("Optional comment describing the link.")]
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var relation = ParseLinkType(linkType);
        var url = targetUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            if (targetWorkItemId is null)
            {
                throw new McpException("Either targetWorkItemId or targetUrl must be provided.");
            }

            url = $"{_options.Value.CollectionUrl.TrimEnd('/')}/_apis/wit/workItems/{targetWorkItemId}";
        }

        var name = artifactLinkName;
        if (relation == ArtifactLinks.Relation && string.IsNullOrWhiteSpace(name))
        {
            name = ArtifactLinks.NameFor(url) ??
                throw new McpException(
                    $"An {ArtifactLinks.Relation} relation requires artifactLinkName, for example '{ArtifactLinks.PullRequestName}' or '{ArtifactLinks.CommitName}', because '{url}' is not a recognised vstfs artifact URL."
                );
        }

        return _client.AddWorkItemRelationAsync(
            id,
            relation,
            url,
            name,
            comment,
            cancellationToken
        );
    }

    [McpServerTool(Name = "add_work_item_attachment", Destructive = false, UseStructuredContent = true)]
    [Description("Uploads text content as a file and attaches it to a work item, for example a log excerpt or a note.")]
    public Task<WorkItem> AddWorkItemAttachmentAsync(
        [Description("Work item id.")] int id,
        [Description("File name including extension, for example build-log.txt.")]
        string fileName,
        [Description("Text content of the attachment.")]
        string content,
        [Description("Optional comment stored with the attachment.")]
        string? comment = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.AddWorkItemAttachmentAsync(
            id,
            fileName,
            content,
            comment,
            EffectiveProject(project),
            cancellationToken
        );
    }

    internal static string ParseLinkType(string linkType)
    {
        return linkType.ToLowerInvariant() switch
        {
            "parent" => "System.LinkTypes.Hierarchy-Reverse",
            "child" => "System.LinkTypes.Hierarchy-Forward",
            "related" => "System.LinkTypes.Related",
            "duplicate" => "System.LinkTypes.Duplicate-Forward",
            "predecessor" => "System.LinkTypes.Dependency-Reverse",
            "successor" => "System.LinkTypes.Dependency-Forward",
            _ when linkType.Contains('.') || linkType == ArtifactLinks.Relation => linkType,
            _ => throw new McpException(
                "Link type must be parent, child, related, duplicate, predecessor, successor, or a raw relation name."
            )
        };
    }

    [McpServerTool(Name = "create_work_item", Destructive = false, UseStructuredContent = true)]
    [Description("Creates a new work item of the given type. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<WorkItem> CreateWorkItemAsync(
        [Description("Work item type, for example Bug, Task, or User Story.")] string type,
        [Description("Title of the new work item.")]
        string title,
        [Description("Optional additional fields as reference name to value pairs, for example System.Description.")]
        Dictionary<string, string>? fields = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
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

    [McpServerTool(Name = "update_work_item", Destructive = true, UseStructuredContent = true)]
    [Description("Updates fields of an existing work item.")]
    public Task<WorkItem> UpdateWorkItemAsync(
        [Description("Work item id.")] int id,
        [Description("Fields to set as reference name to value pairs, for example System.State or System.AssignedTo.")]
        Dictionary<string, string> fields,
        CancellationToken cancellationToken = default)
    {
        return _client.UpdateWorkItemAsync(id, fields, cancellationToken);
    }

    [McpServerTool(Name = "add_work_item_comment", Destructive = false, UseStructuredContent = true)]
    [Description("Adds a comment to the discussion of a work item.")]
    public Task<WorkItem> AddWorkItemCommentAsync(
        [Description("Work item id.")] int id,
        [Description("Comment text.")] string comment,
        CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["System.History"] = comment
        };
        return _client.UpdateWorkItemAsync(id, fields, cancellationToken);
    }
}
