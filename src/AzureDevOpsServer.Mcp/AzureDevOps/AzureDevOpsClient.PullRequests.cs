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
    public async Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(
        string repository,
        string? project,
        string? status,
        int top,
        CancellationToken cancellationToken)
    {
        var effectiveStatus = string.IsNullOrWhiteSpace(status) ?
            "active" :
            status;
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullrequests?searchCriteria.status={Uri.EscapeDataString(effectiveStatus)}&$top={top}&api-version={_options.Value.ApiVersion}",
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
}
