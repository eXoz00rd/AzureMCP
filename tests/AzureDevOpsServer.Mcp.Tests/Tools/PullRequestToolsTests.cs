using AzureDevOpsServer.Mcp.Tools;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class PullRequestToolsTests : ToolTestsBase
{
    [Theory]
    [InlineData("approve", 10)]
    [InlineData("APPROVE", 10)]
    [InlineData("approve_with_suggestions", 5)]
    [InlineData("reset", 0)]
    [InlineData("wait_for_author", -5)]
    [InlineData("reject", -10)]
    public void ParseVote_MapsKnownVotes(string vote, int expected)
    {
        Assert.Equal(expected, PullRequestTools.ParseVote(vote));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("")]
    [InlineData("10")]
    public void ParseVote_WithUnknownVote_ThrowsArgumentException(string vote)
    {
        var exception = Assert.Throws<ArgumentException>(() => PullRequestTools.ParseVote(vote));

        Assert.Contains("approve_with_suggestions", exception.Message);
    }

    [Fact]
    public async Task ListPullRequestsAsync_WithoutProject_UsesDefaultProject()
    {
        using var response = EmptyList();
        var harness = CreateHarness("FallbackProject", response);
        var tools = new PullRequestTools(harness.Client, harness.Options);

        await tools.ListPullRequestsAsync("WebApp", null, null, null, TestContext.Current.CancellationToken);

        Assert.Contains("/FallbackProject/_apis/git/repositories/WebApp/pullrequests", harness.RequestUri);
        Assert.Contains("$top=100", harness.RequestUri);
        Assert.Contains("searchCriteria.status=active", harness.RequestUri);
    }

    [Fact]
    public async Task ListPullRequestsAsync_WithExplicitProject_OverridesDefault()
    {
        using var response = EmptyList();
        var harness = CreateHarness("FallbackProject", response);
        var tools = new PullRequestTools(harness.Client, harness.Options);

        await tools.ListPullRequestsAsync("WebApp", null, 5, "Explicit", TestContext.Current.CancellationToken);

        Assert.Contains("/Explicit/_apis/git/repositories/WebApp/pullrequests", harness.RequestUri);
        Assert.Contains("$top=5", harness.RequestUri);
        Assert.DoesNotContain("FallbackProject", harness.RequestUri);
    }
}
