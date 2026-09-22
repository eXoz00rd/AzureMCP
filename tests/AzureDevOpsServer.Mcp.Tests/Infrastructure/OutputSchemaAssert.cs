using System.Text.Json;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Infrastructure;

// Mirrors what strict MCP clients (the Python SDK used by Open WebUI) enforce on structured tool results.
public static class OutputSchemaAssert
{
    public static void RequiredPropertiesPresent(JsonElement schema, JsonElement value, string path = "$")
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray().Select(element => element.GetString()!))
                {
                    Assert.True(
                        value.TryGetProperty(name, out _),
                        $"{path}.{name} is required by the published output schema but missing from the result."
                    );
                }
            }

            if (schema.TryGetProperty("properties", out var properties))
            {
                foreach (var property in value.EnumerateObject())
                {
                    if (properties.TryGetProperty(property.Name, out var propertySchema))
                    {
                        RequiredPropertiesPresent(propertySchema, property.Value, $"{path}.{property.Name}");
                    }
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var element in value.EnumerateArray())
            {
                RequiredPropertiesPresent(items, element, $"{path}[{index++}]");
            }
        }
    }
}
