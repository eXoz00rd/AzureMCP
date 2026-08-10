using System.Net;
using System.Text;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public abstract class AzureDevOpsClientTestsBase
{
    protected const string CollectionUrl = "https://devops.example.local/DefaultCollection";

    protected static AzureDevOpsClient CreateClient(
        out StubHttpMessageHandler handler,
        params HttpResponseMessage[] responses)
    {
        return CreateClient(CreateOptions(null), out handler, responses);
    }

    protected static AzureDevOpsClient CreateClient(
        IOptions<AzureDevOpsServerOptions> options,
        out StubHttpMessageHandler handler,
        params HttpResponseMessage[] responses)
    {
        handler = new StubHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{CollectionUrl}/")
        };
        return new AzureDevOpsClient(httpClient, options);
    }

    protected static IOptions<AzureDevOpsServerOptions> CreateOptions(string? defaultProject)
    {
        return Options.Create(
            new AzureDevOpsServerOptions
            {
                CollectionUrl = CollectionUrl,
                PersonalAccessToken = "pat-value",
                DefaultProject = defaultProject
            }
        );
    }

    protected static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
