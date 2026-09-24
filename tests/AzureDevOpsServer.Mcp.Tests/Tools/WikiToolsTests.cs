using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tools;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class WikiToolsTests : ToolTestsBase
{
    [Fact]
    public async Task ListWikiPagesAsync_WithoutTop_ReturnsTheDefaultNumberOfPages()
    {
        var pages = string.Join(", ", Enumerable.Range(1, ResponseLimits.DefaultListTop + 1).Select(n => $$"""{ "path": "/P{{n}}" }"""));
        using var response = JsonResponse($$"""{ "path": "/", "subPages": [ {{pages}} ] }""");
        var harness = CreateHarness("FallbackProject", response);
        var tools = new WikiTools(harness.Client, harness.Options);

        var tree = await tools.ListWikiPagesAsync("Alpha.wiki", null, null, TestContext.Current.CancellationToken);

        Assert.Equal(ResponseLimits.DefaultListTop, tree.SubPages!.Count);
        Assert.Equal(ResponseLimits.DefaultListTop + 1, tree.TotalPages);
        Assert.True(tree.Truncated);
        Assert.Contains("/FallbackProject/_apis/wiki/wikis/Alpha.wiki/pages", harness.RequestUri);
    }

    [Fact]
    public async Task ListWikiPagesAsync_WithExplicitTop_OverridesTheDefault()
    {
        using var response = JsonResponse("""{ "path": "/", "subPages": [ { "path": "/A" }, { "path": "/B" } ] }""");
        var harness = CreateHarness("FallbackProject", response);
        var tools = new WikiTools(harness.Client, harness.Options);

        var tree = await tools.ListWikiPagesAsync("Alpha.wiki", 1, null, TestContext.Current.CancellationToken);

        Assert.Equal("/A", Assert.Single(tree.SubPages!).Path);
        Assert.True(tree.Truncated);
    }

    [Fact]
    public async Task GetWikiPageAsync_WithoutMaxChars_ReturnsTheDefaultNumberOfCharacters()
    {
        var content = new string('x', ResponseLimits.DefaultMaxChars + 1);
        using var response = JsonResponse($$"""{ "path": "/Big", "content": "{{content}}" }""");
        var harness = CreateHarness("FallbackProject", response);
        var tools = new WikiTools(harness.Client, harness.Options);

        var page = await tools.GetWikiPageAsync(
            "Alpha.wiki",
            "/Big",
            null,
            null,
            null,
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ResponseLimits.DefaultMaxChars, page.Content!.Length);
        Assert.Equal(ResponseLimits.DefaultMaxChars + 1, page.TotalChars);
        Assert.True(page.Truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ResponseLimits.MaxTop + 1)]
    public async Task ListWikiPagesAsync_WithInvalidTop_FailsBeforeSendingRequest(int top)
    {
        var harness = CreateHarness("FallbackProject");
        var tools = new WikiTools(harness.Client, harness.Options);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => tools.ListWikiPagesAsync("Alpha.wiki", top, null, TestContext.Current.CancellationToken)
        );

        Assert.Empty(harness.Handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ResponseLimits.MaxChars + 1)]
    public async Task GetWikiPageAsync_WithInvalidMaxChars_FailsBeforeSendingRequest(int maxChars)
    {
        var harness = CreateHarness("FallbackProject");
        var tools = new WikiTools(harness.Client, harness.Options);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => tools.GetWikiPageAsync(
                "Alpha.wiki",
                "/Page",
                null,
                null,
                maxChars,
                null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Empty(harness.Handler.Requests);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-3, 5)]
    [InlineData(null, 0)]
    [InlineData(5, 4)]
    public async Task GetWikiPageAsync_WithInvalidLineRange_FailsBeforeSendingRequest(int? startLine, int? endLine)
    {
        var harness = CreateHarness("FallbackProject");
        var tools = new WikiTools(harness.Client, harness.Options);

        await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => tools.GetWikiPageAsync(
                "Alpha.wiki",
                "/Page",
                startLine,
                endLine,
                null,
                null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Empty(harness.Handler.Requests);
    }
}
