using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class ProjectTools
{
    private readonly AzureDevOpsClient _client;

    public ProjectTools(AzureDevOpsClient client)
    {
        _client = client;
    }

    [McpServerTool(Name = "list_projects")]
    [Description("Lists the projects in the configured Azure DevOps Server collection.")]
    public Task<IReadOnlyList<TeamProject>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        return _client.GetProjectsAsync(cancellationToken);
    }
}