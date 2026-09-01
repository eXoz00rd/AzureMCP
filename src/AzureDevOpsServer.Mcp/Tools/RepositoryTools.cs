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
    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public RepositoryTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_repositories", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists Git repositories in the given project, the default project, or the whole collection.")]
    public Task<IReadOnlyList<GitRepository>> ListRepositoriesAsync(
        [Description(
            "Optional project name. Falls back to ADOS_DEFAULT_PROJECT; lists all repositories when neither is set."
        )]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetRepositoriesAsync(EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_branches", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Lists the branches of a Git repository. Returns at most the requested number of branches, so raise it when a branch seems missing."
    )]
    public Task<IReadOnlyList<GitRef>> ListBranchesAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Maximum number of branches to return. Defaults to 100. Valid range 1-1000.")]
        int? top = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetBranchesAsync(
            repository,
            EffectiveProject(project),
            ResponseLimits.ResolveTop(top),
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_file_content", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Gets the content of a text file from a Git repository. Uses the default branch when no branch is given."
    )]
    public Task<GitFileContent> GetFileContentAsync(
        [Description("Repository name or id.")] string repository,
        [Description("File path inside the repository, for example /src/Program.cs.")]
        string path,
        [Description("Optional branch name.")] string? branch = null,
        [Description(
            "Maximum number of characters to return. Defaults to 30000, valid range 1-1000000; the result reports whether it was truncated."
        )]
        int? maxChars = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetFileContentAsync(
            repository,
            path,
            branch,
            EffectiveProject(project),
            ResponseLimits.ResolveMaxChars(maxChars),
            cancellationToken
        );
    }

    [McpServerTool(Name = "list_commits", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists recent commits of a repository, optionally filtered by branch and file path.")]
    public Task<IReadOnlyList<GitCommit>> ListCommitsAsync(
        [Description("Repository name or id.")] string repository,
        [Description(
            "Optional branch name, with or without the refs/heads/ prefix. Uses the default branch when omitted."
        )]
        string? branch = null,
        [Description("Optional file or folder path to filter the history by.")]
        string? itemPath = null,
        [Description("Maximum number of commits to return. Defaults to 20. Valid range 1-1000.")]
        int? top = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetCommitsAsync(
            repository,
            branch,
            itemPath,
            ResponseLimits.ResolveTop(top, ResponseLimits.DefaultCommitCount),
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_commit", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets a commit with its message, author, and the list of changed files.")]
    public Task<GitCommitDetails> GetCommitAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Full or abbreviated commit SHA.")]
        string commitId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetCommitAsync(repository, commitId, EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_repository_items", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the files and folders of a repository path, one level deep by default.")]
    public Task<LimitedList<GitTreeItem>> ListRepositoryItemsAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Optional folder path, defaults to the repository root.")]
        string? path = null,
        [Description("Optional branch name. Uses the default branch when omitted.")]
        string? branch = null,
        [Description("Set to true to list the whole subtree recursively.")]
        bool? recursive = null,
        [Description(
            "Maximum number of entries to return. Defaults to 500, valid range 1-10000; the result reports whether it was truncated."
        )]
        int? maxItems = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetRepositoryItemsAsync(
            repository,
            path,
            branch,
            recursive ?? false,
            EffectiveProject(project),
            ResponseLimits.ResolveMaxItems(maxItems),
            cancellationToken
        );
    }

    [McpServerTool(Name = "diff_branches", ReadOnly = true, UseStructuredContent = true)]
    [Description("Compares two branches: ahead and behind commit counts plus the changed files.")]
    public Task<GitDiffs> DiffBranchesAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Base branch name.")] string baseBranch,
        [Description("Target branch name.")] string targetBranch,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project = null,
        CancellationToken cancellationToken = default)
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
