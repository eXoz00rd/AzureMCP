using System.ComponentModel;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class ServerInfoTool
{
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public ServerInfoTool(IOptions<AzureDevOpsServerOptions> options)
    {
        _options = options;
    }

    [McpServerTool(Name = "server_info", ReadOnly = true, UseStructuredContent = true)]
    [Description("Returns the configured Azure DevOps Server connection details. The PAT is never exposed.")]
    public ServerInfo GetServerInfo()
    {
        var options = _options.Value;
        return new ServerInfo(options.CollectionUrl, options.DefaultProject, options.ApiVersion);
    }
}

public sealed record ServerInfo(string CollectionUrl, string? DefaultProject, string ApiVersion);