using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tools;
using ModelContextProtocol;
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
    public void ParseVote_WithUnknownVote_ThrowsMcpException(string vote)
    {
        var exception = Assert.Throws<McpException>(() => PullRequestTools.ParseVote(vote));

        Assert.Contains("approve_with_suggestions", exception.Message);
    }

    [Fact]
    public async Task ListPullRequestsAsync_WithoutProject_UsesDefaultProject()
    {
        using var response = EmptyList();
        var harness = CreateHarness("FallbackProject", response);
        var tools = new PullRequestTools(harness.Client, harness.Options);

        await tools.ListPullRequestsAsync(
            "WebApp",
            null,
            null,
            null,
            TestContext.Current.CancellationToken
        );

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

        await tools.ListPullRequestsAsync(
            "WebApp",
            null,
            5,
            "Explicit",
            TestContext.Current.CancellationToken
        );

        Assert.Contains("/Explicit/_apis/git/repositories/WebApp/pullrequests", harness.RequestUri);
        Assert.Contains("$top=5", harness.RequestUri);
        Assert.DoesNotContain("FallbackProject", harness.RequestUri);
    }

    [Fact]
    public async Task LinkPullRequestToWorkItemAsync_SendsArtifactLinkBuiltFromPullRequest()
    {
        using var pullRequest = JsonResponse(
            """
            {
              "pullRequestId": 63162,
              "title": "Fix the importer",
              "status": "active",
              "sourceRefName": "refs/heads/feature",
              "targetRefName": "refs/heads/main",
              "repository": {
                "id": "8f1c0d1e-2b3a-4c5d-9e8f-7a6b5c4d3e2f",
                "name": "WebApp",
                "project": { "id": "1a2b3c4d-5e6f-4a8b-9c0d-1e2f3a4b5c6d", "name": "FallbackProject" }
              }
            }
            """
        );
        using var link = JsonResponse(
            """{ "id": 63162, "rev": 7, "fields": {}, "url": "https://devops.example.local/_apis/wit/workItems/63162" }"""
        );
        var harness = CreateHarness("FallbackProject", pullRequest, link);
        var tools = new PullRequestTools(harness.Client, harness.Options);

        await tools.LinkPullRequestToWorkItemAsync(
            "WebApp",
            63162,
            63162,
            null,
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, harness.Handler.Requests.Count);
        Assert.Equal(HttpMethod.Patch, harness.Handler.Requests[1].Method);
        Assert.Contains("_apis/wit/workitems/63162", harness.Handler.Requests[1].RequestUri!.AbsoluteUri);
        using var body = JsonDocument.Parse(Assert.Single(harness.Handler.RequestBodies));
        var value = Assert.Single(body.RootElement.EnumerateArray()).GetProperty("value");
        Assert.True(value.GetProperty("rel").ValueEquals("ArtifactLink"));
        Assert.True(
            value.GetProperty("url")
                 .ValueEquals(
                     "vstfs:///Git/PullRequestId/1a2b3c4d-5e6f-4a8b-9c0d-1e2f3a4b5c6d%2F8f1c0d1e-2b3a-4c5d-9e8f-7a6b5c4d3e2f%2F63162"
                 )
        );
        Assert.True(value.GetProperty("attributes").GetProperty("name").ValueEquals("Pull Request"));
    }

    [Fact]
    public async Task LinkPullRequestToWorkItemAsync_WithoutRepositoryIds_ThrowsDescriptiveError()
    {
        using var pullRequest = JsonResponse(
            """
            {
              "pullRequestId": 63162,
              "title": "Fix the importer",
              "status": "active",
              "sourceRefName": "refs/heads/feature",
              "targetRefName": "refs/heads/main"
            }
            """
        );
        var harness = CreateHarness("FallbackProject", pullRequest);
        var tools = new PullRequestTools(harness.Client, harness.Options);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(() => tools.LinkPullRequestToWorkItemAsync(
                "WebApp",
                63162,
                63162,
                null,
                null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("artifact link cannot be built", exception.Message);
    }
}
