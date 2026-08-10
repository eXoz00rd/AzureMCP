using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
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

    [McpServerTool(Name = "list_pull_requests")]
    [Description("Lists pull requests of a Git repository filtered by status.")]
    public Task<IReadOnlyList<GitPullRequest>> ListPullRequestsAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Optional status filter: active, completed, abandoned, or all. Defaults to active.")]
        string? status,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetPullRequestsAsync(repository, EffectiveProject(project), status, cancellationToken);
    }

    [McpServerTool(Name = "get_pull_request")]
    [Description("Gets the details of a single pull request.")]
    public Task<GitPullRequest> GetPullRequestAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetPullRequestAsync(repository, pullRequestId, EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "create_pull_request")]
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
        string? description,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.CreatePullRequestAsync(
            repository,
            sourceBranch,
            targetBranch,
            title,
            description,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_pull_request_changes")]
    [Description("Lists the files changed in a pull request, based on its latest iteration.")]
    public Task<IReadOnlyList<PullRequestChange>> GetPullRequestChangesAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetPullRequestChangesAsync(
            repository,
            pullRequestId,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "list_pull_request_threads")]
    [Description("Lists the comment threads of a pull request, including file context and authors.")]
    public Task<IReadOnlyList<PullRequestThread>> ListPullRequestThreadsAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetPullRequestThreadsAsync(
            repository,
            pullRequestId,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "add_pull_request_comment")]
    [Description("Adds a comment to a pull request as a new thread, optionally anchored to a file and line.")]
    public Task<PullRequestThread> AddPullRequestCommentAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Comment text.")] string comment,
        [Description("Optional repository file path to anchor the comment to, for example /src/Program.cs.")]
        string? filePath,
        [Description("Optional 1-based line number in the file; used only when filePath is set.")]
        int? line,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
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

    private string? EffectiveProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
    }
}