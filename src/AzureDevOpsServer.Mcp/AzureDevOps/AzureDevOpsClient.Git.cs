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
        int top,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/refs?filter=heads/&$top={top}&api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<GitRef>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<GitFileContent> GetFileContentAsync(
        string repository,
        string path,
        string? branch,
        string? project,
        int maxChars,
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

        var item = await response.Content.ReadFromJsonAsync<GitItem>(cancellationToken) ??
            throw new AzureDevOpsClientException($"The response for item '{path}' could not be parsed.");

        var content = item.Content ?? string.Empty;
        if (IsBinaryContent(content))
        {
            return new GitFileContent(
                item.Path,
                null,
                content.Length,
                false,
                true
            );
        }

        var (text, truncated) = Limit(content, maxChars);
        return new GitFileContent(
            item.Path,
            text,
            content.Length,
            truncated,
            false
        );
    }

    public async Task<IReadOnlyList<GitCommit>> GetCommitsAsync(
        string repository,
        string? branch,
        string? itemPath,
        int top,
        string? project,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/commits?searchCriteria.$top={top}&api-version={_options.Value.ApiVersion}";
        if (!string.IsNullOrWhiteSpace(branch))
        {
            requestUri += $"&searchCriteria.itemVersion.version={Uri.EscapeDataString(ShortBranchName(branch))}";
        }

        if (!string.IsNullOrWhiteSpace(itemPath))
        {
            requestUri += $"&searchCriteria.itemPath={Uri.EscapeDataString(itemPath)}";
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<GitCommit>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<GitCommitDetails> GetCommitAsync(
        string repository,
        string commitId,
        string? project,
        CancellationToken cancellationToken)
    {
        var commitPath =
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/commits/{Uri.EscapeDataString(commitId)}";

        using var commitResponse = await _httpClient.GetAsync(
            $"{commitPath}?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(commitResponse, cancellationToken);

        var commit = await commitResponse.Content.ReadFromJsonAsync<GitCommit>(cancellationToken) ??
            throw new AzureDevOpsClientException($"The response for commit {commitId} could not be parsed.");

        using var changesResponse = await _httpClient.GetAsync(
            $"{commitPath}/changes?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(changesResponse, cancellationToken);

        var changes = await changesResponse.Content.ReadFromJsonAsync<GitCommitChangesResult>(cancellationToken);
        return new GitCommitDetails(commit, changes?.Changes ?? []);
    }

    public async Task<LimitedList<GitTreeItem>> GetRepositoryItemsAsync(
        string repository,
        string? path,
        string? branch,
        bool recursive,
        string? project,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var scopePath = string.IsNullOrWhiteSpace(path) ?
            "/" :
            path;
        var recursionLevel = recursive ?
            "full" :
            "oneLevel";
        var requestUri =
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/items" +
            $"?scopePath={Uri.EscapeDataString(scopePath)}&recursionLevel={recursionLevel}&api-version={_options.Value.ApiVersion}";
        if (!string.IsNullOrWhiteSpace(branch))
        {
            requestUri +=
                $"&versionDescriptor.version={Uri.EscapeDataString(ShortBranchName(branch))}&versionDescriptor.versionType=branch";
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<GitTreeItem>>(cancellationToken);
        var items = result?.Value ?? [];
        return items.Count <= maxItems ?
            new LimitedList<GitTreeItem>(items, false) :
            new LimitedList<GitTreeItem>(items.Take(maxItems).ToList(), true);
    }

    public async Task<GitDiffs> GetBranchDiffAsync(
        string repository,
        string baseBranch,
        string targetBranch,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/diffs/commits" +
            $"?baseVersion={Uri.EscapeDataString(ShortBranchName(baseBranch))}&targetVersion={Uri.EscapeDataString(ShortBranchName(targetBranch))}&api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var diffs = await response.Content.ReadFromJsonAsync<GitDiffs>(cancellationToken);
        return diffs ??
            throw new AzureDevOpsClientException("The branch diff response could not be parsed.");
    }

    private static string ShortBranchName(string branch)
    {
        return branch.StartsWith("refs/heads/", StringComparison.Ordinal) ?
            branch["refs/heads/".Length..] :
            branch;
    }
}
