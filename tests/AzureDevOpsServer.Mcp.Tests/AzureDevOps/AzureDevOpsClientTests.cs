using System.Net;
using System.Text;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class AzureDevOpsClientTests
{
    private static AzureDevOpsClient CreateClient(HttpResponseMessage response, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(response);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://devops.example.local/DefaultCollection/")
        };
        var options = Options.Create(new AzureDevOpsServerOptions
        {
            CollectionUrl = "https://devops.example.local/DefaultCollection",
            PersonalAccessToken = "pat-value"
        });
        return new AzureDevOpsClient(httpClient, options);
    }

    [Fact]
    public async Task GetProjectsAsync_WithSuccessResponse_ReturnsProjects()
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
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var client = CreateClient(response, out var handler);

        var projects = await client.GetProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, projects.Count);
        Assert.Equal("Alpha", projects[0].Name);
        Assert.Equal("First project", projects[0].Description);
        Assert.Null(projects[1].Description);
        Assert.EndsWith("_apis/projects?api-version=7.0", handler.LastRequest!.RequestUri!.ToString());
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
        var client = CreateClient(response, out _);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetProjectsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("PAT", exception.Message);
    }

    [Fact]
    public async Task GetProjectsAsync_WithServerError_ThrowsWithStatusCode()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom")
        };
        var client = CreateClient(response, out _);

        var exception = await Assert.ThrowsAsync<AzureDevOpsClientException>(
            () => client.GetProjectsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("500", exception.Message);
        Assert.Contains("boom", exception.Message);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }
}
