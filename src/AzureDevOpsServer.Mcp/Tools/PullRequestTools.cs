using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class PullRequestTools
{
    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public PullRequestTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_pull_requests", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists pull requests of a Git repository filtered by status.")]
    public Task<IReadOnlyList<GitPullRequest>> ListPullRequestsAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Optional status filter: active, completed, abandoned, or all. Defaults to active.")]
        string? status = null,
        [Description("Maximum number of pull requests to return. Defaults to 100.")]
        int? top = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetPullRequestsAsync(
            repository,
            EffectiveProject(project),
            status,
            top ?? ResponseLimits.DefaultListTop,
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_pull_request", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets the details of a single pull request.")]
    public Task<GitPullRequest> GetPullRequestAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetPullRequestAsync(repository, pullRequestId, EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "create_pull_request", Destructive = false, UseStructuredContent = true)]
    [Description("Creates a pull request from a source branch to a target branch.")]
    public Task<GitPullRequest> CreatePullRequestAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Source branch name, with or without the refs/heads/ prefix.")]
        string sourceBranch,
        [Description("Target branch name, with or without the refs/heads/ prefix.")]
        string targetBranch,
        [Description("Title of the pull request.")]
        string title,
        [Description("Optional description of the pull request.")]
        string? description = null,
        [Description("Set to true to create the pull request as a draft.")]
        bool? isDraft = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.CreatePullRequestAsync(
            repository,
            sourceBranch,
            targetBranch,
            title,
            description,
            isDraft,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_pull_request_changes", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the files changed in a pull request, based on its latest iteration.")]
    public Task<IReadOnlyList<PullRequestChange>> GetPullRequestChangesAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetPullRequestChangesAsync(
            repository,
            pullRequestId,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "list_pull_request_threads", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the comment threads of a pull request, including file context and authors.")]
    public Task<IReadOnlyList<PullRequestThread>> ListPullRequestThreadsAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetPullRequestThreadsAsync(
            repository,
            pullRequestId,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "add_pull_request_comment", Destructive = false, UseStructuredContent = true)]
    [Description("Adds a comment to a pull request as a new thread, optionally anchored to a file and line.")]
    public Task<PullRequestThread> AddPullRequestCommentAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Comment text.")] string comment,
        [Description("Optional repository file path to anchor the comment to, for example /src/Program.cs.")]
        string? filePath = null,
        [Description("Optional 1-based line number in the file; used only when filePath is set.")]
        int? line = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.CreatePullRequestThreadAsync(
            repository,
            pullRequestId,
            comment,
            filePath,
            line,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "vote_on_pull_request", Destructive = false, UseStructuredContent = true)]
    [Description("Casts the authenticated user's vote on a pull request.")]
    public Task<PullRequestReviewer> VoteOnPullRequestAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Vote: approve, approve_with_suggestions, wait_for_author, reject, or reset.")]
        string vote,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.SetPullRequestVoteAsync(
            repository,
            pullRequestId,
            ParseVote(vote),
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "list_my_pull_requests", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Lists pull requests across all repositories of a project, optionally only those the signed-in user created or reviews. Answers questions like which pull requests are waiting for me."
    )]
    public Task<IReadOnlyList<GitPullRequest>> ListMyPullRequestsAsync(
        [Description("Optional status filter: active, completed, abandoned, or all. Defaults to active.")]
        string? status = null,
        [Description("Set to true to only return pull requests created by the signed-in user.")]
        bool? createdByMe = null,
        [Description("Set to true to only return pull requests where the signed-in user is a reviewer.")]
        bool? assignedToMe = null,
        [Description("Maximum number of pull requests to return. Defaults to 100.")]
        int? top = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetProjectPullRequestsAsync(
            EffectiveProject(project),
            status,
            createdByMe ?? false,
            assignedToMe ?? false,
            top ?? ResponseLimits.DefaultListTop,
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_pull_request_policies", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Gets the policy evaluations of a pull request: required builds, reviewer rules, and linked work item checks with their status. Explains why a pull request cannot be completed."
    )]
    public Task<IReadOnlyList<PolicyEvaluation>> GetPullRequestPoliciesAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetPullRequestPolicyEvaluationsAsync(
            repository,
            pullRequestId,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "list_pull_request_work_items", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Lists the work items linked to a pull request. Use get_work_items with the returned ids for details."
    )]
    public Task<IReadOnlyList<ResourceRef>> ListPullRequestWorkItemsAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetPullRequestWorkItemsAsync(
            repository,
            pullRequestId,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "link_pull_request_to_work_item", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Links a pull request to a work item, the same relation the plus button of the Work items panel creates. Builds the artifact link from the pull request itself, so no vstfs URL has to be passed."
    )]
    public Task<WorkItem> LinkPullRequestToWorkItemAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Work item id that receives the link.")]
        int workItemId,
        [Description("Optional comment describing the link.")]
        string? comment = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.LinkPullRequestToWorkItemAsync(
            repository,
            pullRequestId,
            workItemId,
            EffectiveProject(project),
            comment,
            cancellationToken
        );
    }

    [McpServerTool(Name = "reply_to_pull_request_thread", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Replies to an existing comment thread of a pull request. Use list_pull_request_threads to find the thread id."
    )]
    public Task<PullRequestComment> ReplyToPullRequestThreadAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Thread id from list_pull_request_threads.")]
        int threadId,
        [Description("Reply text.")] string comment,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.ReplyToPullRequestThreadAsync(
            repository,
            pullRequestId,
            threadId,
            comment,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "update_pull_request_comment", Destructive = true, UseStructuredContent = true)]
    [Description(
        "Updates the text of an existing pull request comment. Use list_pull_request_threads to find the thread id and comment id."
    )]
    public Task<PullRequestComment> UpdatePullRequestCommentAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Thread id from list_pull_request_threads.")]
        int threadId,
        [Description("Comment id from list_pull_request_threads.")]
        int commentId,
        [Description("New comment text.")] string comment,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.UpdatePullRequestCommentAsync(
            repository,
            pullRequestId,
            threadId,
            commentId,
            comment,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "set_pull_request_thread_status", Destructive = false, UseStructuredContent = true)]
    [Description("Sets the status of a pull request comment thread, for example to resolve it as fixed or won't fix.")]
    public Task<PullRequestThread> SetPullRequestThreadStatusAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Thread id from list_pull_request_threads.")]
        int threadId,
        [Description("Target status: active, fixed, wontFix, closed, byDesign, or pending.")]
        string status,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.SetPullRequestThreadStatusAsync(
            repository,
            pullRequestId,
            threadId,
            status,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "update_pull_request", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Updates the title or description of a pull request, turns auto-complete on or off for the signed-in user, or toggles its draft status."
    )]
    public Task<GitPullRequest> UpdatePullRequestAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional new title.")] string? title = null,
        [Description("Optional new description. Pass an empty string to clear it.")]
        string? description = null,
        [Description("Optional auto-complete switch: true sets it for the signed-in user, false clears it.")]
        bool? autoComplete = null,
        [Description("Optional draft switch: true marks the pull request as a draft, false publishes it.")]
        bool? isDraft = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.UpdatePullRequestAsync(
            repository,
            pullRequestId,
            title,
            description,
            autoComplete,
            isDraft,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "add_pull_request_reviewer", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Adds a reviewer to a pull request. Accepts an identity id, an account name, or 'me' for the signed-in user."
    )]
    public Task<PullRequestReviewer> AddPullRequestReviewerAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Identity id, account name such as domain\\user or user@example.local, or 'me'.")]
        string reviewer,
        [Description("Set to true to add the reviewer as required.")]
        bool? isRequired = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.AddPullRequestReviewerAsync(
            repository,
            pullRequestId,
            reviewer,
            isRequired ?? false,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "remove_pull_request_reviewer", Destructive = true, UseStructuredContent = true)]
    [Description("Removes a reviewer from a pull request, discarding any vote they cast.")]
    public async Task<string> RemovePullRequestReviewerAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Identity id, account name, or 'me'.")]
        string reviewer,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        await _client.RemovePullRequestReviewerAsync(
            repository,
            pullRequestId,
            reviewer,
            EffectiveProject(project),
            cancellationToken
        );

        return $"Reviewer '{reviewer}' was removed from pull request {pullRequestId}.";
    }

    internal static int ParseVote(string vote)
    {
        return vote.ToLowerInvariant() switch
        {
            "approve" => 10,
            "approve_with_suggestions" => 5,
            "reset" => 0,
            "wait_for_author" => -5,
            "reject" => -10,
            _ => throw new McpException(
                "Vote must be one of: approve, approve_with_suggestions, wait_for_author, reject, reset."
            )
        };
    }

    [McpServerTool(Name = "update_pull_request_status", Destructive = true, UseStructuredContent = true)]
    [Description("Completes, abandons, or reactivates a pull request.")]
    public Task<GitPullRequest> UpdatePullRequestStatusAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Target status: completed, abandoned, or active.")]
        string status,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.UpdatePullRequestStatusAsync(
            repository,
            pullRequestId,
            status,
            EffectiveProject(project),
            cancellationToken
        );
    }

    private string? EffectiveProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
    }
}