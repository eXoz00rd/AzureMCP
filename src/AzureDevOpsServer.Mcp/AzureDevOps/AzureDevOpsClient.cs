using System.Net;
using System.Net.Http.Json;
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

            var page = await response.Content.ReadFromJsonAsync<ProjectListResult>(cancellationToken);
            if (page is not null)
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
        var scope = string.IsNullOrWhiteSpace(project) ?
            string.Empty :
            $"{Uri.EscapeDataString(project)}/";

        using var response = await _httpClient.PostAsJsonAsync(
            $"{scope}_apis/wit/wiql?api-version={_options.Value.ApiVersion}",
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