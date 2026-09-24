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

    public async Task<WikiPageContent> GetWikiPageAsync(
        string wiki,
        string path,
        int? startLine,
        int? endLine,
        int maxChars,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wiki/wikis/{Uri.EscapeDataString(wiki)}/pages?path={Uri.EscapeDataString(path)}&includeContent=true&api-version={ApiVersion(ApiArea.Wiki)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var page = await response.Content.ReadFromJsonAsync<WikiPage>(cancellationToken) ??
            throw new AzureDevOpsClientException($"The response for wiki page '{path}' could not be parsed.");
        if (page.Content is null)
        {
            return new WikiPageContent(page.Path, null, 0, 0, false);
        }

        // The wiki API has no line or size parameters, so the page is read whole and narrowed here.
        var window = TextWindow.Apply(page.Content, startLine, endLine, maxChars);
        return new WikiPageContent(page.Path, window.Text, window.TotalChars, window.TotalLines, window.Truncated);
    }

    public async Task<WikiPageTree> GetWikiPageTreeAsync(
        string wiki,
        int top,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wiki/wikis/{Uri.EscapeDataString(wiki)}/pages?path=%2F&recursionLevel=full&api-version={ApiVersion(ApiArea.Wiki)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var root = await response.Content.ReadFromJsonAsync<WikiPage>(cancellationToken) ??
            throw new AzureDevOpsClientException("The wiki page tree response could not be parsed.");
        return LimitPageTree(root, top);
    }

    // Keeps the first top pages level by level, so a large wiki still shows every top-level section
    // before any deep one. A page is only ever kept together with its parent, because a parent is
    // always reached before its children.
    private static WikiPageTree LimitPageTree(WikiPage root, int top)
    {
        var kept = new HashSet<WikiPage>(ReferenceEqualityComparer.Instance);
        var total = 0;
        var queue = new Queue<WikiPage>();
        queue.Enqueue(root);

        while (queue.TryDequeue(out var page))
        {
            foreach (var child in page.SubPages ?? [])
            {
                total++;
                if (kept.Count < top)
                {
                    kept.Add(child);
                }

                queue.Enqueue(child);
            }
        }

        return new WikiPageTree(root.Path, KeptSubPages(root, kept), total, total > top);
    }

    private static List<WikiPage>? KeptSubPages(WikiPage page, HashSet<WikiPage> kept)
    {
        return page.SubPages?
                   .Where(kept.Contains)
                   .Select(child => child with { SubPages = KeptSubPages(child, kept) })
                   .ToList();
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
