using System.Net;
using System.Text;
using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;
public abstract class AzureDevOpsClientTestsBase
{
    protected static AzureDevOpsClient CreateClient(
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

    protected static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    protected sealed class StubHttpMessageHandler : HttpMessageHandler
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
