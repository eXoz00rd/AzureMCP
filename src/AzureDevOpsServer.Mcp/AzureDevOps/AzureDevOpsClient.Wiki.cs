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
    public async Task<IReadOnlyList<Wiki>> GetWikisAsync(string? project, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wiki/wikis?api-version={ApiVersion(ApiArea.Wiki)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<Wiki>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<WikiPage> GetWikiPageAsync(
        string wiki,
        string path,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wiki/wikis/{Uri.EscapeDataString(wiki)}/pages?path={Uri.EscapeDataString(path)}&includeContent=true&api-version={ApiVersion(ApiArea.Wiki)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var page = await response.Content.ReadFromJsonAsync<WikiPage>(cancellationToken);
        return page ??
            throw new AzureDevOpsClientException($"The response for wiki page '{path}' could not be parsed.");
    }

    public async Task<WikiPage> GetWikiPageTreeAsync(
        string wiki,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wiki/wikis/{Uri.EscapeDataString(wiki)}/pages?path=%2F&recursionLevel=full&api-version={ApiVersion(ApiArea.Wiki)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var root = await response.Content.ReadFromJsonAsync<WikiPage>(cancellationToken);
        return root ??
            throw new AzureDevOpsClientException("The wiki page tree response could not be parsed.");
    }

    public async Task<WikiPageUpdate> CreateOrUpdateWikiPageAsync(
        string wiki,
        string path,
        string content,
        string? project,
        CancellationToken cancellationToken)
    {
        var pageUri =
            $"{Scope(RequireProject(project))}_apis/wiki/wikis/{Uri.EscapeDataString(wiki)}/pages?path={Uri.EscapeDataString(path)}&api-version={ApiVersion(ApiArea.Wiki)}";

        var existingVersion = await GetWikiPageVersionAsync(pageUri, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Put, pageUri)
        {
            Content = JsonContent.Create(new { content })
        };
        if (existingVersion is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", existingVersion);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var page = await response.Content.ReadFromJsonAsync<WikiPage>(cancellationToken);
        return new WikiPageUpdate(
            page?.Path ?? path,
            existingVersion is null,
            response.Headers.ETag?.Tag ?? string.Empty
        );
    }

    private async Task<string?> GetWikiPageVersionAsync(string pageUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        return response.Headers.ETag?.Tag;
    }
}
