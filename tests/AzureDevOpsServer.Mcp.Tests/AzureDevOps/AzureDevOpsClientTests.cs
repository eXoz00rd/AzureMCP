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