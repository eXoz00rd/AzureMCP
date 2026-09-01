using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using AzureDevOpsServer.Mcp.Configuration;
using AzureDevOpsServer.Mcp.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Tools;

public sealed class ToolSchemaTests
{
    [Fact]
    public void NullableParameters_AreOptionalInSchema()
    {
        var context = new NullabilityInfoContext();
        var offenders = new List<string>();

        foreach (var toolType in Toolsets.Resolve(null))
        {
            foreach (var method in ToolMethods(toolType))
            {
                var tool = method.GetCustomAttribute<McpServerToolAttribute>()!.Name;
                var required = RequiredNames(method);
                offenders.AddRange(
                    method.GetParameters()
                          .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                          .Where(parameter => IsNullable(context, parameter))
                          .Where(parameter => !parameter.HasDefaultValue || required.Contains(parameter.Name))
                          .Select(parameter => $"{tool}.{parameter.Name}")
                );
            }
        }

        Assert.Empty(offenders);
    }

    private static bool IsNullable(NullabilityInfoContext context, ParameterInfo parameter)
    {
        return Nullable.GetUnderlyingType(parameter.ParameterType) is not null ||
            context.Create(parameter).WriteState == NullabilityState.Nullable;
    }

    [Fact]
    public void LinkWorkItem_RequiresOnlyIdAndLinkType()
    {
        var method = ToolMethods(typeof(WorkItemTools))
            .Single(candidate => candidate.GetCustomAttribute<McpServerToolAttribute>()!.Name == "link_work_item");

        var required = RequiredNames(method).Order().ToArray();

        Assert.Equal(new[] { "id", "linkType" }, required);
    }

    [Fact]
    public void LinkPullRequestToWorkItem_RequiresRepositoryAndIds()
    {
        var method = ToolMethods(typeof(PullRequestTools))
            .Single(candidate =>
                candidate.GetCustomAttribute<McpServerToolAttribute>()!.Name == "link_pull_request_to_work_item"
            );

        var required = RequiredNames(method).Order().ToArray();

        Assert.Equal(new[] { "pullRequestId", "repository", "workItemId" }, required);
    }

    [Fact]
    public void LimitParameters_DocumentTheirValidRange()
    {
        string[] limitNames = ["top", "maxChars", "maxItems", "depth"];
        var offenders = new List<string>();

        foreach (var toolType in Toolsets.Resolve(null))
        {
            foreach (var method in ToolMethods(toolType))
            {
                var tool = method.GetCustomAttribute<McpServerToolAttribute>()!.Name;
                offenders.AddRange(
                    method.GetParameters()
                          .Where(parameter => limitNames.Contains(parameter.Name))
                          .Where(parameter =>
                              parameter.GetCustomAttribute<DescriptionAttribute>()
                                  ?.Description.Contains("alid range", StringComparison.Ordinal) != true
                          )
                          .Select(parameter => $"{tool}.{parameter.Name}")
                );
            }
        }

        Assert.Empty(offenders);
    }

    private static IEnumerable<MethodInfo> ToolMethods(Type toolType)
    {
        return toolType
               .GetMethods(BindingFlags.Public | BindingFlags.Instance)
               .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);
    }

    private static IReadOnlyList<string> RequiredNames(MethodInfo method)
    {
        var schema = McpServerTool.Create(method, _ => null!).ProtocolTool.InputSchema;
        return schema.TryGetProperty("required", out JsonElement required) ?
            [.. required.EnumerateArray().Select(entry => entry.GetString()!)] :
            [];
    }
}
