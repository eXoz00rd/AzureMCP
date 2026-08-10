using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class BuildTools
{
    private const int DefaultBuildCount = 20;

    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public BuildTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_build_definitions")]
    [Description("Lists the build definitions of a project. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<IReadOnlyList<BuildDefinition>> ListBuildDefinitionsAsync(
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")] string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetBuildDefinitionsAsync(EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_builds")]
    [Description(
        "Lists recent builds of a project, optionally filtered by build definition. Requires a project name or ADOS_DEFAULT_PROJECT."
    )]
    public Task<IReadOnlyList<Build>> ListBuildsAsync(
        [Description("Optional build definition id to filter by.")] int? definitionId,
        [Description("Maximum number of builds to return. Defaults to 20.")]
        int? top,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetBuildsAsync(
            EffectiveProject(project),
            definitionId,
            top ?? DefaultBuildCount,
            cancellationToken
        );
    }

    [McpServerTool(Name = "queue_build")]
    [Description("Queues a new build for a build definition. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<Build> QueueBuildAsync(
        [Description("Build definition id.")] int definitionId,
        [Description(
            "Optional source branch, with or without the refs/heads/ prefix. Uses the definition default when omitted."
        )]
        string? sourceBranch,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.QueueBuildAsync(EffectiveProject(project), definitionId, sourceBranch, cancellationToken);
    }

    [McpServerTool(Name = "get_build_timeline")]
    [Description(
        "Gets the timeline of a build: stages, jobs, and tasks with their results, error counts, and issue messages of failed records. Each record references its log id."
    )]
    public Task<IReadOnlyList<TimelineRecord>> GetBuildTimelineAsync(
        [Description("Build id.")] int buildId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetBuildTimelineAsync(EffectiveProject(project), buildId, cancellationToken);
    }

    [McpServerTool(Name = "get_build_log")]
    [Description("Gets the text content of a build log. Use get_build_timeline to find the log id of a failed task.")]
    public Task<string> GetBuildLogAsync(
        [Description("Build id.")] int buildId,
        [Description("Log id from the timeline record.")]
        int logId,
        [Description("Optional 1-based first line to return.")]
        int? startLine,
        [Description("Optional 1-based last line to return.")]
        int? endLine,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetBuildLogAsync(
            EffectiveProject(project),
            buildId,
            logId,
            startLine,
            endLine,
            cancellationToken
        );
    }

    [McpServerTool(Name = "list_build_artifacts")]
    [Description("Lists the published artifacts of a build with their download URLs.")]
    public Task<IReadOnlyList<BuildArtifact>> ListBuildArtifactsAsync(
        [Description("Build id.")] int buildId,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetBuildArtifactsAsync(EffectiveProject(project), buildId, cancellationToken);
    }

    private string? EffectiveProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
    }
}