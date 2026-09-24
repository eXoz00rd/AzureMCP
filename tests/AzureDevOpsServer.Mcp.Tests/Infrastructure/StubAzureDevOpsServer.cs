using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    private readonly List<string?> _authorizationHeaders = [];
    private readonly List<string?> _cookieHeaders = [];
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
                  "description": "seen with PAT __CALLER__",
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

    // Tells tests whose PAT reached the server without the response ever containing the PAT itself.
    public static string Fingerprint(string personalAccessToken)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(personalAccessToken)))[..8];
    }

    private static string CallerFingerprint(string? authorization)
    {
        if (authorization is null || !authorization.StartsWith("Basic ", StringComparison.Ordinal))
        {
            return "none";
        }

        var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..]));
        return Fingerprint(credentials[(credentials.IndexOf(':') + 1)..]);
    }

    public IReadOnlyList<string?> AuthorizationHeaders
    {
        get
        {
            lock (_requestsGate)
            {
                return [.. _authorizationHeaders];
            }
        }
    }

    // The project list sets a session cookie naming its caller, the way a real server may, so tests can see
    // whether a cookie issued to one caller ever comes back on another caller's request.
    public IReadOnlyList<string?> CookieHeaders
    {
        get
        {
            lock (_requestsGate)
            {
                return [.. _cookieHeaders];
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
                _authorizationHeaders.Add(context.Request.Headers["Authorization"]);
                _cookieHeaders.Add(context.Request.Headers["Cookie"]);
            }

            var isKnownProjectsRequest = context.Request.HttpMethod == "GET" &&
                path.EndsWith("_apis/projects", StringComparison.Ordinal);
            // Matches only the single-work-item route this fixture actually models; a broader
            // Contains check would also (incorrectly) answer requests for revisions, comments,
            // or other work item subroutes with this same canned response.
            var isKnownWorkItemRequest = context.Request.HttpMethod == "GET" &&
                path.EndsWith("_apis/wit/workitems/1", StringComparison.OrdinalIgnoreCase);
            context.Response.StatusCode = isKnownProjectsRequest || isKnownWorkItemRequest ?
                (int)HttpStatusCode.OK :
                (int)HttpStatusCode.NotFound;
            if (isKnownProjectsRequest)
            {
                context.Response.AppendHeader(
                    "Set-Cookie",
                    $"Session-of={CallerFingerprint(context.Request.Headers["Authorization"])}; Path=/"
                );
            }

            var responseText = isKnownProjectsRequest ?
                _projectsResponse.Replace("__CALLER__", CallerFingerprint(context.Request.Headers["Authorization"]), StringComparison.Ordinal) :
                isKnownWorkItemRequest ? _workItemResponse :
                "{}";
            if (path.EndsWith("_apis/wit/workitems/42", StringComparison.Ordinal))
            {
                responseText = """{"id":42,"rev":4,"fields":{"System.State":"Resolved"},"url":"https://example.test/42"}""";
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                if (context.Request.HttpMethod == "PATCH")
                {
                    using var document = await JsonDocument.ParseAsync(context.Request.InputStream);
                    var first = document.RootElement[0];
                    if (first.GetProperty("op").ValueEquals("test") &&
                        (!first.GetProperty("path").ValueEquals("/rev") ||
                        first.GetProperty("value").GetInt32() != 3))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        responseText = """{"message":"Revision test failed","typeKey":"WorkItemRevisionMismatchException"}""";
                    }
                }
            }

            if (context.Request.HttpMethod == "GET" && TryBuildReadToolResponse(context.Request, out var readToolResponse))
            {
                responseText = readToolResponse;
                context.Response.StatusCode = (int)HttpStatusCode.OK;
            }

            var body = Encoding.UTF8.GetBytes(responseText);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    // Models larger-than-a-small-model's-context answers for the tools that bound their responses: a wiki
    // of twelve pages in three sections and a twelve-line page.
    private static bool TryBuildReadToolResponse(HttpListenerRequest request, out string body)
    {
        var path = request.Url!.AbsolutePath;
        body = string.Empty;

        if (path.EndsWith("/_apis/wiki/wikis/Alpha.wiki/pages", StringComparison.Ordinal))
        {
            body = request.QueryString["recursionLevel"] == "full" ?
                WikiTreeJson() :
                $$"""{ "path": "{{request.QueryString["path"]}}", "content": {{JsonSerializer.Serialize(WikiPageText())}} }""";
            return true;
        }

        return false;
    }

    private static string WikiTreeJson()
    {
        var sections = Enumerable.Range(1, 3).Select(section =>
        {
            var pages = string.Join(", ", Enumerable.Range(1, 3).Select(page => $$"""{ "path": "/Section{{section}}/Page{{page}}" }"""));
            return $$"""{ "path": "/Section{{section}}", "subPages": [ {{pages}} ] }""";
        });
        return $$"""{ "path": "/", "subPages": [ {{string.Join(", ", sections)}} ] }""";
    }

    private static string WikiPageText()
    {
        return string.Concat(Enumerable.Range(1, 12).Select(line => $"Line {line:00}\n"));
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
