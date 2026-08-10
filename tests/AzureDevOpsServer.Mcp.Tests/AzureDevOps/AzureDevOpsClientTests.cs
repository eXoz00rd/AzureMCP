using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class AzureDevOpsClientTests
{
    private static AzureDevOpsClient CreateClient(
        out StubHttpMessageHandler handler,
        params HttpResponseMessage[] responses)
    {
        handler = new StubHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://devops.example.local/DefaultCollection/")
        };
        var options = Options.Create(
            new AzureDevOpsServerOptions
            {
                CollectionUrl = "https://devops.example.local/DefaultCollection",
                PersonalAccessToken = "pat-value"
            }
        );
        return new AzureDevOpsClient(httpClient, options);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task GetProjectsAsync_WithSinglePage_ReturnsProjects()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                {
                  "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb",
                  "name": "Alpha",
                  "description": "First project",
                  "state": "wellFormed",
                  "url": "https://devops.example.local/DefaultCollection/_apis/projects/0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb"
                },
                {
                  "id": "b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa",
                  "name": "Beta",
                  "state": "wellFormed",
                  "url": "https://devops.example.local/DefaultCollection/_apis/projects/b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa"
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var projects = await client.GetProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, projects.Count);
        Assert.Equal("Alpha", projects[0].Name);
        Assert.Equal("First project", projects[0].Description);
        Assert.Null(projects[1].Description);
        Assert.EndsWith(
            "_apis/projects?api-version=7.0&$top=100",
            Assert.Single(handler.Requests).RequestUri!.ToString()
        );
    }

    [Fact]
    public async Task GetProjectsAsync_WithContinuationToken_FetchesAllPages()
    {
        const string firstPage =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb",
                  "name": "Alpha",
                  "state": "wellFormed",
                  "url": "https://devops.example.local/DefaultCollection/_apis/projects/0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb"
                }
              ]
            }
            """;
        const string secondPage =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": "b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa",
                  "name": "Beta",
                  "state": "wellFormed",
                  "url": "https://devops.example.local/DefaultCollection/_apis/projects/b3f11a5c-9d24-4c5e-8a4f-2f47c1d0e9aa"
                }
              ]
            }
            """;
        using var first = JsonResponse(firstPage);
        first.Headers.Add("x-ms-continuationtoken", "token-123");
        using var second = JsonResponse(secondPage);
        var client = CreateClient(out var handler, first, second);

        var projects = await client.GetProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, projects.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("continuationToken=token-123", handler.Requests[1].RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
    public async Task GetProjectsAsync_WithAuthenticationFailure_ThrowsWithPatHint(HttpStatusCode statusCode)
    {
        using var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("<html>Sign in</html>")
        };
        var client = CreateClient(out _, response);

        var exception =
            await Assert.ThrowsAsync<AzureDevOpsClientException>(()
                => client.GetProjectsAsync(TestContext.Current.CancellationToken)
            );

        Assert.Contains("PAT", exception.Message);
    }

    [Fact]
    public async Task GetProjectsAsync_WithServerError_ThrowsWithStatusCode()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom")
        };
        var client = CreateClient(out _, response);

        var exception =
            await Assert.ThrowsAsync<AzureDevOpsClientException>(()
                => client.GetProjectsAsync(TestContext.Current.CancellationToken)
            );

        Assert.Contains("500", exception.Message);
        Assert.Contains("boom", exception.Message);
    }

    [Fact]
    public async Task QueryWorkItemsAsync_WithProject_PostsToProjectScope()
    {
        const string json =
            """
            {
              "queryType": "flat",
              "workItems": [
                { "id": 42, "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/42" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var result = await client.QueryWorkItemsAsync(
            "SELECT [System.Id] FROM WorkItems",
            "Alpha Project",
            TestContext.Current.CancellationToken
        );

        var reference = Assert.Single(result.WorkItems);
        Assert.Equal(42, reference.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("Alpha%20Project/_apis/wit/wiql?api-version=7.0", request.RequestUri!.AbsoluteUri);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.True(body.RootElement.GetProperty("query").ValueEquals("SELECT [System.Id] FROM WorkItems"));
    }

    [Fact]
    public async Task QueryWorkItemsAsync_WithoutProject_PostsToCollectionScope()
    {
        using var response = JsonResponse("""{ "queryType": "flat", "workItems": [] }""");
        var client = CreateClient(out var handler, response);

        var result = await client.QueryWorkItemsAsync(
            "SELECT [System.Id] FROM WorkItems",
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(result.WorkItems);
        Assert.EndsWith(
            "/DefaultCollection/_apis/wit/wiql?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.ToString()
        );
    }

    [Fact]
    public async Task GetWorkItemAsync_ReturnsWorkItemWithFields()
    {
        const string json =
            """
            {
              "id": 42,
              "rev": 3,
              "fields": {
                "System.Title": "Fix login bug",
                "System.State": "Active",
                "Microsoft.VSTS.Scheduling.StoryPoints": 5
              },
              "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/42"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var workItem = await client.GetWorkItemAsync(42, TestContext.Current.CancellationToken);

        Assert.Equal(42, workItem.Id);
        Assert.Equal(3, workItem.Rev);
        Assert.True(workItem.Fields["System.Title"].ValueEquals("Fix login bug"));
        Assert.Equal(5, workItem.Fields["Microsoft.VSTS.Scheduling.StoryPoints"].GetInt32());
        Assert.EndsWith(
            "_apis/wit/workitems/42?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.ToString()
        );
    }

    [Fact]
    public async Task GetRepositoriesAsync_WithoutProject_ListsCollectionRepositories()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": "3f9a1c2b-6d7e-4f80-9a1b-2c3d4e5f6a7b",
                  "name": "WebApp",
                  "defaultBranch": "refs/heads/main",
                  "remoteUrl": "https://devops.example.local/DefaultCollection/Alpha/_git/WebApp"
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var repositories = await client.GetRepositoriesAsync(null, TestContext.Current.CancellationToken);

        var repository = Assert.Single(repositories);
        Assert.Equal("WebApp", repository.Name);
        Assert.Equal("refs/heads/main", repository.DefaultBranch);
        Assert.EndsWith(
            "/DefaultCollection/_apis/git/repositories?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetRepositoriesAsync_WithProject_UsesProjectScope()
    {
        using var response = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, response);

        var repositories = await client.GetRepositoriesAsync("Alpha Project", TestContext.Current.CancellationToken);

        Assert.Empty(repositories);
        Assert.EndsWith(
            "Alpha%20Project/_apis/git/repositories?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetBranchesAsync_ReturnsHeadRefs()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "name": "refs/heads/main", "objectId": "a1b2c3d4" },
                { "name": "refs/heads/develop", "objectId": "e5f6a7b8" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var branches = await client.GetBranchesAsync("WebApp", "Alpha", TestContext.Current.CancellationToken);

        Assert.Equal(2, branches.Count);
        Assert.Equal("refs/heads/main", branches[0].Name);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/refs?filter=heads/&api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetFileContentAsync_WithBranch_RequestsVersionDescriptor()
    {
        const string json =
            """
            {
              "objectId": "a1b2c3d4",
              "path": "/README.md",
              "content": "# Hello"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var item = await client.GetFileContentAsync(
            "WebApp",
            "/README.md",
            "develop",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/README.md", item.Path);
        Assert.Equal("# Hello", item.Content);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("path=%2FREADME.md", requestUri);
        Assert.Contains("includeContent=true", requestUri);
        Assert.Contains("$format=json", requestUri);
        Assert.Contains("versionDescriptor.version=develop&versionDescriptor.versionType=branch", requestUri);
    }

    [Fact]
    public async Task GetFileContentAsync_WithoutBranch_OmitsVersionDescriptor()
    {
        using var response = JsonResponse("""{ "objectId": "a1b2c3d4", "path": "/README.md", "content": "# Hello" }""");
        var client = CreateClient(out var handler, response);

        await client.GetFileContentAsync(
            "WebApp",
            "/README.md",
            null,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain("versionDescriptor", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CreateWorkItemAsync_PostsJsonPatchToTypedUrl()
    {
        const string json =
            """
            {
              "id": 100,
              "rev": 1,
              "fields": { "System.Title": "New bug" },
              "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/100"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);
        var fields = new Dictionary<string, string>
        {
            ["System.Title"] = "New bug"
        };

        var workItem = await client.CreateWorkItemAsync("Alpha", "Bug", fields, TestContext.Current.CancellationToken);

        Assert.Equal(100, workItem.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("Alpha/_apis/wit/workitems/$Bug?api-version=7.0", request.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var operation = Assert.Single(body.RootElement.EnumerateArray());
        Assert.True(operation.GetProperty("op").ValueEquals("add"));
        Assert.True(operation.GetProperty("path").ValueEquals("/fields/System.Title"));
        Assert.True(operation.GetProperty("value").ValueEquals("New bug"));
    }

    [Fact]
    public async Task CreateWorkItemAsync_WithoutProject_Throws()
    {
        var client = CreateClient(out var handler);
        var fields = new Dictionary<string, string>
        {
            ["System.Title"] = "New bug"
        };

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.CreateWorkItemAsync(null, "Bug", fields, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ADOS_DEFAULT_PROJECT", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UpdateWorkItemAsync_SendsPatchOperations()
    {
        const string json =
            """
            {
              "id": 42,
              "rev": 4,
              "fields": { "System.State": "Resolved" },
              "url": "https://devops.example.local/DefaultCollection/_apis/wit/workItems/42"
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);
        var fields = new Dictionary<string, string>
        {
            ["System.State"] = "Resolved"
        };

        var workItem = await client.UpdateWorkItemAsync(42, fields, TestContext.Current.CancellationToken);

        Assert.Equal(4, workItem.Rev);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.EndsWith("_apis/wit/workitems/42?api-version=7.0", request.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var operation = Assert.Single(body.RootElement.EnumerateArray());
        Assert.True(operation.GetProperty("path").ValueEquals("/fields/System.State"));
        Assert.True(operation.GetProperty("value").ValueEquals("Resolved"));
    }

    [Fact]
    public async Task UpdateWorkItemAsync_WithEmptyFields_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.UpdateWorkItemAsync(42, new Dictionary<string, string>(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("At least one field", exception.Message);
        Assert.Empty(handler.Requests);
    }

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
            TestContext.Current.CancellationToken
        );

        var pullRequest = Assert.Single(pullRequests);
        Assert.Equal(7, pullRequest.PullRequestId);
        Assert.Equal("refs/heads/develop", pullRequest.SourceRefName);
        Assert.Equal("Sebastian", pullRequest.CreatedBy!.DisplayName);
        Assert.EndsWith(
            "Alpha/_apis/git/repositories/WebApp/pullrequests?searchCriteria.status=active&api-version=7.0",
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
    public async Task GetBuildDefinitionsAsync_ReturnsDefinitions()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "id": 12, "name": "CI", "path": "\\Pipelines" },
                { "id": 13, "name": "Nightly", "path": "\\" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var definitions = await client.GetBuildDefinitionsAsync("Alpha", TestContext.Current.CancellationToken);

        Assert.Equal(2, definitions.Count);
        Assert.Equal("CI", definitions[0].Name);
        Assert.EndsWith(
            "Alpha/_apis/build/definitions?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetBuildDefinitionsAsync_WithoutProject_Throws()
    {
        var client = CreateClient(out var handler);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(()
            => client.GetBuildDefinitionsAsync(null, TestContext.Current.CancellationToken)
        );

        Assert.Contains("ADOS_DEFAULT_PROJECT", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetBuildsAsync_WithDefinitionFilter_AppendsQueryParameters()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": 500,
                  "buildNumber": "20260810.1",
                  "status": "completed",
                  "result": "succeeded",
                  "sourceBranch": "refs/heads/main",
                  "definition": { "id": 12, "name": "CI" },
                  "queueTime": "2026-08-10T10:00:00Z",
                  "finishTime": "2026-08-10T10:05:00Z"
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var builds = await client.GetBuildsAsync("Alpha", 12, 20, TestContext.Current.CancellationToken);

        var build = Assert.Single(builds);
        Assert.Equal("20260810.1", build.BuildNumber);
        Assert.Equal("succeeded", build.Result);
        Assert.Equal(12, build.Definition!.Id);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("$top=20", requestUri);
        Assert.Contains("definitions=12", requestUri);
        Assert.Contains("Alpha/_apis/build/builds", requestUri);
    }

    [Fact]
    public async Task QueueBuildAsync_PostsDefinitionAndNormalizedBranch()
    {
        const string json =
            """
            {
              "id": 501,
              "buildNumber": "20260810.2",
              "status": "notStarted",
              "sourceBranch": "refs/heads/develop",
              "definition": { "id": 12, "name": "CI" }
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var build = await client.QueueBuildAsync("Alpha", 12, "develop", TestContext.Current.CancellationToken);

        Assert.Equal(501, build.Id);
        Assert.Null(build.Result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith(
            "Alpha/_apis/build/builds?api-version=7.0",
            request.RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal(12, body.RootElement.GetProperty("definition").GetProperty("id").GetInt32());
        Assert.True(body.RootElement.GetProperty("sourceBranch").ValueEquals("refs/heads/develop"));
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

    [Fact]
    public async Task GetReleaseDefinitionsAsync_ReturnsDefinitions()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [ { "id": 3, "name": "Deploy to production", "path": "\\" } ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var definitions = await client.GetReleaseDefinitionsAsync("Alpha", TestContext.Current.CancellationToken);

        var definition = Assert.Single(definitions);
        Assert.Equal("Deploy to production", definition.Name);
        Assert.EndsWith(
            "Alpha/_apis/release/definitions?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetReleasesAsync_WithDefinitionFilter_AppendsQueryParameters()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": 42,
                  "name": "Release-15",
                  "status": "active",
                  "createdOn": "2026-08-11T08:00:00Z",
                  "releaseDefinition": { "id": 3, "name": "Deploy to production" }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var releases = await client.GetReleasesAsync("Alpha", 3, 20, TestContext.Current.CancellationToken);

        var release = Assert.Single(releases);
        Assert.Equal("Release-15", release.Name);
        Assert.Equal(3, release.ReleaseDefinition!.Id);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("$top=20", requestUri);
        Assert.Contains("definitionId=3", requestUri);
        Assert.Contains("Alpha/_apis/release/releases", requestUri);
    }

    [Fact]
    public async Task GetReleaseAsync_ReturnsEnvironmentStatuses()
    {
        const string json =
            """
            {
              "id": 42,
              "name": "Release-15",
              "status": "active",
              "environments": [
                { "id": 1, "name": "Staging", "status": "succeeded" },
                { "id": 2, "name": "Production", "status": "inProgress" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var release = await client.GetReleaseAsync("Alpha", 42, TestContext.Current.CancellationToken);

        Assert.Equal(2, release.Environments!.Count);
        Assert.Equal("succeeded", release.Environments[0].Status);
        Assert.Equal("Production", release.Environments[1].Name);
        Assert.EndsWith(
            "Alpha/_apis/release/releases/42?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task CreateReleaseAsync_PostsDefinitionAndDescription()
    {
        const string json =
            """{ "id": 43, "name": "Release-16", "status": "active" }""";
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var release = await client.CreateReleaseAsync(
            "Alpha",
            3,
            "Hotfix deployment",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(43, release.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith(
            "Alpha/_apis/release/releases?api-version=7.0",
            request.RequestUri!.AbsoluteUri
        );
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal(3, body.RootElement.GetProperty("definitionId").GetInt32());
        Assert.True(body.RootElement.GetProperty("description").ValueEquals("Hotfix deployment"));
    }

    [Fact]
    public async Task GetBuildTimelineAsync_ReturnsRecordsWithIssues()
    {
        const string json =
            """
            {
              "records": [
                {
                  "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb",
                  "type": "Stage",
                  "name": "Build",
                  "state": "completed",
                  "result": "failed",
                  "errorCount": 1,
                  "warningCount": 0,
                  "log": { "id": 7 },
                  "issues": [ { "type": "error", "message": "CS1002: ; expected" } ]
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var records = await client.GetBuildTimelineAsync("Alpha", 500, TestContext.Current.CancellationToken);

        var record = Assert.Single(records);
        Assert.Equal("failed", record.Result);
        Assert.Equal(7, record.Log!.Id);
        Assert.Equal("CS1002: ; expected", Assert.Single(record.Issues!).Message);
        Assert.EndsWith(
            "Alpha/_apis/build/builds/500/timeline?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetBuildLogAsync_WithLineRange_AppendsQueryParameters()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("##[error]CS1002: ; expected\nBuild failed.")
        };
        var client = CreateClient(out var handler, response);

        var log = await client.GetBuildLogAsync(
            "Alpha",
            500,
            7,
            10,
            50,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("CS1002", log);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("builds/500/logs/7", requestUri);
        Assert.Contains("startLine=10", requestUri);
        Assert.Contains("endLine=50", requestUri);
    }

    [Fact]
    public async Task GetBuildLogAsync_WithoutLineRange_OmitsQueryParameters()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("full log")
        };
        var client = CreateClient(out var handler, response);

        var log = await client.GetBuildLogAsync(
            "Alpha",
            500,
            7,
            null,
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("full log", log);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.DoesNotContain("startLine", requestUri);
        Assert.DoesNotContain("endLine", requestUri);
    }

    [Fact]
    public async Task GetBuildArtifactsAsync_ReturnsArtifacts()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "id": 1,
                  "name": "drop",
                  "resource": {
                    "type": "Container",
                    "downloadUrl": "https://devops.example.local/DefaultCollection/_apis/resources/Containers/10?itemPath=drop&%24format=zip"
                  }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var artifacts = await client.GetBuildArtifactsAsync("Alpha", 500, TestContext.Current.CancellationToken);

        var artifact = Assert.Single(artifacts);
        Assert.Equal("drop", artifact.Name);
        Assert.Contains("format=zip", artifact.Resource!.DownloadUrl);
        Assert.EndsWith(
            "Alpha/_apis/build/builds/500/artifacts?api-version=7.0",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetCommitsAsync_WithBranchAndPath_AppendsSearchCriteria()
    {
        const string json =
            """
            {
              "count": 1,
              "value": [
                {
                  "commitId": "abc123def456",
                  "comment": "Fix login bug",
                  "author": { "name": "Sebastian", "email": "sebastian@example.local", "date": "2026-08-11T09:00:00Z" }
                }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var commits = await client.GetCommitsAsync(
            "WebApp",
            "refs/heads/develop",
            "/src",
            20,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        var commit = Assert.Single(commits);
        Assert.Equal("Fix login bug", commit.Comment);
        Assert.Equal("Sebastian", commit.Author!.Name);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("searchCriteria.$top=20", requestUri);
        Assert.Contains("searchCriteria.itemVersion.version=develop", requestUri);
        Assert.Contains("searchCriteria.itemPath=%2Fsrc", requestUri);
    }

    [Fact]
    public async Task GetCommitAsync_ReturnsMetadataAndChanges()
    {
        const string commitJson =
            """
            {
              "commitId": "abc123def456",
              "comment": "Fix login bug",
              "author": { "name": "Sebastian", "email": "sebastian@example.local", "date": "2026-08-11T09:00:00Z" }
            }
            """;
        const string changesJson =
            """
            {
              "changes": [
                { "changeType": "edit", "item": { "path": "/src/Login.cs" } },
                { "changeType": "add", "item": { "path": "/tests/LoginTests.cs" } }
              ]
            }
            """;
        using var commit = JsonResponse(commitJson);
        using var changes = JsonResponse(changesJson);
        var client = CreateClient(out var handler, commit, changes);

        var details = await client.GetCommitAsync(
            "WebApp",
            "abc123def456",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("Fix login bug", details.Commit.Comment);
        Assert.Equal(2, details.Changes.Count);
        Assert.Equal("/src/Login.cs", details.Changes[0].Item.Path);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith(
            "commits/abc123def456?api-version=7.0",
            handler.Requests[0].RequestUri!.AbsoluteUri
        );
        Assert.EndsWith(
            "commits/abc123def456/changes?api-version=7.0",
            handler.Requests[1].RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetRepositoryItemsAsync_DefaultsToOneLevel()
    {
        const string json =
            """
            {
              "count": 2,
              "value": [
                { "path": "/src", "isFolder": true, "gitObjectType": "tree" },
                { "path": "/README.md", "gitObjectType": "blob" }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var items = await client.GetRepositoryItemsAsync(
            "WebApp",
            null,
            "develop",
            false,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsFolder);
        Assert.Null(items[1].IsFolder);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("scopePath=%2F", requestUri);
        Assert.Contains("recursionLevel=oneLevel", requestUri);
        Assert.Contains("versionDescriptor.version=develop", requestUri);
    }

    [Fact]
    public async Task GetRepositoryItemsAsync_Recursive_UsesFullRecursion()
    {
        using var response = JsonResponse("""{ "count": 0, "value": [] }""");
        var client = CreateClient(out var handler, response);

        await client.GetRepositoryItemsAsync(
            "WebApp",
            "/src",
            null,
            true,
            "Alpha",
            TestContext.Current.CancellationToken
        );

        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("scopePath=%2Fsrc", requestUri);
        Assert.Contains("recursionLevel=full", requestUri);
        Assert.DoesNotContain("versionDescriptor", requestUri);
    }

    [Fact]
    public async Task GetBranchDiffAsync_ReturnsAheadBehindAndChanges()
    {
        const string json =
            """
            {
              "aheadCount": 3,
              "behindCount": 1,
              "changes": [
                { "changeType": "edit", "item": { "path": "/src/Program.cs" } }
              ]
            }
            """;
        using var response = JsonResponse(json);
        var client = CreateClient(out var handler, response);

        var diffs = await client.GetBranchDiffAsync(
            "WebApp",
            "main",
            "refs/heads/develop",
            "Alpha",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, diffs.AheadCount);
        Assert.Equal(1, diffs.BehindCount);
        Assert.Equal("/src/Program.cs", Assert.Single(diffs.Changes!).Item.Path);
        var requestUri = Assert.Single(handler.Requests).RequestUri!.AbsoluteUri;
        Assert.Contains("diffs/commits", requestUri);
        Assert.Contains("baseVersion=main", requestUri);
        Assert.Contains("targetVersion=develop", requestUri);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public StubHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return _responses.Dequeue();
        }
    }
}