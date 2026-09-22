using System.Text.Json;
using ModelContextProtocol.Protocol;
using OpenWebUiPluginGenerator;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.OpenWebUi;

public sealed class PluginGeneratorTests
{
    private static readonly PluginSettings Settings = new("1.2.3", "all", string.Empty, string.Empty);

    [Fact]
    public void Generate_EmitsOneMethodPerRegisteredTool()
    {
        var tools = PluginGenerator.CollectTools(null, readOnly: false);

        var plugin = PluginGenerator.Generate(tools, Settings);

        Assert.All(tools, tool => Assert.Contains($"    async def {tool.Name}(\n", plugin));
        Assert.DoesNotContain("%%", plugin);
    }

    [Fact]
    public void Generate_MapsSchemaTypesToPythonTypeHints()
    {
        var plugin = PluginGenerator.Generate(PluginGenerator.CollectTools(null, readOnly: false), Settings);

        var getWorkItems = MethodSource(plugin, "get_work_items");
        Assert.Contains("        ids: list[int],\n", getWorkItems);
        Assert.Contains("        fields: Optional[list[str]] = None,\n", getWorkItems);
        Assert.Contains("        includeRelations: bool = False,\n", getWorkItems);
        Assert.Contains("        descriptionFormat: Optional[str] = None,\n", getWorkItems);

        var updateWorkItem = MethodSource(plugin, "update_work_item");
        Assert.Contains("        id: int,\n", updateWorkItem);
        Assert.Contains("        fields: dict[str, str],\n", updateWorkItem);
        Assert.Contains("        expectedRevision: Optional[int] = None,\n", updateWorkItem);
        Assert.Contains("        :param expectedRevision: Revision from the last read.", updateWorkItem);
    }

    [Fact]
    public void Generate_PlacesRequiredParametersBeforeOptionalOnes()
    {
        var tools = PluginGenerator.CollectTools(null, readOnly: false);
        var plugin = PluginGenerator.Generate(tools, Settings);

        foreach (var tool in tools)
        {
            var signature = MethodSource(plugin, tool.Name).Split(") -> str:")[0];
            var seenOptional = false;
            foreach (var line in signature.Split('\n').Where(line => line.EndsWith(',') && !line.Contains("__user__")))
            {
                var optional = line.Contains(" = ", StringComparison.Ordinal);
                Assert.False(seenOptional && !optional, $"{tool.Name}: required parameter after an optional one: {line.Trim()}");
                seenOptional |= optional;
            }
        }
    }

    [Fact]
    public void CollectTools_WithToolsetAndReadOnly_ReturnsOnlyThoseTools()
    {
        var names = PluginGenerator.CollectTools("workitems", readOnly: true).Select(tool => tool.Name).ToList();

        Assert.Contains("get_work_item", names);
        Assert.Contains("server_info", names);
        Assert.DoesNotContain("update_work_item", names);
        Assert.DoesNotContain("list_projects", names);
    }

    [Fact]
    public void Generate_EscapesDocstringDelimitersAndBackslashes()
    {
        var tool = new Tool
        {
            Name = "probe",
            Description = "Ends a \"\"\" string and a C:\\path",
            InputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""")
        };

        var plugin = PluginGenerator.Generate([tool], Settings);

        Assert.Contains("        Ends a \\\"\\\"\\\" string and a C:\\\\path\n", plugin);
    }

    [Fact]
    public void Generate_EscapesFrontmatterValuesAndKeepsThemOnOneLine()
    {
        var plugin = PluginGenerator.Generate([], new PluginSettings("1.2.3", "a\"\"\"b\nc", string.Empty, string.Empty));

        Assert.Contains("Toolsets: a\\\"\\\"\\\"b c.\n", plugin);
        Assert.DoesNotContain("a\"\"\"b", plugin);
    }

    [Fact]
    public void Generate_WithUnsupportedSchemaType_Throws()
    {
        var tool = new Tool
        {
            Name = "probe",
            InputSchema = JsonSerializer.Deserialize<JsonElement>(
                """{"type":"object","properties":{"value":{"type":["string","integer"]}}}"""
            )
        };

        Assert.Throws<NotSupportedException>(() => PluginGenerator.Generate([tool], Settings));
    }

    [Fact]
    public void Generate_WithDownloadSettings_FillsValveDefaults()
    {
        var sha256 = new string('a', 64);

        var plugin = PluginGenerator.Generate(
            [],
            new PluginSettings("1.2.3", "all", "https://artifacts.example/AzureDevOpsServer.Mcp", sha256)
        );

        Assert.Contains("default=\"https://artifacts.example/AzureDevOpsServer.Mcp\",", plugin);
        Assert.Contains($"default=\"{sha256}\",", plugin);
        Assert.Contains("version: 1.2.3\n", plugin);
    }

    private static string MethodSource(string plugin, string toolName)
    {
        var start = plugin.IndexOf($"    async def {toolName}(\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No method was generated for {toolName}.");
        var end = plugin.IndexOf("return await self._call_tool(", start, StringComparison.Ordinal);
        return plugin[start..end];
    }
}
