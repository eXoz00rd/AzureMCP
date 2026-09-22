using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class ToolRegistration
{
    public static int AddTools(IServiceCollection services, AzureDevOpsServerOptions options)
    {
        return AddTools(services, Toolsets.Resolve(options.Toolsets), options.ReadOnly);
    }

    internal static int AddTools(IServiceCollection services, IEnumerable<Type> toolTypes, bool readOnly)
    {
        var registered = 0;

        foreach (var toolType in toolTypes)
        {
            foreach (var method in SelectMethods(toolType, readOnly))
            {
                var capturedType = toolType;
                var capturedMethod = method;
                services.AddSingleton<McpServerTool>(serviceProvider => McpServerTool.Create(
                        capturedMethod,
                        // Build from the request scope; the captured root provider would share scoped state across callers.
                        request => ActivatorUtilities.CreateInstance(
                            request.Services ?? throw new InvalidOperationException("The MCP request has no service scope."),
                            capturedType
                        ),
                        new McpServerToolCreateOptions { Services = serviceProvider }
                    )
                );
                registered++;
            }
        }

        return registered;
    }

    private static IEnumerable<MethodInfo> SelectMethods(Type toolType, bool readOnly)
    {
        return toolType
               .GetMethods(BindingFlags.Public | BindingFlags.Instance)
               .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is { } attribute &&
                   (!readOnly || attribute.ReadOnly == true));
    }
}
