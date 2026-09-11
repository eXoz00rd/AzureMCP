using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AzureDevOpsServer.Mcp.Tests.Infrastructure;

/// <summary>
/// A minimal loopback HTTP server standing in for an Azure DevOps Server collection, used to
/// exercise the MCP server end to end without contacting a real instance.
/// </summary>
public sealed class StubAzureDevOpsServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _acceptLoop;
    private readonly object _requestsGate = new();
    private readonly List<string> _requestPaths = [];
    private readonly string _projectsResponse;
    private readonly string _workItemResponse;

    public StubAzureDevOpsServer()
    {
        const int maxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var port = ReserveLoopbackPort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
                _listener = listener;
                CollectionUrl = $"http://127.0.0.1:{port}/DefaultCollection";
                break;
            }
            catch (HttpListenerException) when (attempt < maxAttempts)
            {
                // Another process claimed the port between ReserveLoopbackPort() and Start(); retry with a new one.
                listener.Close();
            }
        }

        _projectsResponse =
            $$"""
            {
              "count": 1,
              "value": [
                {
                  "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb",
                  "name": "Alpha",
                  "state": "wellFormed",
                  "url": "{{CollectionUrl}}/_apis/projects/0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb"
                }
              ]
            }
            """;
        _workItemResponse =
            $$"""
            {
              "id": 1,
              "rev": 3,
              "fields": {
                "System.Title": "Sample bug",
                "System.Description": "<p>Steps to <b>reproduce</b>:</p><ul><li>Open the app</li><li>Click submit</li></ul>"
              },
              "url": "{{CollectionUrl}}/_apis/wit/workitems/1"
            }
            """;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string CollectionUrl { get; }

    public IReadOnlyList<string> RequestPaths
    {
        get
        {
            lock (_requestsGate)
            {
                return [.. _requestPaths];
            }
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                return;
            }

            var path = context.Request.Url!.AbsolutePath;
            lock (_requestsGate)
            {
                _requestPaths.Add(path);
            }

            var isKnownProjectsRequest = context.Request.HttpMethod == "GET" &&
                path.EndsWith("_apis/projects", StringComparison.Ordinal);
            // Matches only the single-work-item route this fixture actually models; a broader
            // Contains check would also (incorrectly) answer requests for revisions, comments,
            // or other work item subroutes with this same canned response.
            var isKnownWorkItemRequest = context.Request.HttpMethod == "GET" &&
                path.EndsWith("_apis/wit/workitems/1", StringComparison.OrdinalIgnoreCase);
            var responseBody = isKnownProjectsRequest ? _projectsResponse :
                isKnownWorkItemRequest ? _workItemResponse :
                "{}";
            context.Response.StatusCode = isKnownProjectsRequest || isKnownWorkItemRequest ?
                (int)HttpStatusCode.OK :
                (int)HttpStatusCode.NotFound;
            var body = Encoding.UTF8.GetBytes(responseBody);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    private static int ReserveLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: GetContextAsync().WaitAsync(_stopping.Token) throws this on shutdown.
        }

        _listener.Close();
        _stopping.Dispose();
    }
}
