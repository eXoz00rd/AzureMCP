using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed partial class AzureDevOpsClient
{
    private static readonly JsonSerializerOptions CommentJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WiqlQueryResult> QueryWorkItemsAsync(
        string wiql,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(project)}_apis/wit/wiql?api-version={ApiVersion(ApiArea.WorkItems)}",
            new { query = wiql },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<WiqlQueryResult>(cancellationToken);
        return result?.WorkItems is null ?
            new WiqlQueryResult([]) :
            result;
    }

    public async Task<WorkItem> GetWorkItemAsync(
        int id,
        IReadOnlyList<string>? fields,
        bool includeRelations,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"_apis/wit/workitems/{id}?{FieldsOrRelations(fields, includeRelations)}&api-version={ApiVersion(ApiArea.WorkItems)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var workItem = await response.Content.ReadFromJsonAsync<WorkItem>(cancellationToken);
        return workItem is null ?
            throw new AzureDevOpsClientException($"The response for work item {id} could not be parsed.") :
            includeRelations ? ApplyFieldFilter(workItem, fields) : workItem;
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        IReadOnlyList<int> ids,
        IReadOnlyList<string>? fields,
        bool includeRelations,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            throw new AzureDevOpsClientException("At least one work item id is required.");
        }

        using var response = await _httpClient.GetAsync(
            $"_apis/wit/workitems?ids={string.Join(',', ids)}&{FieldsOrRelations(fields, includeRelations)}&api-version={ApiVersion(ApiArea.WorkItems)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<WorkItem>>(cancellationToken);
        return result?.Value is null ?
            [] :
            includeRelations ? result.Value.Select(workItem => ApplyFieldFilter(workItem, fields)).ToList() : result.Value;
    }

    // $expand=relations returns every field regardless of the fields list, so a narrowed field
    // list combined with includeRelations is applied client-side after the fetch. Not called when
    // includeRelations is false: the server's own fields= query already narrows that response.
    private static WorkItem ApplyFieldFilter(WorkItem workItem, IReadOnlyList<string>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return workItem;
        }

        var filteredFields = new Dictionary<string, JsonElement>(fields.Count);
        foreach (var field in fields)
        {
            if (workItem.Fields.TryGetValue(field, out var value))
            {
                filteredFields[field] = value;
            }
        }

        return workItem with { Fields = filteredFields };
    }

    public async Task<WorkItemCommentList> GetWorkItemCommentsAsync(
        int id,
        string? project,
        int top,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wit/workItems/{id}/comments?$top={top}&api-version={_options.Value.WorkItemCommentsApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var comments = await response.Content.ReadFromJsonAsync<WorkItemCommentList>(cancellationToken);
        return comments ??
            throw new AzureDevOpsClientException($"The comments response for work item {id} could not be parsed.");
    }

    public async Task<WorkItemComment> GetWorkItemCommentAsync(
        int id,
        int commentId,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wit/workItems/{id}/comments/{commentId}?api-version={_options.Value.WorkItemCommentsApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadCommentAsync(response, id, cancellationToken);
    }

    public async Task<WorkItemComment> AddWorkItemCommentAsync(
        int id,
        string? project,
        string text,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(RequireProject(project))}_apis/wit/workItems/{id}/comments?api-version={_options.Value.WorkItemCommentsApiVersion}",
            new { text },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadCommentAsync(response, id, cancellationToken);
    }

    // The comments API returns the identifier as "id" on list and get responses, but as
    // "commentId" (with no "id" member) on add and update responses. A model bound to "id"
    // alone would report 0 for those, so an id of 0 falls back to reading "commentId" directly.
    private static async Task<WorkItemComment> ReadCommentAsync(
        HttpResponseMessage response,
        int workItemId,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        WorkItemComment? comment;
        try
        {
            comment = JsonSerializer.Deserialize<WorkItemComment>(json, CommentJsonOptions);
        }
        catch (JsonException)
        {
            comment = null;
        }

        if (comment is null)
        {
            throw new AzureDevOpsClientException($"The comment response for work item {workItemId} could not be parsed.");
        }

        if (comment.Id == 0)
        {
            using var document = JsonDocument.Parse(json);
            comment = document.RootElement.TryGetProperty("commentId", out var commentIdElement) &&
                commentIdElement.TryGetInt32(out var commentId) ?
                comment with { Id = commentId } :
                throw new AzureDevOpsClientException(
                    $"The comment response for work item {workItemId} did not include an id."
                );
        }

        return comment.WorkItemId is null ?
            comment with { WorkItemId = workItemId } :
            comment;
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemRevisionsAsync(
        int id,
        int top,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"_apis/wit/workItems/{id}/revisions?$top={top}&api-version={ApiVersion(ApiArea.WorkItems)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<WorkItem>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<WorkItem> AddWorkItemRelationAsync(
        int id,
        string relation,
        string targetUrl,
        string? attributeName,
        string? comment,
        CancellationToken cancellationToken)
    {
        var attributes = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(attributeName))
        {
            attributes["name"] = attributeName;
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            attributes["comment"] = comment;
        }

        var value = new Dictionary<string, object?>
        {
            ["rel"] = relation,
            ["url"] = targetUrl
        };
        if (attributes.Count > 0)
        {
            value["attributes"] = attributes;
        }

        var operations = new[]
        {
            new Dictionary<string, object?>
            {
                ["op"] = "add",
                ["path"] = "/relations/-",
                ["value"] = value
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(operations),
            Encoding.UTF8,
            "application/json-patch+json"
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"_apis/wit/workitems/{id}?api-version={ApiVersion(ApiArea.WorkItems)}"
        )
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var workItem = await response.Content.ReadFromJsonAsync<WorkItem>(cancellationToken);
        return workItem ??
            throw new AzureDevOpsClientException($"The relation response for work item {id} could not be parsed.");
    }

    public async Task<WorkItem> AddWorkItemAttachmentAsync(
        int id,
        string fileName,
        string content,
        string? comment,
        string? project,
        CancellationToken cancellationToken)
    {
        using var uploadContent = new StringContent(content, Encoding.UTF8, "application/octet-stream");
        using var uploadResponse = await _httpClient.PostAsync(
            $"{Scope(RequireProject(project))}_apis/wit/attachments?fileName={Uri.EscapeDataString(fileName)}&api-version={ApiVersion(ApiArea.WorkItems)}",
            uploadContent,
            cancellationToken
        );

        await EnsureSuccessAsync(uploadResponse, cancellationToken);

        var attachment =
            await uploadResponse.Content.ReadFromJsonAsync<WorkItemAttachmentReference>(cancellationToken) ??
            throw new AzureDevOpsClientException("The attachment upload response could not be parsed.");

        return await AddWorkItemRelationAsync(
            id,
            "AttachedFile",
            attachment.Url,
            null,
            comment ?? fileName,
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<QueryHierarchyItem>> GetQueriesAsync(
        string? project,
        int depth,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wit/queries?$depth={depth}&api-version={ApiVersion(ApiArea.WorkItems)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<QueryHierarchyItem>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<WiqlQueryResult> RunSavedQueryAsync(
        string? project,
        string queryId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wit/wiql/{Uri.EscapeDataString(queryId)}?api-version={ApiVersion(ApiArea.WorkItems)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<WiqlQueryResult>(cancellationToken);
        return result?.WorkItems is null ?
            new WiqlQueryResult([]) :
            result;
    }

    public async Task<IReadOnlyList<WorkItemType>> GetWorkItemTypesAsync(
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wit/workitemtypes?api-version={ApiVersion(ApiArea.WorkItems)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<WorkItemType>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<WorkItemState>> GetWorkItemStatesAsync(
        string? project,
        string type,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wit/workitemtypes/{Uri.EscapeDataString(type)}/states?api-version={ApiVersion(ApiArea.WorkItems)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<WorkItemState>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<ClassificationNode> GetClassificationNodesAsync(
        string? project,
        string group,
        int depth,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wit/classificationnodes/{group}?$depth={depth}&api-version={ApiVersion(ApiArea.WorkItems)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var node = await response.Content.ReadFromJsonAsync<ClassificationNode>(cancellationToken);
        return node ??
            throw new AzureDevOpsClientException($"The {group} classification response could not be parsed.");
    }

    public async Task<WorkItem> CreateWorkItemAsync(
        string? project,
        string type,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var content = CreateJsonPatchContent(fields);
        using var response = await _httpClient.PostAsync(
            $"{Scope(RequireProject(project))}_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version={ApiVersion(ApiArea.WorkItems)}",
            content,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var workItem = await response.Content.ReadFromJsonAsync<WorkItem>(cancellationToken);
        return workItem ??
            throw new AzureDevOpsClientException("The create work item response could not be parsed.");
    }

    public async Task<WorkItem> UpdateWorkItemAsync(
        int id,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"_apis/wit/workitems/{id}?api-version={ApiVersion(ApiArea.WorkItems)}"
        )
        {
            Content = CreateJsonPatchContent(fields)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var workItem = await response.Content.ReadFromJsonAsync<WorkItem>(cancellationToken);
        return workItem ??
            throw new AzureDevOpsClientException($"The update response for work item {id} could not be parsed.");
    }
}
