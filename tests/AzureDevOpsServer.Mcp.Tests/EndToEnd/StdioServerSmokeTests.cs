using System.Collections.Concurrent;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using ModelContextProtocol.Client;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.EndToEnd;

/// <summary>
/// Starts the built server assembly (<c>dotnet AzureDevOpsServer.Mcp.dll</c>) as a real child
/// process talking MCP over stdio, the same transport an external client such as Claude Code or
/// Copilot uses. This does not cover the installed dotnet-tool shim the NuGet package ships;
/// that packaging path is verified separately in CI. In-process tests exercise tools, clients,
/// and configuration, but none of them start the executable or cross the stdio boundary, so a
/// regression in startup, protocol initialization, or stdout/stderr separation could still pass
/// the rest of the suite.
/// </summary>
public sealed class StdioServerSmokeTests
{
    [Fact]
    public async Task Server_OverStdio_InitializesListsToolsAndPromptsAndCallsReadAndWriteTools()
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
        var updateTool = Assert.Single(tools, tool => tool.Name == "update_work_item");
        Assert.True(updateTool.JsonSchema.GetProperty("properties").TryGetProperty("expectedRevision", out _));
        Assert.DoesNotContain(updateTool.JsonSchema.GetProperty("required").EnumerateArray(),
            parameter => parameter.GetString() == "expectedRevision");
        var updateArguments = new Dictionary<string, object?>
        {
            ["id"] = 42,
            ["fields"] = new Dictionary<string, string> { ["System.State"] = "Resolved" },
            ["expectedRevision"] = 3
        };
        var update = await client.CallToolAsync("update_work_item", updateArguments, cancellationToken: cancellationToken);
        Assert.True(update.IsError is null or false);
        Assert.NotNull(update.StructuredContent);
        var updateText = update.StructuredContent.ToString();
        Assert.NotNull(updateText);
        using var updateJson = System.Text.Json.JsonDocument.Parse(updateText);
        Assert.True(updateJson.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(4, updateJson.RootElement.GetProperty("rev").GetInt32());
        Assert.Contains("System.State", update.StructuredContent.ToString());
        Assert.False(updateJson.RootElement.TryGetProperty("fields", out _));

        updateArguments["expectedRevision"] = 2;
        var conflict = await client.CallToolAsync("update_work_item", updateArguments, cancellationToken: cancellationToken);
        Assert.True(conflict.IsError);
        Assert.Contains("current revision is 4", System.Text.Json.JsonSerializer.Serialize(conflict));

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
