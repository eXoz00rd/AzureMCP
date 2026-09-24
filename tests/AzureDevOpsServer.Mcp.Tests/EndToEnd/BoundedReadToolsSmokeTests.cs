using System.Text.Json;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.EndToEnd;

/// <summary>
/// Calls the wiki read tools that bound their responses through a real stdio process, so their
/// registration, their new parameters, and their truncation reporting are proven
/// across the MCP boundary and not only through direct C# calls. The stub models large answers; it
/// does not prove how a real Azure DevOps Server pages or filters them.
/// </summary>
public sealed class BoundedReadToolsSmokeTests
{
    [Fact]
    public async Task WikiTools_OverStdio_BoundTheirResponsesAndReportTruncation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellationToken = cancellation.Token;
        await using var azureDevOps = new StubAzureDevOpsServer();
        await using var client = await StartClientAsync(azureDevOps, cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        AssertOptionalParameters(tools, "list_wiki_pages", "top");
        AssertOptionalParameters(tools, "get_wiki_page", "startLine", "endLine", "maxChars");

        var limited = await CallAsync(
            client,
            tools,
            "list_wiki_pages",
            new Dictionary<string, object?> { ["wiki"] = "Alpha.wiki", ["project"] = "Alpha", ["top"] = 5 },
            cancellationToken
        );
        Assert.True(limited.GetProperty("truncated").GetBoolean());
        Assert.Equal(12, limited.GetProperty("totalPages").GetInt32());
        var sections = limited.GetProperty("subPages").EnumerateArray().ToArray();
        Assert.Equal(3, sections.Length);
        Assert.Equal(2, sections[0].GetProperty("subPages").GetArrayLength());
        Assert.Equal(0, sections[1].GetProperty("subPages").GetArrayLength());

        var whole = await CallAsync(
            client,
            tools,
            "list_wiki_pages",
            new Dictionary<string, object?> { ["wiki"] = "Alpha.wiki", ["project"] = "Alpha", ["top"] = 12 },
            cancellationToken
        );
        Assert.False(whole.GetProperty("truncated").GetBoolean());
        Assert.All(whole.GetProperty("subPages").EnumerateArray(), section => Assert.Equal(3, section.GetProperty("subPages").GetArrayLength()));

        var cut = await CallAsync(
            client,
            tools,
            "get_wiki_page",
            new Dictionary<string, object?>
            {
                ["wiki"] = "Alpha.wiki",
                ["path"] = "/Big",
                ["project"] = "Alpha",
                ["maxChars"] = 20
            },
            cancellationToken
        );
        Assert.True(cut.GetProperty("truncated").GetBoolean());
        Assert.Equal(20, cut.GetProperty("content").GetString()!.Length);
        Assert.Equal(96, cut.GetProperty("totalChars").GetInt32());
        Assert.Equal(12, cut.GetProperty("totalLines").GetInt32());

        var range = await CallAsync(
            client,
            tools,
            "get_wiki_page",
            new Dictionary<string, object?>
            {
                ["wiki"] = "Alpha.wiki",
                ["path"] = "/Big",
                ["project"] = "Alpha",
                ["startLine"] = 3,
                ["endLine"] = 4
            },
            cancellationToken
        );
        Assert.False(range.GetProperty("truncated").GetBoolean());
        Assert.Equal("Line 03\nLine 04\n", range.GetProperty("content").GetString());
        Assert.Equal(12, range.GetProperty("totalLines").GetInt32());

        var invalid = await client.CallToolAsync(
            "get_wiki_page",
            new Dictionary<string, object?>
            {
                ["wiki"] = "Alpha.wiki",
                ["path"] = "/Big",
                ["project"] = "Alpha",
                ["startLine"] = 5,
                ["endLine"] = 4
            },
            cancellationToken: cancellationToken
        );
        Assert.True(invalid.IsError);
        Assert.Contains("must not be less than", JsonSerializer.Serialize(invalid));
    }

    private static async Task<McpClient> StartClientAsync(StubAzureDevOpsServer azureDevOps, CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments = [typeof(AzureDevOpsServerOptions).Assembly.Location],
                Name = "azure-devops-server-mcp-bounded-read-smoke-test",
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    [AzureDevOpsServerOptions.CollectionUrlVariable] = azureDevOps.CollectionUrl,
                    [AzureDevOpsServerOptions.PersonalAccessTokenVariable] = "smoke-test-pat"
                }
            }
        );
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static void AssertOptionalParameters(IList<McpClientTool> tools, string toolName, params string[] parameters)
    {
        var schema = Assert.Single(tools, tool => tool.Name == toolName).JsonSchema;
        var required = schema.TryGetProperty("required", out var names) ?
            names.EnumerateArray().Select(name => name.GetString()).ToArray() :
            [];
        foreach (var parameter in parameters)
        {
            Assert.True(schema.GetProperty("properties").TryGetProperty(parameter, out _), $"{toolName} has no '{parameter}' parameter.");
            Assert.DoesNotContain(parameter, required);
        }
    }

    // Returns the structured result after checking it against the output schema the server published.
    private static async Task<JsonElement> CallAsync(
        McpClient client,
        IList<McpClientTool> tools,
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

        Assert.True(result.IsError is null or false, $"Expected {toolName} to succeed: {JsonSerializer.Serialize(result)}");
        Assert.NotNull(result.StructuredContent);
        var structured = JsonSerializer.SerializeToElement(result.StructuredContent);
        OutputSchemaAssert.RequiredPropertiesPresent(
            JsonSerializer.SerializeToElement(Assert.Single(tools, tool => tool.Name == toolName).ProtocolTool.OutputSchema),
            structured
        );
        return structured;
    }
}
