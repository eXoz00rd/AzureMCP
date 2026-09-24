using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenWebUiPluginGenerator;

internal sealed record PluginSettings(string Version, string Toolsets, string ServerUrl);

internal static partial class PluginGenerator
{
    private static readonly HashSet<string> PythonKeywords = new(StringComparer.Ordinal)
    {
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def",
        "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is",
        "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield"
    };

    public static IReadOnlyList<Tool> CollectTools(string? toolsets, bool readOnly)
    {
        var services = new ServiceCollection();
        ToolRegistration.AddTools(services, new AzureDevOpsServerOptions { Toolsets = toolsets, ReadOnly = readOnly });
        using var provider = services.BuildServiceProvider();
        return
        [
            .. provider.GetServices<McpServerTool>()
                       .Select(tool => tool.ProtocolTool)
                       .OrderBy(tool => tool.Name, StringComparer.Ordinal)
        ];
    }

    public static string Generate(IReadOnlyList<Tool> tools, PluginSettings settings)
    {
        return ReadTemplate()
               .Replace("%%VERSION%%", FrontmatterValue(settings.Version))
               .Replace("%%TOOLSETS%%", FrontmatterValue(settings.Toolsets))
               .Replace("%%SERVER_URL%%", PythonString(settings.ServerUrl))
               .Replace("%%TOOL_METHODS%%", string.Join("\n", tools.Select(GenerateMethod)).TrimEnd('\n'));
    }

    private static string GenerateMethod(Tool tool)
    {
        RequireIdentifier(tool.Name, "tool name");

        var schema = tool.InputSchema;
        var required = schema.TryGetProperty("required", out var requiredNames) ?
            requiredNames.EnumerateArray().Select(name => name.GetString()!).ToHashSet(StringComparer.Ordinal) :
            [];
        var parameters = new List<Parameter>();
        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                RequireIdentifier(property.Name, $"parameter of {tool.Name}");
                if (property.Name == "self")
                {
                    throw new NotSupportedException(
                        $"The parameter 'self' of {tool.Name} collides with the generated method's own 'self'."
                    );
                }

                parameters.Add(ToParameter(property.Name, property.Value, required.Contains(property.Name)));
            }
        }

        var method = new StringBuilder();
        method.Append($"    async def {tool.Name}(\n        self,\n");
        foreach (var parameter in parameters.Where(p => p.Default is null).Concat(parameters.Where(p => p.Default is not null)))
        {
            var defaultValue = parameter.Default is null ? string.Empty : $" = {parameter.Default}";
            method.Append($"        {parameter.Name}: {parameter.TypeHint}{defaultValue},\n");
        }

        method.Append("        __user__: dict = {},\n    ) -> str:\n        \"\"\"\n");
        AppendDocstringLines(method, tool.Description ?? tool.Name);
        foreach (var parameter in parameters.Where(p => !string.IsNullOrWhiteSpace(p.Description)))
        {
            AppendDocstringLines(method, $":param {parameter.Name}: {parameter.Description}");
        }

        var arguments = string.Join(", ", parameters.Select(p => $"\"{p.Name}\": {p.Name}"));
        method.Append("        \"\"\"\n");
        method.Append($"        return await self._call_tool(\"{tool.Name}\", {{{arguments}}}, __user__)\n");
        return method.ToString();
    }

    private static Parameter ToParameter(string name, JsonElement schema, bool isRequired)
    {
        var (typeHint, nullable) = MapType(schema);
        var defaultValue = schema.TryGetProperty("default", out var value) ?
            PythonLiteral(value) :
            isRequired ? null : "None";
        if (nullable || defaultValue == "None")
        {
            typeHint = $"Optional[{typeHint}]";
        }

        var description = schema.TryGetProperty("description", out var text) ? text.GetString() : null;
        return new Parameter(name, typeHint, defaultValue, description);
    }

    private static (string TypeHint, bool Nullable) MapType(JsonElement schema)
    {
        var (type, nullable) = ReadType(schema);
        var typeHint = type switch
        {
            "string" => "str",
            "integer" => "int",
            "number" => "float",
            "boolean" => "bool",
            "array" when schema.TryGetProperty("items", out var items) => $"list[{NestedTypeHint(items)}]",
            "object" when schema.TryGetProperty("additionalProperties", out var values) &&
                values.ValueKind == JsonValueKind.Object => $"dict[str, {NestedTypeHint(values)}]",
            _ => throw new NotSupportedException($"Schema type '{type}' cannot be expressed as an Open WebUI tool parameter.")
        };

        return (typeHint, nullable);
    }

    private static string NestedTypeHint(JsonElement schema)
    {
        var (typeHint, nullable) = MapType(schema);
        return nullable ? $"Optional[{typeHint}]" : typeHint;
    }

    private static (string Type, bool Nullable) ReadType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            throw new NotSupportedException("A parameter schema without a type cannot be expressed as an Open WebUI tool parameter.");
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            return (type.GetString()!, false);
        }

        var names = type.EnumerateArray().Select(name => name.GetString()!).ToList();
        var concrete = names.Where(name => name != "null").ToList();
        if (concrete.Count != 1)
        {
            throw new NotSupportedException($"Schema type union [{string.Join(", ", names)}] cannot be expressed as an Open WebUI tool parameter.");
        }

        return (concrete[0], names.Contains("null"));
    }

    private static string PythonLiteral(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => "None",
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => PythonString(value.GetString()!),
            _ => throw new NotSupportedException($"Default value '{value.GetRawText()}' cannot be expressed in Python.")
        };
    }

    private static string PythonString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }

    // Open WebUI reads frontmatter line by line from the module docstring, so a value must stay on one line.
    private static string FrontmatterValue(string value)
    {
        return EscapeDocstring(value.ReplaceLineEndings(" "));
    }

    private static string EscapeDocstring(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"\"\"", "\\\"\\\"\\\"");
    }

    private static void AppendDocstringLines(StringBuilder method, string text)
    {
        foreach (var line in EscapeDocstring(text).ReplaceLineEndings("\n").Split('\n'))
        {
            method.Append($"        {line}".TrimEnd()).Append('\n');
        }
    }

    private static void RequireIdentifier(string name, string role)
    {
        if (!IdentifierPattern().IsMatch(name) || PythonKeywords.Contains(name) || name.StartsWith("__", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"The {role} '{name}' is not usable as a Python identifier.");
        }
    }

    private static string ReadTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PluginTemplate.py") ??
            throw new InvalidOperationException("The plugin template is not embedded in the generator.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    private sealed record Parameter(string Name, string TypeHint, string? Default, string? Description);
}
