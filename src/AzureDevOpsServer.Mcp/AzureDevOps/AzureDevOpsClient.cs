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

    public async Task<IReadOnlyList<GitTreeItem>> GetRepositoryItemsAsync(
        string repository,
        string? path,
        string? branch,
        bool recursive,
        string? project,
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
        return result?.Value ?? [];
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

    public async Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(
        string repository,
        string? project,
        string? status,
        CancellationToken cancellationToken)
    {
        var effectiveStatus = string.IsNullOrWhiteSpace(status) ?
            "active" :
            status;
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullrequests?searchCriteria.status={Uri.EscapeDataString(effectiveStatus)}&api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<GitPullRequest>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<GitPullRequest> GetPullRequestAsync(
        string repository,
        int pullRequestId,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullrequests/{pullRequestId}?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var pullRequest = await response.Content.ReadFromJsonAsync<GitPullRequest>(cancellationToken);
        return pullRequest ??
            throw new AzureDevOpsClientException($"The response for pull request {pullRequestId} could not be parsed.");
    }

    public async Task<GitPullRequest> CreatePullRequestAsync(
        string repository,
        string sourceBranch,
        string targetBranch,
        string title,
        string? description,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullrequests?api-version={_options.Value.ApiVersion}",
            new
            {
                sourceRefName = ToRefName(sourceBranch),
                targetRefName = ToRefName(targetBranch),
                title,
                description
            },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var pullRequest = await response.Content.ReadFromJsonAsync<GitPullRequest>(cancellationToken);
        return pullRequest ??
            throw new AzureDevOpsClientException("The create pull request response could not be parsed.");
    }

    public async Task<IReadOnlyList<PullRequestChange>> GetPullRequestChangesAsync(
        string repository,
        int pullRequestId,
        string? project,
        CancellationToken cancellationToken)
    {
        var pullRequestPath =
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}";

        using var iterationsResponse = await _httpClient.GetAsync(
            $"{pullRequestPath}/iterations?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(iterationsResponse, cancellationToken);

        var iterations =
            await iterationsResponse.Content.ReadFromJsonAsync<ListResult<PullRequestIteration>>(cancellationToken);
        var latestIteration = iterations?.Value?.MaxBy(iteration => iteration.Id);
        if (latestIteration is null)
        {
            return [];
        }

        using var changesResponse = await _httpClient.GetAsync(
            $"{pullRequestPath}/iterations/{latestIteration.Id}/changes?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(changesResponse, cancellationToken);

        var changes = await changesResponse.Content.ReadFromJsonAsync<PullRequestIterationChanges>(cancellationToken);
        return changes?.ChangeEntries ?? [];
    }

    public async Task<IReadOnlyList<PullRequestThread>> GetPullRequestThreadsAsync(
        string repository,
        int pullRequestId,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/threads?api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<PullRequestThread>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<PullRequestThread> CreatePullRequestThreadAsync(
        string repository,
        int pullRequestId,
        string comment,
        string? filePath,
        int? line,
        string? project,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["comments"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["parentCommentId"] = 0,
                    ["content"] = comment,
                    ["commentType"] = 1
                }
            },
            ["status"] = 1
        };

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var threadContext = new Dictionary<string, object?>
            {
                ["filePath"] = filePath
            };
            if (line is not null)
            {
                var position = new Dictionary<string, object?>
                {
                    ["line"] = line,
                    ["offset"] = 1
                };
                threadContext["rightFileStart"] = position;
                threadContext["rightFileEnd"] = position;
            }

            payload["threadContext"] = threadContext;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/threads?api-version={_options.Value.ApiVersion}",
            payload,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var thread = await response.Content.ReadFromJsonAsync<PullRequestThread>(cancellationToken);
        return thread ??
            throw new AzureDevOpsClientException("The create thread response could not be parsed.");
    }

    public async Task<PullRequestReviewer> SetPullRequestVoteAsync(
        string repository,
        int pullRequestId,
        int vote,
        string? project,
        CancellationToken cancellationToken)
    {
        var userId = await GetAuthenticatedUserIdAsync(cancellationToken);

        using var response = await _httpClient.PutAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/reviewers/{userId}?api-version={_options.Value.ApiVersion}",
            new { vote },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var reviewer = await response.Content.ReadFromJsonAsync<PullRequestReviewer>(cancellationToken);
        return reviewer ??
            throw new AzureDevOpsClientException("The vote response could not be parsed.");
    }

    public async Task<GitPullRequest> UpdatePullRequestStatusAsync(
        string repository,
        int pullRequestId,
        string status,
        string? project,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = status.ToLowerInvariant();
        if (normalizedStatus is not ("active" or "abandoned" or "completed"))
        {
            throw new AzureDevOpsClientException("Status must be one of: active, abandoned, completed.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["status"] = normalizedStatus
        };

        if (normalizedStatus == "completed")
        {
            var pullRequest = await GetPullRequestAsync(repository, pullRequestId, project, cancellationToken);
            var commitId = pullRequest.LastMergeSourceCommit?.CommitId ??
                throw new AzureDevOpsClientException(
                    "The pull request has no merge source commit, so it cannot be completed."
                );
            payload["lastMergeSourceCommit"] = new Dictionary<string, object?>
            {
                ["commitId"] = commitId
            };
        }

        using var response = await _httpClient.PatchAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}?api-version={_options.Value.ApiVersion}",
            payload,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var updated = await response.Content.ReadFromJsonAsync<GitPullRequest>(cancellationToken);
        return updated ??
            throw new AzureDevOpsClientException(
                $"The status update response for pull request {pullRequestId} could not be parsed."
            );
    }

    public async Task<IReadOnlyList<Wiki>> GetWikisAsync(string? project, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/wiki/wikis?api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/wiki/wikis/{Uri.EscapeDataString(wiki)}/pages?path={Uri.EscapeDataString(path)}&includeContent=true&api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/wiki/wikis/{Uri.EscapeDataString(wiki)}/pages?path=%2F&recursionLevel=full&api-version={_options.Value.ApiVersion}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var root = await response.Content.ReadFromJsonAsync<WikiPage>(cancellationToken);
        return root ??
            throw new AzureDevOpsClientException("The wiki page tree response could not be parsed.");
    }

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

    public async Task<string> GetBuildLogAsync(
        string? project,
        int buildId,
        int logId,
        int? startLine,
        int? endLine,
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

        return await response.Content.ReadAsStringAsync(cancellationToken);
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

    public async Task<IReadOnlyList<ReleaseDefinition>> GetReleaseDefinitionsAsync(
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/release/definitions?api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/release/releases?api-version={_options.Value.ApiVersion}&$top={top}";
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
            $"{Scope(RequireProject(project))}_apis/release/releases/{releaseId}?api-version={_options.Value.ApiVersion}",
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
            $"{Scope(RequireProject(project))}_apis/release/releases?api-version={_options.Value.ApiVersion}",
            new { definitionId, description },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var release = await response.Content.ReadFromJsonAsync<Release>(cancellationToken);
        return release ??
            throw new AzureDevOpsClientException("The create release response could not be parsed.");
    }

    private async Task<Guid> GetAuthenticatedUserIdAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            "_apis/connectionData",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var connectionData = await response.Content.ReadFromJsonAsync<ConnectionData>(cancellationToken);
        return connectionData?.AuthenticatedUser?.Id ??
            throw new AzureDevOpsClientException("The authenticated user could not be resolved from connection data.");
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

    private static string RequireProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            throw new AzureDevOpsClientException(
                "A project is required for this operation. Pass a project name or set ADOS_DEFAULT_PROJECT."
            ) :
            project;
    }

    private static string ToRefName(string branch)
    {
        return branch.StartsWith("refs/", StringComparison.Ordinal) ?
            branch :
            $"refs/heads/{branch}";
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