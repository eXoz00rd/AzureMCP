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
    public async Task<WiqlQueryResult> QueryWorkItemsAsync(
        string wiql,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(project)}_apis/wit/wiql?api-version={_options.Value.ApiVersion}",
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
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"_apis/wit/workitems/{id}?{FieldsOrRelations(fields)}&api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var workItem = await response.Content.ReadFromJsonAsync<WorkItem>(cancellationToken);
        return workItem ??
            throw new AzureDevOpsClientException($"The response for work item {id} could not be parsed.");
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        IReadOnlyList<int> ids,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            throw new AzureDevOpsClientException("At least one work item id is required.");
        }

        using var response = await _httpClient.GetAsync(
            $"_apis/wit/workitems?ids={string.Join(',', ids)}&{FieldsOrRelations(fields)}&api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<WorkItem>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<QueryHierarchyItem>> GetQueriesAsync(
        string? project,
        int depth,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wit/queries?$depth={depth}&api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/wit/wiql/{Uri.EscapeDataString(queryId)}?api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/wit/workitemtypes?api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/wit/workitemtypes/{Uri.EscapeDataString(type)}/states?api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/wit/classificationnodes/{group}?$depth={depth}&api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version={_options.Value.ApiVersion}",
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
            $"_apis/wit/workitems/{id}?api-version={_options.Value.ApiVersion}"
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
