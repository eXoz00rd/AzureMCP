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
    public async Task CreateOrUpdateWikiPageAsync_WhenPageIsMissing_CreatesWithoutIfMatch()
    {
        using var versionCheck = new HttpResponseMessage(HttpStatusCode.NotFound);
        using var put = JsonResponse("""{ "path": "/Runbooks/Deploy" }""");
        put.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
        var client = CreateClient(out var handler, versionCheck, put);

        var result = await client.CreateOrUpdateWikiPageAsync(
            "Alpha.wiki",
            "/Runbooks/Deploy",
            "# Deploy",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.True(result.Created);
        Assert.Equal("/Runbooks/Deploy", result.Path);
        Assert.Equal("\"v1\"", result.Version);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.False(handler.Requests[1].Headers.Contains("If-Match"));
        Assert.Contains("path=%2FRunbooks%2FDeploy", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CreateOrUpdateWikiPageAsync_WhenPageExists_SendsIfMatchVersion()
    {
        using var versionCheck = JsonResponse("""{ "path": "/Runbooks/Deploy" }""");
        versionCheck.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v3\"");
        using var put = JsonResponse("""{ "path": "/Runbooks/Deploy" }""");
        var client = CreateClient(out var handler, versionCheck, put);

        var result = await client.CreateOrUpdateWikiPageAsync(
            "Alpha.wiki",
            "/Runbooks/Deploy",
            "# Deploy v2",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.False(result.Created);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("\"v3\"", Assert.Single(handler.Requests[1].Headers.GetValues("If-Match")));
    }

    [Fact]
    public async Task CreateOrUpdateWikiPageAsync_WithoutProject_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(() => client.CreateOrUpdateWikiPageAsync(
                "Alpha.wiki",
                "/Page",
                "content",
                null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("ADOS_DEFAULT_PROJECT", exception.Message);
        Assert.Empty(handler.Requests);
    }

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
            null,
            null,
            ResponseLimits.DefaultMaxChars,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/Onboarding/Setup", page.Path);
        Assert.Equal("# Setup\nInstall the SDK first.", page.Content);
        Assert.Equal(30, page.TotalChars);
        Assert.Equal(2, page.TotalLines);
        Assert.False(page.Truncated);
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

        var root = await client.GetWikiPageTreeAsync(
            "Alpha.wiki",
            ResponseLimits.DefaultListTop,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/", root.Path);
        Assert.Equal(3, root.TotalPages);
        Assert.False(root.Truncated);
        Assert.Equal(2, root.SubPages!.Count);
        Assert.Equal("/Onboarding/Setup", Assert.Single(root.SubPages[0].SubPages!).Path);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("path=%2F&recursionLevel=full", requestUri);
    }
    [Fact]
    public async Task GetWikiPageAsync_WhenContentExceedsMaxChars_ReturnsPrefixAndReportsTruncated()
    {
        using var response = JsonResponse("""{ "path": "/Big", "content": "0123456789ABCDEFGHIJ" }""");
        var client = CreateClient(out _, response);

        var page = await client.GetWikiPageAsync(
            "Alpha.wiki",
            "/Big",
            null,
            null,
            10,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("0123456789", page.Content);
        Assert.Equal(20, page.TotalChars);
        Assert.True(page.Truncated);
    }

    [Fact]
    public async Task GetWikiPageAsync_WhenContentEqualsMaxChars_IsNotTruncated()
    {
        using var response = JsonResponse("""{ "path": "/Exact", "content": "0123456789" }""");
        var client = CreateClient(out _, response);

        var page = await client.GetWikiPageAsync(
            "Alpha.wiki",
            "/Exact",
            null,
            null,
            10,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("0123456789", page.Content);
        Assert.Equal(10, page.TotalChars);
        Assert.False(page.Truncated);
    }

    [Fact]
    public async Task GetWikiPageAsync_WithLineRange_ReturnsThoseLinesAndTheWholePageLineCount()
    {
        using var response = JsonResponse("""{ "path": "/Lines", "content": "one\ntwo\nthree\nfour\nfive\n" }""");
        var client = CreateClient(out _, response);

        var page = await client.GetWikiPageAsync(
            "Alpha.wiki",
            "/Lines",
            2,
            3,
            ResponseLimits.DefaultMaxChars,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("two\nthree\n", page.Content);
        Assert.Equal(10, page.TotalChars);
        Assert.Equal(5, page.TotalLines);
        Assert.False(page.Truncated);
    }

    [Fact]
    public async Task GetWikiPageAsync_WhenLineRangeExceedsMaxChars_ReportsTruncated()
    {
        using var response = JsonResponse("""{ "path": "/Lines", "content": "one\ntwo\nthree\nfour\n" }""");
        var client = CreateClient(out _, response);

        var page = await client.GetWikiPageAsync(
            "Alpha.wiki",
            "/Lines",
            2,
            null,
            6,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("two\nth", page.Content);
        Assert.Equal(15, page.TotalChars);
        Assert.True(page.Truncated);
    }

    [Fact]
    public async Task GetWikiPageAsync_WithoutContent_ReturnsNoContent()
    {
        using var response = JsonResponse("""{ "path": "/Onboarding" }""");
        var client = CreateClient(out _, response);

        var page = await client.GetWikiPageAsync(
            "Alpha.wiki",
            "/Onboarding",
            null,
            null,
            ResponseLimits.DefaultMaxChars,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Null(page.Content);
        Assert.Equal(0, page.TotalChars);
        Assert.False(page.Truncated);
    }

    [Fact]
    public async Task GetWikiPageTreeAsync_WhenTreeExceedsTop_KeepsPagesLevelByLevelAndReportsTruncated()
    {
        const string json =
            """
            {
              "path": "/",
              "subPages": [
                { "path": "/A", "subPages": [ { "path": "/A/1" }, { "path": "/A/2" }, { "path": "/A/3" } ] },
                { "path": "/B", "subPages": [ { "path": "/B/1" } ] },
                { "path": "/C" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var tree = await client.GetWikiPageTreeAsync("Alpha.wiki", 4, "Alpha", TestContext.Current.CancellationToken);

        Assert.True(tree.Truncated);
        Assert.Equal(7, tree.TotalPages);
        Assert.Equal(["/A", "/B", "/C"], tree.SubPages!.Select(page => page.Path));
        Assert.Equal(["/A/1"], tree.SubPages![0].SubPages!.Select(page => page.Path));
        Assert.Empty(tree.SubPages[1].SubPages!);
        Assert.Null(tree.SubPages[2].SubPages);
    }

    [Fact]
    public async Task GetWikiPageTreeAsync_WhenTreeHasExactlyTopPages_IsNotTruncated()
    {
        const string json =
            """{ "path": "/", "subPages": [ { "path": "/A", "subPages": [ { "path": "/A/1" } ] }, { "path": "/B" } ] }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var tree = await client.GetWikiPageTreeAsync("Alpha.wiki", 3, "Alpha", TestContext.Current.CancellationToken);

        Assert.False(tree.Truncated);
        Assert.Equal(3, tree.TotalPages);
        Assert.Equal("/A/1", Assert.Single(tree.SubPages![0].SubPages!).Path);
    }
}
