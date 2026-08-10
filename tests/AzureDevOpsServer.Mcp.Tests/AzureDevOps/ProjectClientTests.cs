using System.Net;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;
public sealed class ProjectClientTests : AzureDevOpsClientTestsBase
{
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

}
