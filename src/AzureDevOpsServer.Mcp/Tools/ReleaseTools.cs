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
    private const int DefaultReleaseCount = 20;

    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public ReleaseTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_release_definitions")]
    [Description("Lists the release definitions of a project. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<IReadOnlyList<ReleaseDefinition>> ListReleaseDefinitionsAsync(
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")] string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetReleaseDefinitionsAsync(EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_releases")]
    [Description(
        "Lists recent releases of a project, optionally filtered by release definition. Requires a project name or ADOS_DEFAULT_PROJECT."
    )]
    public Task<IReadOnlyList<Release>> ListReleasesAsync(
        [Description("Optional release definition id to filter by.")] int? definitionId,
        [Description("Maximum number of releases to return. Defaults to 20.")]
        int? top,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetReleasesAsync(
            EffectiveProject(project),
            definitionId,
            top ?? DefaultReleaseCount,
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_release")]
    [Description("Gets a release with the deployment status of each environment.")]
    public Task<Release> GetReleaseAsync(
        [Description("Release id.")] int releaseId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetReleaseAsync(EffectiveProject(project), releaseId, cancellationToken);
    }

    [McpServerTool(Name = "create_release")]
    [Description("Creates a new release from a release definition. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<Release> CreateReleaseAsync(
        [Description("Release definition id.")] int definitionId,
        [Description("Optional description of the release.")]
        string? description,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.CreateReleaseAsync(EffectiveProject(project), definitionId, description, cancellationToken);
    }

    private string? EffectiveProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
    }
}