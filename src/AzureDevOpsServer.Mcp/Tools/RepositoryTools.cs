using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class RepositoryTools
{
    private const int DefaultCommitCount = 20;

    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public RepositoryTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_repositories", ReadOnly = true)]
    [Description("Lists Git repositories in the given project, the default project, or the whole collection.")]
    public Task<IReadOnlyList<GitRepository>> ListRepositoriesAsync(
        [Description(
            "Optional project name. Falls back to ADOS_DEFAULT_PROJECT; lists all repositories when neither is set."
        )]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetRepositoriesAsync(EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_branches", ReadOnly = true)]
    [Description("Lists the branches of a Git repository.")]
    public Task<IReadOnlyList<GitRef>> ListBranchesAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetBranchesAsync(repository, EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "get_file_content", ReadOnly = true)]
    [Description(
        "Gets the content of a text file from a Git repository. Uses the default branch when no branch is given."
    )]
    public Task<GitItem> GetFileContentAsync(
        [Description("Repository name or id.")] string repository,
        [Description("File path inside the repository, for example /src/Program.cs.")]
        string path,
        [Description("Optional branch name.")] string? branch,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetFileContentAsync(
            repository,
            path,
            branch,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "list_commits", ReadOnly = true)]
    [Description("Lists recent commits of a repository, optionally filtered by branch and file path.")]
    public Task<IReadOnlyList<GitCommit>> ListCommitsAsync(
        [Description("Repository name or id.")] string repository,
        [Description(
            "Optional branch name, with or without the refs/heads/ prefix. Uses the default branch when omitted."
        )]
        string? branch,
        [Description("Optional file or folder path to filter the history by.")]
        string? itemPath,
        [Description("Maximum number of commits to return. Defaults to 20.")]
        int? top,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetCommitsAsync(
            repository,
            branch,
            itemPath,
            top ?? DefaultCommitCount,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_commit", ReadOnly = true)]
    [Description("Gets a commit with its message, author, and the list of changed files.")]
    public Task<GitCommitDetails> GetCommitAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Full or abbreviated commit SHA.")]
        string commitId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetCommitAsync(repository, commitId, EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_repository_items", ReadOnly = true)]
    [Description("Lists the files and folders of a repository path, one level deep by default.")]
    public Task<IReadOnlyList<GitTreeItem>> ListRepositoryItemsAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Optional folder path, defaults to the repository root.")]
        string? path,
        [Description("Optional branch name. Uses the default branch when omitted.")]
        string? branch,
        [Description("Set to true to list the whole subtree recursively.")]
        bool? recursive,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetRepositoryItemsAsync(
            repository,
            path,
            branch,
            recursive ?? false,
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "diff_branches", ReadOnly = true)]
    [Description("Compares two branches: ahead and behind commit counts plus the changed files.")]
    public Task<GitDiffs> DiffBranchesAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Base branch name.")] string baseBranch,
        [Description("Target branch name.")] string targetBranch,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetBranchDiffAsync(
            repository,
            baseBranch,
            targetBranch,
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