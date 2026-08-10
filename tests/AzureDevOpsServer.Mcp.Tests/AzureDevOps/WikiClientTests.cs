using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class WikiClientTests : AzureDevOpsClientTestsBase
{
    [Fact]
    public async Task GetWikisAsync_ReturnsProjectWikis()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                { "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb", "name": "Alpha.wiki", "type": "projectWiki" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var wikis = await client.GetWikisAsync("Alpha", TestContext.Current.CancellationToken);

        var wiki = Assert.Single(wikis);
        Assert.Equal("Alpha.wiki", wiki.Name);
        Assert.Equal("projectWiki", wiki.Type);
        Assert.EndsWith(
            "Alpha/_apis/wiki/wikis?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetWikisAsync_WithoutProject_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.GetWikisAsync(null, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ADOS_DEFAULT_PROJECT", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetWikiPageAsync_ReturnsContent()
    {
        const string json =
            """
            {
              "path": "/Onboarding/Setup",
              "content": "# Setup\nInstall the SDK first."
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var page = await client.GetWikiPageAsync(
            "Alpha.wiki",
            "/Onboarding/Setup",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/Onboarding/Setup", page.Path);
        Assert.StartsWith("# Setup", page.Content);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("wikis/Alpha.wiki/pages", requestUri);
        Assert.Contains("path=%2FOnboarding%2FSetup", requestUri);
        Assert.Contains("includeContent=true", requestUri);
    }

    [Fact]
    public async Task GetWikiPageTreeAsync_ReturnsNestedPages()
    {
        const string json =
            """
            {
              "path": "/",
              "subPages": [
                { "path": "/Onboarding", "subPages": [ { "path": "/Onboarding/Setup" } ] },
                { "path": "/Architecture" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var root = await client.GetWikiPageTreeAsync("Alpha.wiki", "Alpha", TestContext.Current.CancellationToken);

        Assert.Equal("/", root.Path);
        Assert.Equal(2, root.SubPages!.Count);
        Assert.Equal("/Onboarding/Setup", Assert.Single(root.SubPages[0].SubPages!).Path);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("path=%2F&recursionLevel=full", requestUri);
    }

}
