using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
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
    public async Task UpdatePullRequestAsync_WithTitleAndDescription_SendsOnlyProvidedFields()
    {
        using var response = JsonResponse(
            """{ "pullRequestId": 7, "title": "New title", "status": "active", "sourceRefName": "refs/heads/develop", "targetRefName": "refs/heads/main" }"""
        );
        var client = CreateClient(out var handler, response);

        await client.UpdatePullRequestAsync(
            "WebApp",
            7,
            "New title",
            "New description",
            null,
            "Alpha",
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("title").ValueEquals("New title"));
        Assert.True(body.RootElement.GetProperty("description").ValueEquals("New description"));
        Assert.False(body.RootElement.TryGetProperty("autoCompleteSetBy", out _));
    }

    [Fact]
    public async Task UpdatePullRequestAsync_WithAutoComplete_ResolvesIdentity()
    {
        using var connectionData = JsonResponse(
            """{ "authenticatedUser": { "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb" } }"""
        );
        using var response = JsonResponse(
            """{ "pullRequestId": 7, "title": "Add feature", "status": "active", "sourceRefName": "refs/heads/develop", "targetRefName": "refs/heads/main" }"""
        );
        var client = CreateClient(out var handler, connectionData, response);

        await client.UpdatePullRequestAsync("WebApp", 7, null, null, true, "Alpha", TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(
            body.RootElement.GetProperty("autoCompleteSetBy")
                .GetProperty("id")
                .ValueEquals("0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb")
        );
    }

    [Fact]
    public async Task UpdatePullRequestAsync_WithNothingToChange_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.UpdatePullRequestAsync("WebApp", 7, null, null, null, "Alpha", TestContext.Current.CancellationToken));

        Assert.Contains("At least one of title", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AddPullRequestReviewerAsync_WithIdentityId_SkipsLookup()
    {
        using var response = JsonResponse(
            """{ "displayName": "Sebastian", "uniqueName": "sebastian@example.local", "vote": 0, "isRequired": true }"""
        );
        var client = CreateClient(out var handler, response);

        var reviewer = await client.AddPullRequestReviewerAsync(
            "WebApp",
            7,
            "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb",
            true,
            "Alpha",
            TestContext.Current.CancellationToken);

        Assert.True(reviewer.IsRequired);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith(
            "pullRequests/7/reviewers/0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb?api-version=7.0",
            request.RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("isRequired").GetBoolean());
    }

    [Fact]
    public async Task AddPullRequestReviewerAsync_WithAccountName_ResolvesIdentityFirst()
    {
        using var identities = JsonResponse(
            """{ "count": 1, "value": [ { "id": "b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa", "providerDisplayName": "Sebastian" } ] }"""
        );
        using var response = JsonResponse("""{ "displayName": "Sebastian", "vote": 0 }""");
        var client = CreateClient(out var handler, identities, response);

        await client.AddPullRequestReviewerAsync(
            "WebApp",
            7,
            "sebastian@example.local",
            false,
            "Alpha",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("_apis/identities", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("filterValue=sebastian%40example.local", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.EndsWith("reviewers/b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa?api-version=7.0", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task AddPullRequestReviewerAsync_WithUnknownName_Throws()
    {
        using var identities = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, identities);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.AddPullRequestReviewerAsync(
                "WebApp",
                7,
                "ghost@example.local",
                false,
                "Alpha",
                TestContext.Current.CancellationToken));

        Assert.Contains("could not be resolved to an identity", exception.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RemovePullRequestReviewerAsync_SendsDelete()
    {
        using var connectionData = JsonResponse(
            """{ "authenticatedUser": { "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb" } }"""
        );
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var client = CreateClient(out var handler, connectionData, response);

        await client.RemovePullRequestReviewerAsync("WebApp", 7, "me", "Alpha", TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.EndsWith(
            "pullRequests/7/reviewers/0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb?api-version=7.0",
            handler.Requests[1].RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetPullRequestAsync_ReturnsReviewerVotesAndMergeState()
    {
        const string json =
            """
            {
              "pullRequestId": 7,
              "title": "Add feature",
              "status": "active",
              "sourceRefName": "refs/heads/develop",
              "targetRefName": "refs/heads/main",
              "mergeStatus": "conflicts",
              "isDraft": true,
              "creationDate": "2026-08-11T08:00:00Z",
              "reviewers": [
                { "displayName": "Sebastian", "uniqueName": "sebastian@example.local", "vote": 10, "isRequired": true },
                { "displayName": "Reviewers Team", "uniqueName": "team", "vote": 0, "isContainer": true }
              ],
              "repository": {
                "id": "3f9a1c2b-6d7e-4f80-9a1b-2c3d4e5f6a7b",
                "name": "WebApp",
                "project": { "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb", "name": "Alpha" }
              }
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out _, response);

        var pullRequest = await client.GetPullRequestAsync("WebApp", 7, "Alpha", TestContext.Current.CancellationToken);

        Assert.Equal("conflicts", pullRequest.MergeStatus);
        Assert.True(pullRequest.IsDraft);
        Assert.NotNull(pullRequest.CreationDate);
        Assert.Equal(2, pullRequest.Reviewers!.Count);
        Assert.Equal(10, pullRequest.Reviewers[0].Vote);
        Assert.True(pullRequest.Reviewers[0].IsRequired);
        Assert.True(pullRequest.Reviewers[1].IsContainer);
        Assert.Equal("Alpha", pullRequest.Repository!.Project!.Name);
    }

    [Fact]
    public async Task GetProjectPullRequestsAsync_WithoutFilters_QueriesProjectScope()
    {
        using var response = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, response);

        await client.GetProjectPullRequestsAsync("Alpha", null, false, false, 100, TestContext.Current.CancellationToken);

        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("Alpha/_apis/git/pullrequests", requestUri);
        Assert.Contains("searchCriteria.status=active", requestUri);
        Assert.Contains("$top=100", requestUri);
        Assert.DoesNotContain("creatorId", requestUri);
        Assert.DoesNotContain("reviewerId", requestUri);
    }

    [Fact]
    public async Task GetProjectPullRequestsAsync_WithUserFilters_ResolvesIdentityOnce()
    {
        using var connectionData = JsonResponse(
            """{ "authenticatedUser": { "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb" } }"""
        );
        using var pullRequests = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, connectionData, pullRequests);

        await client.GetProjectPullRequestsAsync("Alpha", "all", true, true, 50, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/_apis/connectionData", handler.Requests[0].RequestUri!.AbsoluteUri);
        var requestUri = handler.Requests[1].RequestUri!.AbsoluteUri;
        Assert.Contains("searchCriteria.status=all", requestUri);
        Assert.Contains("searchCriteria.creatorId=0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb", requestUri);
        Assert.Contains("searchCriteria.reviewerId=0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb", requestUri);
    }

    [Fact]
    public async Task GetPullRequestPolicyEvaluationsAsync_UsesCodeReviewArtifactId()
    {
        const string pullRequestJson =
            """
            {
              "pullRequestId": 7,
              "title": "Add feature",
              "status": "active",
              "sourceRefName": "refs/heads/develop",
              "targetRefName": "refs/heads/main",
              "repository": {
                "id": "3f9a1c2b-6d7e-4f80-9a1b-2c3d4e5f6a7b",
                "name": "WebApp",
                "project": { "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb", "name": "Alpha" }
              }
            }
            """;
        const string policiesJson =
            """
            {
              "count": 2,
              "value": [
                {
                  "status": "approved",
                  "configuration": { "isBlocking": true, "isEnabled": true, "type": { "displayName": "Build" } }
                },
                {
                  "status": "rejected",
                  "configuration": { "isBlocking": true, "isEnabled": true, "type": { "displayName": "Minimum number of reviewers" } }
                }
              ]
            }
            """;
        using var pullRequest = JsonResponse(pullRequestJson);
        using var policies = JsonResponse(policiesJson);
        var client = CreateClient(out var handler, pullRequest, policies);

        var evaluations = await client.GetPullRequestPolicyEvaluationsAsync(
            "WebApp",
            7,
            "Alpha",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, evaluations.Count);
        Assert.Equal("approved", evaluations[0].Status);
        Assert.Equal("Build", evaluations[0].Configuration!.Type!.DisplayName);
        Assert.True(evaluations[1].Configuration!.IsBlocking);
        var requestUri = handler.Requests[1].RequestUri!.AbsoluteUri;
        Assert.Contains("Alpha/_apis/policy/evaluations", requestUri);
        Assert.Contains(
            "artifactId=vstfs%3A%2F%2F%2FCodeReview%2FCodeReviewId%2F0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb%2F7",
            requestUri
        );
    }

    [Fact]
    public async Task GetPullRequestPolicyEvaluationsAsync_WithoutProjectOnPullRequest_Throws()
    {
        const string pullRequestJson =
            """
            {
              "pullRequestId": 7,
              "title": "Add feature",
              "status": "active",
              "sourceRefName": "refs/heads/develop",
              "targetRefName": "refs/heads/main"
            }
            """;
        using var pullRequest = JsonResponse(pullRequestJson);
        var client = CreateClient(out var handler, pullRequest);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetPullRequestPolicyEvaluationsAsync("WebApp", 7, "Alpha", TestContext.Current.CancellationToken));

        Assert.Contains("could not be resolved", exception.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetPullRequestWorkItemsAsync_ReturnsLinkedIds()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "id": "42", "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/42" },
                { "id": "43", "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/43" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var workItems = await client.GetPullRequestWorkItemsAsync(
            "WebApp",
            7,
            "Alpha",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, workItems.Count);
        Assert.Equal("42", workItems[0].Id);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullRequests/7/workitems?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task ReplyToPullRequestThreadAsync_PostsCommentToThread()
    {
        using var response = JsonResponse("""{ "id": 3, "content": "Fixed in the latest push" }""");
        var client = CreateClient(out var handler, response);

        var comment = await client.ReplyToPullRequestThreadAsync(
            "WebApp",
            7,
            10,
            "Fixed in the latest push",
            "Alpha",
            TestContext.Current.CancellationToken);

        Assert.Equal(3, comment.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullRequests/7/threads/10/comments?api-version=7.0",
            request.RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("content").ValueEquals("Fixed in the latest push"));
    }

    [Fact]
    public async Task UpdatePullRequestCommentAsync_PatchesCommentContent()
    {
        using var response = JsonResponse("""{ "id": 3, "content": "Fixed in the latest push (corrected)" }""");
        var client = CreateClient(out var handler, response);

        var comment = await client.UpdatePullRequestCommentAsync(
            "WebApp",
            7,
            10,
            3,
            "Fixed in the latest push (corrected)",
            "Alpha",
            TestContext.Current.CancellationToken);

        Assert.Equal(3, comment.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullRequests/7/threads/10/comments/3?api-version=7.0",
            request.RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("content").ValueEquals("Fixed in the latest push (corrected)"));
    }

    [Theory]
    [InlineData("fixed", "fixed")]
    [InlineData("wontfix", "wontFix")]
    [InlineData("ByDesign", "byDesign")]
    public async Task SetPullRequestThreadStatusAsync_NormalizesStatus(string input, string expected)
    {
        using var response = JsonResponse("""{ "id": 10, "status": "closed" }""");
        var client = CreateClient(out var handler, response);

        await client.SetPullRequestThreadStatusAsync(
            "WebApp",
            7,
            10,
            input,
            "Alpha",
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("status").ValueEquals(expected));
    }

    [Fact]
    public async Task SetPullRequestThreadStatusAsync_WithUnknownStatus_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.SetPullRequestThreadStatusAsync(
                "WebApp",
                7,
                10,
                "resolved",
                "Alpha",
                TestContext.Current.CancellationToken));

        Assert.Contains("wontFix", exception.Message);
        Assert.Empty(handler.Requests);
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
