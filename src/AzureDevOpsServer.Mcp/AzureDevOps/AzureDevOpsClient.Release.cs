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
    public async Task<IReadOnlyList<ReleaseDefinition>> GetReleaseDefinitionsAsync(
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/release/definitions?api-version={ApiVersion(ApiArea.Release)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<ReleaseDefinition>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<Release>> GetReleasesAsync(
        string? project,
        int? definitionId,
        int top,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"{Scope(RequireProject(project))}_apis/release/releases?api-version={ApiVersion(ApiArea.Release)}&$top={top}";
        if (definitionId is not null)
        {
            requestUri += $"&definitionId={definitionId}";
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<Release>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<Release> GetReleaseAsync(
        string? project,
        int releaseId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/release/releases/{releaseId}?api-version={ApiVersion(ApiArea.Release)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var release = await response.Content.ReadFromJsonAsync<Release>(cancellationToken);
        return release ??
            throw new AzureDevOpsClientException($"The response for release {releaseId} could not be parsed.");
    }

    public async Task<Release> CreateReleaseAsync(
        string? project,
        int definitionId,
        string? description,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(RequireProject(project))}_apis/release/releases?api-version={ApiVersion(ApiArea.Release)}",
            new { definitionId, description },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var release = await response.Content.ReadFromJsonAsync<Release>(cancellationToken);
        return release ??
            throw new AzureDevOpsClientException("The create release response could not be parsed.");
    }
}
