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
    public async Task<IReadOnlyList<TimelineRecord>> GetBuildTimelineAsync(
        string? project,
        int buildId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/build/builds/{buildId}/timeline?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var timeline = await response.Content.ReadFromJsonAsync<BuildTimeline>(cancellationToken);
        return timeline?.Records ?? [];
    }

    public async Task<TextContent> GetBuildLogAsync(
        string? project,
        int buildId,
        int logId,
        int? startLine,
        int? endLine,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"{Scope(RequireProject(project))}_apis/build/builds/{buildId}/logs/{logId}?api-version={_options.Value.ApiVersion}";
        if (startLine is not null)
        {
            requestUri += $"&startLine={startLine}";
        }

        if (endLine is not null)
        {
            requestUri += $"&endLine={endLine}";
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var log = await response.Content.ReadAsStringAsync(cancellationToken);
        var (text, truncated) = Limit(log, maxChars);
        return new TextContent(text, log.Length, truncated);
    }

    public async Task<IReadOnlyList<BuildArtifact>> GetBuildArtifactsAsync(
        string? project,
        int buildId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/build/builds/{buildId}/artifacts?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<BuildArtifact>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<BuildDefinition>> GetBuildDefinitionsAsync(
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/build/definitions?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<BuildDefinition>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<Build>> GetBuildsAsync(
        string? project,
        int? definitionId,
        int top,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"{Scope(RequireProject(project))}_apis/build/builds?api-version={_options.Value.ApiVersion}&$top={top}";
        if (definitionId is not null)
        {
            requestUri += $"&definitions={definitionId}";
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<Build>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<Build> QueueBuildAsync(
        string? project,
        int definitionId,
        string? sourceBranch,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(RequireProject(project))}_apis/build/builds?api-version={_options.Value.ApiVersion}",
            new
            {
                definition = new { id = definitionId },
                sourceBranch = string.IsNullOrWhiteSpace(sourceBranch) ?
                    null :
                    ToRefName(sourceBranch)
            },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var build = await response.Content.ReadFromJsonAsync<Build>(cancellationToken);
        return build ??
            throw new AzureDevOpsClientException("The queue build response could not be parsed.");
    }
}
