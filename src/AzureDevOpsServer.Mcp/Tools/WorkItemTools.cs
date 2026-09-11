using System.ComponentModel;
using System.Text.Json;
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
    private const string DescriptionFormatHtml = "html";
    private const string DescriptionFormatText = "text";

    // Whether a field is HTML is a property of the field itself, not something safely inferable
    // from its value: a plain field can legitimately contain tag-shaped text (for example a title
    // "Fix <span> rendering"). Restricting conversion to the known HTML field reference names used
    // across the built-in Azure DevOps process templates keeps the "plain fields are returned
    // exactly as sent" guarantee unconditional instead of heuristic.
    private static readonly HashSet<string> RichTextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Description",
        "Microsoft.VSTS.TCM.ReproSteps",
        "Microsoft.VSTS.TCM.SystemInfo",
        "Microsoft.VSTS.Common.AcceptanceCriteria",
        "Microsoft.VSTS.CMMI.Justification",
        "Microsoft.VSTS.CMMI.Symptom",
        "Microsoft.VSTS.CMMI.RootCause"
    };

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

    // Defaults only when the argument is omitted. An explicitly supplied blank value is not a
    // valid 'html' or 'text' and falls through to the rejection below instead of silently
    // defaulting.
    private static string NormalizeDescriptionFormat(string? descriptionFormat)
    {
        if (descriptionFormat is null ||
            string.Equals(descriptionFormat, DescriptionFormatHtml, StringComparison.OrdinalIgnoreCase))
        {
            return DescriptionFormatHtml;
        }

        if (string.Equals(descriptionFormat, DescriptionFormatText, StringComparison.OrdinalIgnoreCase))
        {
            return DescriptionFormatText;
        }

        throw new McpException(
            $"'descriptionFormat' must be '{DescriptionFormatHtml}' or '{DescriptionFormatText}'. Received '{descriptionFormat}'."
        );
    }

    // Only known rich-text fields are converted, so plain fields (titles, states, identities,
    // and any field outside the allowlist) are returned exactly as the server sent them, even
    // when their value happens to contain tag-shaped text.
    private static WorkItem ApplyDescriptionFormat(WorkItem workItem, string descriptionFormat)
    {
        if (descriptionFormat != DescriptionFormatText)
        {
            return workItem;
        }

        var converted = new Dictionary<string, JsonElement>(workItem.Fields.Count);
        foreach (var (name, value) in workItem.Fields)
        {
            // The allowlist alone decides whether a field is converted; a further "does it look
            // like HTML" check would skip decoding an allowlisted value that has no tags at all,
            // for example an entity-only value such as "Fish &amp; Chips".
            converted[name] = RichTextFields.Contains(name) && value.ValueKind == JsonValueKind.String ?
                JsonSerializer.SerializeToElement(HtmlText.ToPlainText(value.GetString()!)) :
                value;
        }

        return workItem with { Fields = converted };
    }

    [McpServerTool(Name = "get_work_item", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Gets a single work item. Returns all fields and relations unless a field list is given; prefer a field list to avoid pulling large HTML descriptions."
    )]
    public async Task<WorkItem> GetWorkItemAsync(
        [Description("Work item id.")] int id,
        [Description(
            "Optional field reference names to return, for example System.Title and System.State. Relations are only returned when this is omitted, or when includeRelations is set."
        )]
        string[]? fields = null,
        [Description("When true, also returns relations even when a field list is given.")]
        bool includeRelations = false,
        [Description(
            "Format for known rich-text fields (System.Description, Repro Steps, System Info, Acceptance Criteria, and the CMMI Justification/Symptom/Root Cause fields): 'html' (default, unchanged) or 'text' (tags stripped, entities decoded). Other fields are always returned as sent."
        )]
        string? descriptionFormat = null,
        CancellationToken cancellationToken = default)
    {
        var format = NormalizeDescriptionFormat(descriptionFormat);
        var workItem = await _client.GetWorkItemAsync(id, fields, includeRelations, cancellationToken);
        return ApplyDescriptionFormat(workItem, format);
    }

    [McpServerTool(Name = "get_work_items", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets multiple work items by their ids in one call. Prefer a field list when fetching many items.")]
    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        [Description("Work item ids.")] int[] ids,
        [Description(
            "Optional field reference names to return. Relations are only returned when this is omitted, or when includeRelations is set."
        )]
        string[]? fields = null,
        [Description("When true, also returns relations even when a field list is given.")]
        bool includeRelations = false,
        [Description(
            "Format for known rich-text fields (System.Description, Repro Steps, System Info, Acceptance Criteria, and the CMMI Justification/Symptom/Root Cause fields): 'html' (default, unchanged) or 'text' (tags stripped, entities decoded). Other fields are always returned as sent."
        )]
        string? descriptionFormat = null,
        CancellationToken cancellationToken = default)
    {
        var format = NormalizeDescriptionFormat(descriptionFormat);
        var workItems = await _client.GetWorkItemsAsync(ids, fields, includeRelations, cancellationToken);
        return workItems.Select(workItem => ApplyDescriptionFormat(workItem, format)).ToList();
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
    public async Task<IReadOnlyList<WorkItem>> GetWorkItemRevisionsAsync(
        [Description("Work item id.")] int id,
        [Description("Maximum number of revisions to return. Defaults to 100. Valid range 1-1000.")]
        int? top = null,
        [Description(
            "Format for known rich-text fields (System.Description, Repro Steps, System Info, Acceptance Criteria, and the CMMI Justification/Symptom/Root Cause fields): 'html' (default, unchanged) or 'text' (tags stripped, entities decoded). Other fields are always returned as sent."
        )]
        string? descriptionFormat = null,
        CancellationToken cancellationToken = default)
    {
        var format = NormalizeDescriptionFormat(descriptionFormat);
        var revisions = await _client.GetWorkItemRevisionsAsync(id, ResponseLimits.ResolveTop(top), cancellationToken);
        return revisions.Select(workItem => ApplyDescriptionFormat(workItem, format)).ToList();
    }

    [McpServerTool(Name = "link_work_item", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Links a work item to another work item, or to a commit or pull request by its artifact URL. To link a pull request, prefer link_pull_request_to_work_item, which builds the URL itself."
    )]
    public async Task<WorkItemWriteResult> LinkWorkItemAsync(
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

        var workItem = await _client.AddWorkItemRelationAsync(
            id,
            relation,
            url,
            name,
            comment,
            cancellationToken
        );
        return new WorkItemWriteResult(workItem.Id, workItem.Rev, [relation], true);
    }

    [McpServerTool(Name = "add_work_item_attachment", Destructive = false, UseStructuredContent = true)]
    [Description("Uploads text content as a file and attaches it to a work item, for example a log excerpt or a note.")]
    public async Task<WorkItemWriteResult> AddWorkItemAttachmentAsync(
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
        var workItem = await _client.AddWorkItemAttachmentAsync(
            id,
            fileName,
            content,
            comment,
            EffectiveProject(project),
            cancellationToken
        );
        return new WorkItemWriteResult(workItem.Id, workItem.Rev, ["AttachedFile"], true);
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
    public async Task<WorkItemWriteResult> CreateWorkItemAsync(
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
        var workItem = await _client.CreateWorkItemAsync(effectiveProject, type, allFields, cancellationToken);
        return new WorkItemWriteResult(workItem.Id, workItem.Rev, allFields.Keys.ToList(), true);
    }

    [McpServerTool(Name = "update_work_item", Destructive = true, UseStructuredContent = true)]
    [Description("Updates fields of an existing work item.")]
    public async Task<WorkItemWriteResult> UpdateWorkItemAsync(
        [Description("Work item id.")] int id,
        [Description("Fields to set as reference name to value pairs, for example System.State or System.AssignedTo.")]
        Dictionary<string, string> fields,
        CancellationToken cancellationToken = default)
    {
        var workItem = await _client.UpdateWorkItemAsync(id, fields, cancellationToken);
        return new WorkItemWriteResult(workItem.Id, workItem.Rev, fields.Keys.ToList(), true);
    }

    [McpServerTool(Name = "add_work_item_comment", Destructive = false, UseStructuredContent = true)]
    [Description("Adds a comment to the discussion of a work item and returns the created comment, including its id.")]
    public Task<WorkItemComment> AddWorkItemCommentAsync(
        [Description("Work item id.")] int id,
        [Description("Comment text.")] string comment,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.AddWorkItemCommentAsync(id, EffectiveProject(project), comment, cancellationToken);
    }

    [McpServerTool(Name = "get_work_item_comment", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets a single discussion comment of a work item by its comment id.")]
    public Task<WorkItemComment> GetWorkItemCommentAsync(
        [Description("Work item id.")] int id,
        [Description("Comment id.")] int commentId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetWorkItemCommentAsync(id, commentId, EffectiveProject(project), cancellationToken);
    }
}
