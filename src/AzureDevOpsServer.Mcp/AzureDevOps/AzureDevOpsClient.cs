using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed class AzureDevOpsClient
{
    private const int MaxErrorBodyLength = 500;
    private const int ProjectPageSize = 100;
    private const string ContinuationTokenHeader = "x-ms-continuationtoken";

    private readonly HttpClient _httpClient;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public AzureDevOpsClient(HttpClient httpClient, IOptions<AzureDevOpsServerOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<TeamProject>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = new List<TeamProject>();
        string? continuationToken = null;

        do
        {
            var requestUri = $"_apis/projects?api-version={_options.Value.ApiVersion}&$top={ProjectPageSize}";
            if (!string.IsNullOrEmpty(continuationToken))
            {
                requestUri += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
            }

            using var response = await _httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<ListResult<TeamProject>>(cancellationToken);
            if (page?.Value is not null)
            {
                projects.AddRange(page.Value);
            }

            continuationToken = response.Headers.TryGetValues(ContinuationTokenHeader, out var values) ?
                values.FirstOrDefault() :
                null;
        } while (!string.IsNullOrEmpty(continuationToken));

        return projects;
    }

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

    public async Task<WorkItem> GetWorkItemAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"_apis/wit/workitems/{id}?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var workItem = await response.Content.ReadFromJsonAsync<WorkItem>(cancellationToken);
        return workItem ??
            throw new AzureDevOpsClientException($"The response for work item {id} could not be parsed.");
    }

    public async Task<WorkItem> CreateWorkItemAsync(
        string? project,
        string type,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            throw new AzureDevOpsClientException(
                "A project is required to create a work item. Pass a project name or set ADOS_DEFAULT_PROJECT."
            );
        }

        using var content = CreateJsonPatchContent(fields);
        using var response = await _httpClient.PostAsync(
            $"{Scope(project)}_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version={_options.Value.ApiVersion}",
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

    public async Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<GitRepository>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<GitRef>> GetBranchesAsync(
        string repository,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/refs?filter=heads/&api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<GitRef>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<GitItem> GetFileContentAsync(
        string repository,
        string path,
        string? branch,
        string? project,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/items" +
            $"?path={Uri.EscapeDataString(path)}&includeContent=true&$format=json&api-version={_options.Value.ApiVersion}";
        if (!string.IsNullOrWhiteSpace(branch))
        {
            requestUri +=
                $"&versionDescriptor.version={Uri.EscapeDataString(branch)}&versionDescriptor.versionType=branch";
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var item = await response.Content.ReadFromJsonAsync<GitItem>(cancellationToken);
        return item ??
            throw new AzureDevOpsClientException($"The response for item '{path}' could not be parsed.");
    }

    private static HttpContent CreateJsonPatchContent(IReadOnlyDictionary<string, string> fields)
    {
        if (fields.Count == 0)
        {
            throw new AzureDevOpsClientException("At least one field is required.");
        }

        var operations = fields
                         .Select(field => new { op = "add", path = $"/fields/{field.Key}", value = field.Value })
                         .ToList();
        return new StringContent(
            JsonSerializer.Serialize(operations),
            Encoding.UTF8,
            "application/json-patch+json"
        );
    }

    private static string Scope(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            string.Empty :
            $"{Uri.EscapeDataString(project)}/";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation)
        {
            throw new AzureDevOpsClientException(
                "Authentication against Azure DevOps Server failed. Verify that the PAT is valid, not expired, and has the required scopes."
            );
        }

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new AzureDevOpsClientException(
            $"Azure DevOps Server request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Truncate(body)}"
        );
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaxErrorBodyLength ?
            value :
            value[..MaxErrorBodyLength];
    }
}