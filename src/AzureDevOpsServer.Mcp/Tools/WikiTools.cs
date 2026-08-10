using System.ComponentModel;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Tools;

[McpServerToolType]
public sealed class WikiTools
{
    private readonly AzureDevOpsClient _client;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public WikiTools(AzureDevOpsClient client, IOptions<AzureDevOpsServerOptions> options)
    {
        _client = client;
        _options = options;
    }

    [McpServerTool(Name = "list_wikis", ReadOnly = true)]
    [Description("Lists the wikis of a project. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<IReadOnlyList<Wiki>> ListWikisAsync(
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")] string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetWikisAsync(EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_wiki_pages", ReadOnly = true)]
    [Description("Lists the full page tree of a wiki as nested paths without content.")]
    public Task<WikiPage> ListWikiPagesAsync(
        [Description("Wiki name or id.")] string wiki,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetWikiPageTreeAsync(wiki, EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "get_wiki_page", ReadOnly = true)]
    [Description("Gets the markdown content of a wiki page by its path.")]
    public Task<WikiPage> GetWikiPageAsync(
        [Description("Wiki name or id.")] string wiki,
        [Description("Page path, for example /Onboarding/Setup.")]
        string path,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.GetWikiPageAsync(wiki, path, EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "create_or_update_wiki_page", Destructive = false)]
    [Description("Creates a wiki page or replaces the content of an existing one. The whole page content is overwritten, so read the page first when you only want to append.")]
    public Task<WikiPageUpdate> CreateOrUpdateWikiPageAsync(
        [Description("Wiki name or id.")] string wiki,
        [Description("Page path, for example /Onboarding/Setup. Parent paths are created automatically.")]
        string path,
        [Description("Full markdown content of the page.")] string content,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project,
        CancellationToken cancellationToken)
    {
        return _client.CreateOrUpdateWikiPageAsync(wiki, path, content, EffectiveProject(project), cancellationToken);
    }

    private string? EffectiveProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            _options.Value.DefaultProject :
            project;
    }
}