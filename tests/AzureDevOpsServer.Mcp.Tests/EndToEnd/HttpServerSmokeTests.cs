using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AzureDevOpsServer.Mcp.AzureDevOps;
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
    private const string AlicePersonalAccessToken = "http-smoke-pat-alice";
    private const string BobPersonalAccessToken = "http-smoke-pat-bob";
    private const string HttpToken = "http-smoke-token-7f3a";

    [Fact]
    public async Task Server_OverHttp_CallsAzureDevOpsWithEachCallersOwnPersonalAccessToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var azureDevOps = new StubAzureDevOpsServer();

        using var server = await ServerProcess.StartListeningAsync(
            new Dictionary<string, string>
            {
                [AzureDevOpsServerOptions.CollectionUrlVariable] = azureDevOps.CollectionUrl,
                [AzureDevOpsServerOptions.TransportVariable] = "http",
                [AzureDevOpsServerOptions.HttpTokenVariable] = HttpToken,
                [AzureDevOpsServerOptions.LogLevelVariable] = "Trace"
            },
            cancellationToken
        );
        var endpoint = server.Endpoint;

        using (var http = new HttpClient())
        {
            // A well-formed MCP request, so routing selects the endpoint and the guard is what answers.
            using var anonymous = await http.PostAsync(
                endpoint,
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                cancellationToken
            );
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        await using (var alice = await ConnectAsync(endpoint, AlicePersonalAccessToken, cancellationToken))
        {
            Assert.False(string.IsNullOrWhiteSpace(alice.ServerInstructions));

            var tools = await alice.ListToolsAsync(cancellationToken: cancellationToken);
            var listProjects = Assert.Single(tools, tool => tool.Name == "list_projects");
            Assert.True(listProjects.ProtocolTool.Annotations?.ReadOnlyHint);

            var result = await alice.CallToolAsync("list_projects", cancellationToken: cancellationToken);
            Assert.True(result.IsError is null or false, $"Expected a successful tool call, but IsError was {result.IsError}.");
            Assert.NotNull(result.StructuredContent);
            Assert.Contains("Alpha", result.StructuredContent.ToString());
        }

        await using (var bob = await ConnectAsync(endpoint, BobPersonalAccessToken, cancellationToken))
        {
            var result = await bob.CallToolAsync("list_projects", cancellationToken: cancellationToken);
            Assert.True(result.IsError is null or false, $"Expected a successful tool call, but IsError was {result.IsError}.");
        }

        await using (var nobody = await ConnectAsync(endpoint, personalAccessToken: null, cancellationToken))
        {
            var result = await nobody.CallToolAsync("list_projects", cancellationToken: cancellationToken);
            Assert.True(result.IsError);
            Assert.Contains(RequestCredentialProvider.HeaderName, System.Text.Json.JsonSerializer.Serialize(result.Content));

            // A tool that never calls Azure DevOps needs no PAT at all.
            var info = await nobody.CallToolAsync("server_info", cancellationToken: cancellationToken);
            Assert.True(info.IsError is null or false, $"Expected server_info to succeed without a PAT, but IsError was {info.IsError}.");
            Assert.Contains(azureDevOps.CollectionUrl, info.StructuredContent?.ToString());
        }

        // Each caller reached Azure DevOps as themselves, and the caller without a PAT never reached it at all.
        Assert.Equal(
            new[]
            {
                StubCredentialProvider.ExpectedAuthorization(AlicePersonalAccessToken),
                StubCredentialProvider.ExpectedAuthorization(BobPersonalAccessToken)
            },
            azureDevOps.AuthorizationHeaders.Distinct()
        );

        await server.StopAsync();
        Assert.NotEmpty(server.StandardError);
        foreach (var secret in new[] { HttpToken, AlicePersonalAccessToken, BobPersonalAccessToken })
        {
            Assert.DoesNotContain(server.StandardError, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("sse", false, false, "Valid values are: stdio, http.")]
    [InlineData("http", false, false, AzureDevOpsServerOptions.HttpTokenVariable)]
    [InlineData("http", true, true, "ADOS_PAT is not used when ADOS_TRANSPORT is http")]
    public async Task Server_WithUnusableTransportSettings_RefusesToStart(
        string transport,
        bool withHttpToken,
        bool withSharedPersonalAccessToken,
        string expectedMessage)
    {
        var environment = new Dictionary<string, string>
        {
            [AzureDevOpsServerOptions.CollectionUrlVariable] = "https://devops.example.local/DefaultCollection",
            [AzureDevOpsServerOptions.TransportVariable] = transport,
            [AzureDevOpsServerOptions.HttpUrlVariable] = $"http://127.0.0.1:{FreeLoopbackPort()}"
        };
        if (withHttpToken)
        {
            environment[AzureDevOpsServerOptions.HttpTokenVariable] = HttpToken;
        }

        if (withSharedPersonalAccessToken)
        {
            environment[AzureDevOpsServerOptions.PersonalAccessTokenVariable] = AlicePersonalAccessToken;
        }

        using var server = ServerProcess.Start(environment);

        var exitCode = await server.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(server.StandardError, line => line.Contains(expectedMessage, StringComparison.Ordinal));
    }

    private static Task<McpClient> ConnectAsync(Uri endpoint, string? personalAccessToken, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {HttpToken}" };
        if (personalAccessToken is not null)
        {
            headers[RequestCredentialProvider.HeaderName] = personalAccessToken;
        }

        return McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = headers
                }
            ),
            cancellationToken: cancellationToken
        );
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
        private Uri? _endpoint;

        private ServerProcess(Process process)
        {
            _process = process;
        }

        public IReadOnlyCollection<string> StandardError => _standardError;

        public Uri Endpoint => _endpoint ?? throw new InvalidOperationException("The server was not started with a listening endpoint.");

        // Another process can take a free port between choosing it and the server binding it, so a lost port is retried with a new one.
        public static async Task<ServerProcess> StartListeningAsync(
            Dictionary<string, string> environment,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                var port = FreeLoopbackPort();
                environment[AzureDevOpsServerOptions.HttpUrlVariable] = $"http://127.0.0.1:{port}";
                var server = Start(environment);
                server._endpoint = new Uri($"http://127.0.0.1:{port}{AzureDevOpsServerOptions.DefaultHttpPath}");

                if (await server.TryWaitUntilListeningAsync(cancellationToken))
                {
                    return server;
                }

                var output = string.Join(Environment.NewLine, server._standardError);
                server.Dispose();
                if (attempt == 3 || !output.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Fail($"The server exited before listening:{Environment.NewLine}{output}");
                }
            }
        }

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

        private async Task<bool> TryWaitUntilListeningAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var http = new HttpClient();

            while (true)
            {
                if (_process.HasExited)
                {
                    _process.WaitForExit();
                    return false;
                }

                // A socket that accepts a connection but never answers must not stall the wait, so each probe gets its own deadline.
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                probe.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    using var response = await http.GetAsync(Endpoint, probe.Token);
                    return !_process.HasExited;
                }
                catch (HttpRequestException)
                {
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
                {
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await _process.WaitForExitAsync(timeout.Token);

            // The parameterless overload also waits until every redirected line has reached the handlers.
            _process.WaitForExit();
            return _process.ExitCode;
        }

        public async Task StopAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync(TestContext.Current.CancellationToken);
            _process.WaitForExit();
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
