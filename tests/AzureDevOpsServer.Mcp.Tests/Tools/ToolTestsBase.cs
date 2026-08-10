using System.Net;
using System.Text;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public abstract class ToolTestsBase
{
    private const string CollectionUrl = "https://devops.example.local/DefaultCollection";

    protected static ToolHarness CreateHarness(string? defaultProject, params HttpResponseMessage[] responses)
    {
        var handler = new StubHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{CollectionUrl}/")
        };
        var options = Options.Create(
            new AzureDevOpsServerOptions
            {
                CollectionUrl = CollectionUrl,
                PersonalAccessToken = "pat-value",
                DefaultProject = defaultProject
            }
        );
        return new ToolHarness(new AzureDevOpsClient(httpClient, options), options, handler);
    }

    protected static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    protected static HttpResponseMessage EmptyList()
    {
        return JsonResponse("""{ "count": 0, "value": [] }""");
    }

    protected sealed record ToolHarness(
        AzureDevOpsClient Client,
        IOptions<AzureDevOpsServerOptions> Options,
        StubHttpMessageHandler Handler)
    {
        public string RequestUri => Handler.Requests[0].RequestUri!.AbsoluteUri;
    }
}
