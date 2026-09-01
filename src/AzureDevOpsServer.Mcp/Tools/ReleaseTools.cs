using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class ReleaseTools
{
    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public ReleaseTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_release_definitions", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the release definitions of a project. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<IReadOnlyList<ReleaseDefinition>> ListReleaseDefinitionsAsync(
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")] string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetReleaseDefinitionsAsync(EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_releases", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Lists recent releases of a project, optionally filtered by release definition. Requires a project name or ADOS_DEFAULT_PROJECT."
    )]
    public Task<IReadOnlyList<Release>> ListReleasesAsync(
        [Description("Optional release definition id to filter by.")] int? definitionId = null,
        [Description("Maximum number of releases to return. Defaults to 20. Valid range 1-1000.")]
        int? top = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetReleasesAsync(
            EffectiveProject(project),
            definitionId,
            ResponseLimits.ResolveTop(top, ResponseLimits.DefaultReleaseCount),
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_release", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets a release with the deployment status of each environment.")]
    public Task<Release> GetReleaseAsync(
        [Description("Release id.")] int releaseId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetReleaseAsync(EffectiveProject(project), releaseId, cancellationToken);
    }

    [McpServerTool(Name = "create_release", Destructive = false, UseStructuredContent = true)]
    [Description("Creates a new release from a release definition. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<Release> CreateReleaseAsync(
        [Description("Release definition id.")] int definitionId,
        [Description("Optional description of the release.")]
        string? description = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.CreateReleaseAsync(EffectiveProject(project), definitionId, description, cancellationToken);
    }

    [McpServerTool(Name = "list_release_approvals", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Lists pending deployment approvals of a project, optionally for a single release. Shows which gate is blocking a deployment."
    )]
    public Task<IReadOnlyList<ReleaseApproval>> ListReleaseApprovalsAsync(
        [Description("Optional release id to filter by.")] int? releaseId = null,
        [Description("Maximum number of approvals to return. Defaults to 100. Valid range 1-1000.")]
        int? top = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetReleaseApprovalsAsync(
            EffectiveProject(project),
            releaseId,
            ResponseLimits.ResolveTop(top),
            cancellationToken
        );
    }

    [McpServerTool(Name = "update_release_approval", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Approves or rejects a pending deployment approval. Use list_release_approvals to find the approval id."
    )]
    public Task<ReleaseApproval> UpdateReleaseApprovalAsync(
        [Description("Approval id.")] int approvalId,
        [Description("Decision: approved or rejected.")]
        string status,
        [Description("Optional comment stored with the decision.")]
        string? comment = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.UpdateReleaseApprovalAsync(
            EffectiveProject(project),
            approvalId,
            status,
            comment,
            cancellationToken
        );
    }

    [McpServerTool(Name = "deploy_release_environment", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Starts the deployment of a single environment of a release, for example to promote a release to production."
    )]
    public Task<ReleaseEnvironment> DeployReleaseEnvironmentAsync(
        [Description("Release id.")] int releaseId,
        [Description("Environment id from get_release.")]
        int environmentId,
        [Description("Optional comment stored with the deployment.")]
        string? comment = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.DeployReleaseEnvironmentAsync(
            EffectiveProject(project),
            releaseId,
            environmentId,
            comment,
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
