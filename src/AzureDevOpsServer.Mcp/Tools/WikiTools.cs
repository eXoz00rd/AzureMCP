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

    [McpServerTool(Name = "list_wikis", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the wikis of a project. Requires a project name or ADOS_DEFAULT_PROJECT.")]
    public Task<IReadOnlyList<Wiki>> ListWikisAsync(
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")] string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetWikisAsync(EffectiveProject(project), cancellationToken);
    }

    [McpServerTool(Name = "list_wiki_pages", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Lists the page tree of a wiki as nested paths without content. The result reports the total number of pages and whether it was truncated, so raise the limit when it was."
    )]
    public Task<WikiPageTree> ListWikiPagesAsync(
        [Description("Wiki name or id.")] string wiki,
        [Description(
            "Maximum number of pages to return, counted level by level so top-level pages come first. Defaults to 25. Valid range 1-1000."
        )]
        int? top = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.GetWikiPageTreeAsync(
            wiki,
            ResponseLimits.ResolveTop(top),
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "get_wiki_page", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Gets the markdown content of a wiki page by its path. The result reports the total length and line count, so read a long page in parts with a line range."
    )]
    public Task<WikiPageContent> GetWikiPageAsync(
        [Description("Wiki name or id.")] string wiki,
        [Description("Page path, for example /Onboarding/Setup.")]
        string path,
        [Description("Optional 1-based first line to return.")]
        int? startLine = null,
        [Description("Optional 1-based last line to return.")]
        int? endLine = null,
        [Description(
            "Maximum number of characters to return. Defaults to 8000, valid range 1-1000000; the result reports the total length and whether it was truncated."
        )]
        int? maxChars = null,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        ResponseLimits.ValidateLineRange(startLine, endLine);
        return _client.GetWikiPageAsync(
            wiki,
            path,
            startLine,
            endLine,
            ResponseLimits.ResolveMaxChars(maxChars),
            EffectiveProject(project),
            cancellationToken
        );
    }

    [McpServerTool(Name = "create_or_update_wiki_page", Destructive = false, UseStructuredContent = true)]
    [Description(
        "Creates a wiki page or replaces the content of an existing one. The whole page content is overwritten, so read the page first when you only want to append."
    )]
    public Task<WikiPageUpdate> CreateOrUpdateWikiPageAsync(
        [Description("Wiki name or id.")] string wiki,
        [Description("Page path, for example /Onboarding/Setup. Parent paths are created automatically.")]
        string path,
        [Description("Full markdown content of the page.")]
        string content,
        [Description("Optional project name. Falls back to ADOS_DEFAULT_PROJECT when omitted.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        return _client.CreateOrUpdateWikiPageAsync(
            wiki,
            path,
            content,
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
