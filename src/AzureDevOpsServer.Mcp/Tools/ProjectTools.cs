using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class ProjectTools
{
    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public ProjectTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_projects", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the projects in the configured Azure DevOps Server collection.")]
    public Task<IReadOnlyList<TeamProject>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        return _client.GetProjectsAsync(cancellationToken);
    }

    [McpServerTool(Name = "get_project", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets the details of a project including its process template and version control type.")]
    public Task<ProjectDetails> GetProjectAsync(
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        var effectiveProject = string.IsNullOrWhiteSpace(project) ? _options.Value.DefaultProject : project;
        return _client.GetProjectAsync(effectiveProject, cancellationToken);
    }
}