using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using System.Net.Http.Json;

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
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullrequests?searchCriteria.status={Uri.EscapeDataString(effectiveStatus)}&$top={top}&api-version={ApiVersion(ApiArea.Git)}",
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
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullrequests/{pullRequestId}?api-version={ApiVersion(ApiArea.Git)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var pullRequest = await response.Content.ReadFromJsonAsync<GitPullRequest>(cancellationToken);
        return pullRequest ??
            throw new AzureDevOpsClientException($"The response for pull request {pullRequestId} could not be parsed.");
    }

    public async Task<WorkItem> LinkPullRequestToWorkItemAsync(
        string repository,
        int pullRequestId,
        int workItemId,
        string? project,
        string? comment,
        CancellationToken cancellationToken)
    {
        var pullRequest = await GetPullRequestAsync(repository, pullRequestId, project, cancellationToken);
        if (pullRequest.Repository is not { } repositoryRef || repositoryRef.Project is not { } projectRef)
        {
            throw new AzureDevOpsClientException(
                $"Pull request {pullRequestId} did not return its repository and project ids, so the artifact link cannot be built."
            );
        }

        return await AddWorkItemRelationAsync(
            workItemId,
            ArtifactLinks.Relation,
            ArtifactLinks.PullRequestUrl(projectRef.Id, repositoryRef.Id, pullRequestId),
            ArtifactLinks.PullRequestName,
            comment,
            cancellationToken
        );
    }

    public async Task<GitPullRequest> CreatePullRequestAsync(
        string repository,
        string sourceBranch,
        string targetBranch,
        string title,
        string? description,
        bool? isDraft,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullrequests?api-version={ApiVersion(ApiArea.Git)}",
            new
            {
                sourceRefName = ToRefName(sourceBranch),
                targetRefName = ToRefName(targetBranch),
                title,
                description,
                isDraft = isDraft ?? false
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
            $"{pullRequestPath}/iterations?api-version={ApiVersion(ApiArea.Git)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(iterationsResponse, cancellationToken);

        var iterations =
            await iterationsResponse.Content.ReadFromJsonAsync<ListResult<PullRequestIteration>>(cancellationToken);
        var latestIteration = iterations?.Value.MaxBy(iteration => iteration.Id);
        if (latestIteration is null)
        {
            return [];
        }

        using var changesResponse = await _httpClient.GetAsync(
            $"{pullRequestPath}/iterations/{latestIteration.Id}/changes?api-version={ApiVersion(ApiArea.Git)}",
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
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion(ApiArea.Git)}",
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
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion(ApiArea.Git)}",
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
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/reviewers/{userId}?api-version={ApiVersion(ApiArea.Git)}",
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
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}?api-version={ApiVersion(ApiArea.Git)}",
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

    public async Task<IReadOnlyList<GitPullRequest>> GetProjectPullRequestsAsync(
        string? project,
        string? status,
        bool createdByMe,
        bool assignedToMe,
        int top,
        CancellationToken cancellationToken)
    {
        var effectiveStatus = string.IsNullOrWhiteSpace(status) ?
            "active" :
            status;
        var requestUri =
            $"{Scope(RequireProject(project))}_apis/git/pullrequests?searchCriteria.status={Uri.EscapeDataString(effectiveStatus)}&$top={top}&api-version={ApiVersion(ApiArea.Git)}";

        if (createdByMe || assignedToMe)
        {
            var userId = await GetAuthenticatedUserIdAsync(cancellationToken);
            if (createdByMe)
            {
                requestUri += $"&searchCriteria.creatorId={userId}";
            }

            if (assignedToMe)
            {
                requestUri += $"&searchCriteria.reviewerId={userId}";
            }
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<GitPullRequest>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<PolicyEvaluation>> GetPullRequestPolicyEvaluationsAsync(
        string repository,
        int pullRequestId,
        string? project,
        CancellationToken cancellationToken)
    {
        var pullRequest = await GetPullRequestAsync(repository, pullRequestId, project, cancellationToken);
        var projectId = pullRequest.Repository?.Project?.Id ??
            throw new AzureDevOpsClientException(
                $"The project of pull request {pullRequestId} could not be resolved, so its policies cannot be evaluated."
            );

        var artifactId = $"vstfs:///CodeReview/CodeReviewId/{projectId}/{pullRequestId}";
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/policy/evaluations?artifactId={Uri.EscapeDataString(artifactId)}&api-version={ApiVersion(ApiArea.Git)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<PolicyEvaluation>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<ResourceRef>> GetPullRequestWorkItemsAsync(
        string repository,
        int pullRequestId,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/workitems?api-version={ApiVersion(ApiArea.Git)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<ResourceRef>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<PullRequestComment> ReplyToPullRequestThreadAsync(
        string repository,
        int pullRequestId,
        int threadId,
        string comment,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/threads/{threadId}/comments?api-version={ApiVersion(ApiArea.Git)}",
            new { content = comment, commentType = 1 },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<PullRequestComment>(cancellationToken);
        return created ??
            throw new AzureDevOpsClientException("The reply response could not be parsed.");
    }

    public async Task<PullRequestComment> UpdatePullRequestCommentAsync(
        string repository,
        int pullRequestId,
        int threadId,
        int commentId,
        string content,
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PatchAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/threads/{threadId}/comments/{commentId}?api-version={ApiVersion(ApiArea.Git)}",
            new { content },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var updated = await response.Content.ReadFromJsonAsync<PullRequestComment>(cancellationToken);
        return updated ??
            throw new AzureDevOpsClientException($"The update response for comment {commentId} could not be parsed.");
    }

    public async Task<PullRequestThread> SetPullRequestThreadStatusAsync(
        string repository,
        int pullRequestId,
        int threadId,
        string status,
        string? project,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = status.ToLowerInvariant() switch
        {
            "active" => "active",
            "fixed" => "fixed",
            "wontfix" => "wontFix",
            "closed" => "closed",
            "bydesign" => "byDesign",
            "pending" => "pending",
            _ => throw new AzureDevOpsClientException(
                "Thread status must be one of: active, fixed, wontFix, closed, byDesign, pending."
            )
        };

        using var response = await _httpClient.PatchAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/threads/{threadId}?api-version={ApiVersion(ApiArea.Git)}",
            new { status = normalizedStatus },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var thread = await response.Content.ReadFromJsonAsync<PullRequestThread>(cancellationToken);
        return thread ??
            throw new AzureDevOpsClientException($"The update response for thread {threadId} could not be parsed.");
    }

    public async Task<GitPullRequest> UpdatePullRequestAsync(
        string repository,
        int pullRequestId,
        string? title,
        string? description,
        bool? autoComplete,
        bool? isDraft,
        string? project,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            payload["title"] = title;
        }

        if (description is not null)
        {
            payload["description"] = description;
        }

        if (autoComplete is not null)
        {
            payload["autoCompleteSetBy"] = autoComplete.Value ?
                new Dictionary<string, object?> { ["id"] = await GetAuthenticatedUserIdAsync(cancellationToken) } :
                null;
        }

        if (isDraft is not null)
        {
            payload["isDraft"] = isDraft.Value;
        }

        if (payload.Count == 0)
        {
            throw new AzureDevOpsClientException(
                "At least one of title, description, autoComplete, or isDraft is required."
            );
        }

        using var response = await _httpClient.PatchAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}?api-version={ApiVersion(ApiArea.Git)}",
            payload,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var pullRequest = await response.Content.ReadFromJsonAsync<GitPullRequest>(cancellationToken);
        return pullRequest ??
            throw new AzureDevOpsClientException(
                $"The update response for pull request {pullRequestId} could not be parsed."
            );
    }

    public async Task<PullRequestReviewer> AddPullRequestReviewerAsync(
        string repository,
        int pullRequestId,
        string reviewer,
        bool isRequired,
        string? project,
        CancellationToken cancellationToken)
    {
        var reviewerId = await ResolveIdentityIdAsync(reviewer, cancellationToken);

        using var response = await _httpClient.PutAsJsonAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/reviewers/{reviewerId}?api-version={ApiVersion(ApiArea.Git)}",
            new { vote = 0, isRequired },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var added = await response.Content.ReadFromJsonAsync<PullRequestReviewer>(cancellationToken);
        return added ??
            throw new AzureDevOpsClientException("The add reviewer response could not be parsed.");
    }

    public async Task RemovePullRequestReviewerAsync(
        string repository,
        int pullRequestId,
        string reviewer,
        string? project,
        CancellationToken cancellationToken)
    {
        var reviewerId = await ResolveIdentityIdAsync(reviewer, cancellationToken);

        using var response = await _httpClient.DeleteAsync(
            $"{Scope(project)}_apis/git/repositories/{Uri.EscapeDataString(repository)}/pullRequests/{pullRequestId}/reviewers/{reviewerId}?api-version={ApiVersion(ApiArea.Git)}",
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<string> ResolveIdentityIdAsync(
        string reviewer,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(reviewer, out var reviewerId))
        {
            return reviewerId.ToString();
        }

        if (string.Equals(reviewer, "me", StringComparison.OrdinalIgnoreCase))
        {
            return (await GetAuthenticatedUserIdAsync(cancellationToken)).ToString();
        }

        using var response = await _httpClient.GetAsync(
            $"_apis/identities?searchFilter=General&filterValue={Uri.EscapeDataString(reviewer)}&queryMembership=None&api-version={ApiVersion(ApiArea.Core)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var identities = await response.Content.ReadFromJsonAsync<ListResult<IdentityRecord>>(cancellationToken);
        return identities?.Value is [var match, ..] ?
            match.Id.ToString() :
            throw new AzureDevOpsClientException(
                $"The reviewer '{reviewer}' could not be resolved to an identity. Pass the identity id, a unique account name, or 'me' for the signed-in user."
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
