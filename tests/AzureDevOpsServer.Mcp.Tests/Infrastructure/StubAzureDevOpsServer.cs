using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AzureDevOpsServer.Mcp.Tests.Infrastructure;

/// <summary>
/// A minimal loopback HTTP server standing in for an Azure DevOps Server collection, used to
/// exercise the packaged MCP server end to end without contacting a real instance.
/// </summary>
public sealed class StubAzureDevOpsServer : IAsyncDisposable
{
    private const string ProjectsResponse =
        """
        {
          "count": 1,
          "value": [
            {
              "id": "0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb",
              "name": "Alpha",
              "state": "wellFormed",
              "url": "http://127.0.0.1/DefaultCollection/_apis/projects/0fa87caa-7f30-4f8c-9e33-63b06f4a2fdb"
            }
          ]
        }
        """;

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _acceptLoop;
    private readonly object _requestsGate = new();
    private readonly List<string> _requestPaths = [];

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

            lock (_requestsGate)
            {
                _requestPaths.Add(context.Request.Url!.AbsolutePath);
            }

            var body = Encoding.UTF8.GetBytes(ProjectsResponse);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            context.Response.OutputStream.Close();
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
        }

        _listener.Close();
        _stopping.Dispose();
    }
}
