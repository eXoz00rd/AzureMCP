using System.Collections.Concurrent;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using ModelContextProtocol.Client;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.EndToEnd;

/// <summary>
/// Starts the packaged server as a real child process talking MCP over stdio, the same way an
/// external client such as Claude Code or Copilot would. In-process tests exercise tools,
/// clients, and configuration, but none of them start the executable or cross the stdio boundary,
/// so a regression in startup, protocol initialization, or stdout/stderr separation could still
/// pass the rest of the suite.
/// </summary>
public sealed class StdioServerSmokeTests
{
    [Fact]
    public async Task Server_OverStdio_InitializesListsToolsAndPromptsAndCallsAReadOnlyTool()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellationToken = cancellation.Token;

        await using var azureDevOps = new StubAzureDevOpsServer();
        var stderrLines = new ConcurrentQueue<string>();

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments = [typeof(AzureDevOpsServerOptions).Assembly.Location],
                Name = "azure-devops-server-mcp-smoke-test",
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    [AzureDevOpsServerOptions.CollectionUrlVariable] = azureDevOps.CollectionUrl,
                    [AzureDevOpsServerOptions.PersonalAccessTokenVariable] = "smoke-test-pat",
                    [AzureDevOpsServerOptions.LogLevelVariable] = "Information",
                },
                StandardErrorLines = line => stderrLines.Enqueue(line),
            }
        );

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        Assert.Contains(tools, tool => tool.Name == "list_projects");

        var prompts = await client.ListPromptsAsync(cancellationToken: cancellationToken);
        Assert.Contains(prompts, prompt => prompt.Name == "review_pull_request");
        Assert.Contains(prompts, prompt => prompt.Name == "diagnose_build_failure");
        Assert.Contains(prompts, prompt => prompt.Name == "sprint_status");

        var result = await client.CallToolAsync("list_projects", cancellationToken: cancellationToken);

        // CallToolResult.IsError is nullable: a successful call leaves it null rather than false,
        // so the assertion has to accept both instead of requiring an exact `false`.
        Assert.True(result.IsError is null or false, $"Expected a successful tool call, but IsError was {result.IsError}.");
        Assert.NotNull(result.StructuredContent);
        Assert.Contains("Alpha", result.StructuredContent.ToString());
        Assert.Contains(azureDevOps.RequestPaths, path => path.Contains("_apis/projects", StringComparison.Ordinal));

        // ADOS_LOG_LEVEL=Information guarantees the host lifetime messages are emitted, so a
        // non-empty capture here proves diagnostics actually reach stderr rather than merely
        // being absent from stdout because nothing was logged.
        Assert.NotEmpty(stderrLines);
    }
}
