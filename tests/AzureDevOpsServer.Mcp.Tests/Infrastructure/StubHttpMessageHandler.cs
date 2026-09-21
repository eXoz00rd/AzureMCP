namespace AzureDevOpsServer.Mcp.Tests.Infrastructure;

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public StubHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestBodies { get; } = [];

    // Snapshotted per send because retries reuse the same request instance.
    public List<string?> AuthorizationHeaders { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
        if (request.Content is not null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        var response = _responses.Dequeue();
        response.RequestMessage ??= request;
        return response;
    }
}
