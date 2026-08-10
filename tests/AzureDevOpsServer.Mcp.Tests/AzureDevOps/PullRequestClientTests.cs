using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;
public sealed class PullRequestClientTests : AzureDevOpsClientTestsBase
{
    [Fact]
    public async Task GetPullRequestsAsync_DefaultsToActiveStatus()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "pullRequestId": 7,
                  "title": "Add feature",
                  "description": "Feature description",
                  "status": "active",
                  "sourceRefName": "refs/heads/develop",
                  "targetRefName": "refs/heads/main",
                  "createdBy": { "displayName": "Sebastian", "uniqueName": "sebastian@example.local" }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var pullRequests = await client.GetPullRequestsAsync(
            "WebApp",
            "Alpha",
            null,
            ResponseLimits.DefaultListTop,
            TestContext.Current.CancellationToken
        );

        var pullRequest = Assert.Single(pullRequests);
        Assert.Equal(7, pullRequest.PullRequestId);
        Assert.Equal("refs/heads/develop", pullRequest.SourceRefName);
        Assert.Equal("Sebastian", pullRequest.CreatedBy!.DisplayName);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullrequests?searchCriteria.status=active&$top=100&api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetPullRequestsAsync_WithStatus_UsesGivenStatus()
    {
        using var response = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, response);

        var pullRequests = await client.GetPullRequestsAsync(
            "WebApp",
            "Alpha",
            "completed",
            ResponseLimits.DefaultListTop,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(pullRequests);
        Assert.Contains(
            "searchCriteria.status=completed",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetPullRequestAsync_ReturnsPullRequest()
    {
        const string json =
            """
            {
              "pullRequestId": 7,
              "title": "Add feature",
              "status": "active",
              "sourceRefName": "refs/heads/develop",
              "targetRefName": "refs/heads/main"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var pullRequest = await client.GetPullRequestAsync("WebApp", 7, "Alpha", TestContext.Current.CancellationToken);

        Assert.Equal(7, pullRequest.PullRequestId);
        Assert.Null(pullRequest.Description);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullrequests/7?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task CreatePullRequestAsync_NormalizesBranchRefs()
    {
        const string json =
            """
            {
              "pullRequestId": 8,
              "title": "New PR",
              "status": "active",
              "sourceRefName": "refs/heads/develop",
              "targetRefName": "refs/heads/main"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var pullRequest = await client.CreatePullRequestAsync(
            "WebApp",
            "develop",
            "refs/heads/main",
            "New PR",
            "Description",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(8, pullRequest.PullRequestId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullrequests?api-version=7.0",
            request.RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("sourceRefName").ValueEquals("refs/heads/develop"));
        Assert.True(body.RootElement.GetProperty("targetRefName").ValueEquals("refs/heads/main"));
        Assert.True(body.RootElement.GetProperty("title").ValueEquals("New PR"));
    }


    [Fact]
    public async Task GetPullRequestChangesAsync_UsesLatestIteration()
    {
        const string iterationsJson = """{ "count": 2, "value": [ { "id": 1 }, { "id": 2 } ] }""";
        const string changesJson =
            """
            {
              "changeEntries": [
                { "changeTrackingId": 1, "changeType": "edit", "item": { "path": "/src/Program.cs" } },
                { "changeTrackingId": 2, "changeType": "add", "item": { "path": "/src/NewFile.cs" } }
              ]
            }
            """;
        using var iterations = JsonResponse(iterationsJson);
        using var changes = JsonResponse(changesJson);
        var client = CreateClient(out var handler, iterations, changes);

        var result = await client.GetPullRequestChangesAsync(
            "WebApp",
            7,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.Count);
        Assert.Equal("edit", result[0].ChangeType);
        Assert.Equal("/src/Program.cs", result[0].Item.Path);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullRequests/7/iterations?api-version=7.0",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        Assert.Contains("/iterations/2/changes", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetPullRequestChangesAsync_WithNoIterations_ReturnsEmpty()
    {
        using var iterations = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, iterations);

        var result = await client.GetPullRequestChangesAsync(
            "WebApp",
            7,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Empty(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetPullRequestThreadsAsync_ReturnsThreadsWithComments()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": 10,
                  "status": "active",
                  "threadContext": { "filePath": "/src/Program.cs" },
                  "comments": [
                    {
                      "id": 1,
                      "parentCommentId": 0,
                      "content": "Consider a guard clause here",
                      "author": { "displayName": "Sebastian", "uniqueName": "sebastian@example.local" },
                      "publishedDate": "2026-08-10T12:00:00Z"
                    }
                  ]
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var threads = await client.GetPullRequestThreadsAsync(
            "WebApp",
            7,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        var thread = Assert.Single(threads);
        Assert.Equal("active", thread.Status);
        Assert.Equal("/src/Program.cs", thread.ThreadContext!.FilePath);
        var comment = Assert.Single(thread.Comments!);
        Assert.Equal("Consider a guard clause here", comment.Content);
        Assert.Equal("Sebastian", comment.Author!.DisplayName);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullRequests/7/threads?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task CreatePullRequestThreadAsync_WithoutFile_PostsCommentOnly()
    {
        const string json =
            """
            {
              "id": 11,
              "status": "active",
              "comments": [ { "id": 1, "parentCommentId": 0, "content": "General remark" } ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var thread = await client.CreatePullRequestThreadAsync(
            "WebApp",
            7,
            "General remark",
            null,
            null,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(11, thread.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var commentElement = Assert.Single(body.RootElement.GetProperty("comments").EnumerateArray());
        Assert.True(commentElement.GetProperty("content").ValueEquals("General remark"));
        Assert.Equal(1, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("threadContext", out _));
    }

    [Fact]
    public async Task CreatePullRequestThreadAsync_WithFileAndLine_AnchorsThreadContext()
    {
        using var response = JsonResponse("""{ "id": 12, "status": "active", "comments": [] }""");
        var client = CreateClient(out var handler, response);

        await client.CreatePullRequestThreadAsync(
            "WebApp",
            7,
            "Rename this variable",
            "/src/Program.cs",
            5,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var threadContext = body.RootElement.GetProperty("threadContext");
        Assert.True(threadContext.GetProperty("filePath").ValueEquals("/src/Program.cs"));
        Assert.Equal(5, threadContext.GetProperty("rightFileStart").GetProperty("line").GetInt32());
        Assert.Equal(5, threadContext.GetProperty("rightFileEnd").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task SetPullRequestVoteAsync_ResolvesUserAndPutsVote()
    {
        const string connectionDataJson =
            """{ "authenticatedUser": { "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb", "providerDisplayName": "Sebastian" } }""";
        const string reviewerJson =
            """{ "displayName": "Sebastian", "uniqueName": "sebastian@example.local", "vote": 10 }""";
        using var connectionData = JsonResponse(connectionDataJson);
        using var reviewer = JsonResponse(reviewerJson);
        var client = CreateClient(out var handler, connectionData, reviewer);

        var result = await client.SetPullRequestVoteAsync(
            "WebApp",
            7,
            10,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(10, result.Vote);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/DefaultCollection/_apis/connectionData", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullRequests/7/reviewers/0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb?api-version=7.0",
            handler.Requests[1].RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal(10, body.RootElement.GetProperty("vote").GetInt32());
    }

    [Fact]
    public async Task UpdatePullRequestStatusAsync_Abandon_SendsSinglePatch()
    {
        const string json =
            """
            {
              "pullRequestId": 7,
              "title": "Add feature",
              "status": "abandoned",
              "sourceRefName": "refs/heads/develop",
              "targetRefName": "refs/heads/main"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var result = await client.UpdatePullRequestStatusAsync(
            "WebApp",
            7,
            "abandoned",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("abandoned", result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("status").ValueEquals("abandoned"));
        Assert.False(body.RootElement.TryGetProperty("lastMergeSourceCommit", out _));
    }

    [Fact]
    public async Task UpdatePullRequestStatusAsync_Complete_IncludesMergeSourceCommit()
    {
        const string pullRequestJson =
            """
            {
              "pullRequestId": 7,
              "title": "Add feature",
              "status": "active",
              "sourceRefName": "refs/heads/develop",
              "targetRefName": "refs/heads/main",
              "lastMergeSourceCommit": { "commitId": "abc123def456" }
            }
            """;
        const string completedJson =
            """
            {
              "pullRequestId": 7,
              "title": "Add feature",
              "status": "completed",
              "sourceRefName": "refs/heads/develop",
              "targetRefName": "refs/heads/main"
            }
            """;
        using var pullRequest = JsonResponse(pullRequestJson);
        using var completed = JsonResponse(completedJson);
        var client = CreateClient(out var handler, pullRequest, completed);

        var result = await client.UpdatePullRequestStatusAsync(
            "WebApp",
            7,
            "completed",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("completed", result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Patch, handler.Requests[1].Method);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(
            body.RootElement.GetProperty("lastMergeSourceCommit").GetProperty("commitId").ValueEquals("abc123def456")
        );
    }

    [Fact]
    public async Task UpdatePullRequestStatusAsync_WithInvalidStatus_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.UpdatePullRequestStatusAsync(
                "WebApp",
                7,
                "merged",
                "Alpha",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("active, abandoned, completed", exception.Message);
        Assert.Empty(handler.Requests);
    }


    [Fact]
    public async Task GetPullRequestsAsync_AppendsTop()
    {
        using var response = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, response);

        await client.GetPullRequestsAsync(
            "WebApp",
            "Alpha",
            null,
            25,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("$top=25", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

}
