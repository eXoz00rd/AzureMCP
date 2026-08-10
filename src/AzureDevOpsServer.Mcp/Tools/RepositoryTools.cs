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

    [McpServerTool(Name = "list_repositories")]
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

    [McpServerTool(Name = "list_branches")]
    [Description("Lists the branches of a Git repository.")]
    public Task<IReadOnlyList<GitRef>> ListBranchesAsync(
        [Description("Repository name or id.")] string repository,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetBranchesAsync(repository, EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "get_file_content")]
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

    private string? EffectiveProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
    }
}