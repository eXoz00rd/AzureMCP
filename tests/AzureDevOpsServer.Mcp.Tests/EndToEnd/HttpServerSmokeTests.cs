using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using ModelContextProtocol.Client;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.EndToEnd;

/// <summary>
/// Starts the built server assembly as a real child process with <c>ADOS_TRANSPORT=http</c> and talks
/// to it over Streamable HTTP, the way Open WebUI does, so the transport choice in Program.cs, the
/// listener, the access guard, and the real host wiring are all crossed at once.
/// </summary>
public sealed class HttpServerSmokeTests
{
    private const string PersonalAccessToken = "http-smoke-pat";
    private const string HttpToken = "http-smoke-token-7f3a";

    [Fact]
    public async Task Server_OverHttp_InitializesListsToolsAndCallsAReadTool()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var azureDevOps = new StubAzureDevOpsServer();
        var port = FreeLoopbackPort();
        var endpoint = new Uri($"http://127.0.0.1:{port}{AzureDevOpsServerOptions.DefaultHttpPath}");

        using var server = ServerProcess.Start(
            new Dictionary<string, string>
            {
                [AzureDevOpsServerOptions.CollectionUrlVariable] = azureDevOps.CollectionUrl,
                [AzureDevOpsServerOptions.PersonalAccessTokenVariable] = PersonalAccessToken,
                [AzureDevOpsServerOptions.TransportVariable] = "http",
                [AzureDevOpsServerOptions.HttpUrlVariable] = $"http://127.0.0.1:{port}",
                [AzureDevOpsServerOptions.HttpTokenVariable] = HttpToken,
                [AzureDevOpsServerOptions.LogLevelVariable] = "Trace"
            }
        );
        await server.WaitUntilListeningAsync(endpoint, cancellationToken);

        using (var http = new HttpClient())
        {
            using var anonymous = await http.PostAsync(endpoint, new StringContent("{}"), cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        await using (var client = await McpClient.CreateAsync(
                         new HttpClientTransport(
                             new HttpClientTransportOptions
                             {
                                 Endpoint = endpoint,
                                 TransportMode = HttpTransportMode.StreamableHttp,
                                 AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {HttpToken}" }
                             }
                         ),
                         cancellationToken: cancellationToken
                     ))
        {
            Assert.False(string.IsNullOrWhiteSpace(client.ServerInstructions));

            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var listProjects = Assert.Single(tools, tool => tool.Name == "list_projects");
            Assert.True(listProjects.ProtocolTool.Annotations?.ReadOnlyHint);

            var result = await client.CallToolAsync("list_projects", cancellationToken: cancellationToken);
            Assert.True(result.IsError is null or false, $"Expected a successful tool call, but IsError was {result.IsError}.");
            Assert.NotNull(result.StructuredContent);
            Assert.Contains("Alpha", result.StructuredContent.ToString());
        }

        Assert.NotEmpty(azureDevOps.AuthorizationHeaders);
        Assert.All(
            azureDevOps.AuthorizationHeaders,
            header => Assert.Equal(StubCredentialProvider.ExpectedAuthorization(PersonalAccessToken), header)
        );

        await server.StopAsync();
        Assert.NotEmpty(server.StandardError);
        Assert.DoesNotContain(server.StandardError, line => line.Contains(HttpToken, StringComparison.Ordinal));
        Assert.DoesNotContain(server.StandardError, line => line.Contains(PersonalAccessToken, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sse", "Valid values are: stdio, http.")]
    [InlineData("http", AzureDevOpsServerOptions.HttpTokenVariable)]
    public async Task Server_WithUnusableTransportSettings_RefusesToStart(string transport, string expectedMessage)
    {
        using var server = ServerProcess.Start(
            new Dictionary<string, string>
            {
                [AzureDevOpsServerOptions.CollectionUrlVariable] = "https://devops.example.local/DefaultCollection",
                [AzureDevOpsServerOptions.PersonalAccessTokenVariable] = PersonalAccessToken,
                [AzureDevOpsServerOptions.TransportVariable] = transport,
                [AzureDevOpsServerOptions.HttpUrlVariable] = $"http://127.0.0.1:{FreeLoopbackPort()}"
            }
        );

        var exitCode = await server.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(server.StandardError, line => line.Contains(expectedMessage, StringComparison.Ordinal));
    }

    private static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class ServerProcess : IDisposable
    {
        private readonly Process _process;
        private readonly ConcurrentQueue<string> _standardError = new();

        private ServerProcess(Process process)
        {
            _process = process;
        }

        public IReadOnlyCollection<string> StandardError => _standardError;

        public static ServerProcess Start(IReadOnlyDictionary<string, string> environment)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(typeof(AzureDevOpsServerOptions).Assembly.Location);

            // Settings from the machine running the tests must not leak into the server under test.
            foreach (var inherited in startInfo.Environment.Keys.Where(key => key.StartsWith("ADOS_", StringComparison.Ordinal)).ToList())
            {
                startInfo.Environment.Remove(inherited);
            }

            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }

            var process = new Process { StartInfo = startInfo };
            var server = new ServerProcess(process);
            process.ErrorDataReceived += (_, line) =>
            {
                if (line.Data is not null)
                {
                    server._standardError.Enqueue(line.Data);
                }
            };
            process.OutputDataReceived += (_, _) => { };
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            return server;
        }

        public async Task WaitUntilListeningAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var http = new HttpClient();

            while (true)
            {
                if (_process.HasExited)
                {
                    Assert.Fail($"The server exited before listening:{Environment.NewLine}{string.Join(Environment.NewLine, _standardError)}");
                }

                try
                {
                    using var response = await http.GetAsync(endpoint, timeout.Token);
                    return;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
                }
            }
        }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await _process.WaitForExitAsync(timeout.Token);
            return _process.ExitCode;
        }

        public async Task StopAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            _process.Dispose();
        }
    }
}
