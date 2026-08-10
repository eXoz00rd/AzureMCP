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
}
