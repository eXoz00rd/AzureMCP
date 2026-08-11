using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class ToolRegistration
{
    public static int AddTools(IServiceCollection services, AzureDevOpsServerOptions options)
    {
        var registered = 0;

        foreach (var toolType in Toolsets.Resolve(options.Toolsets))
        {
            foreach (var method in SelectMethods(toolType, options.ReadOnly))
            {
                var capturedType = toolType;
                var capturedMethod = method;
                services.AddSingleton<McpServerTool>(serviceProvider => McpServerTool.Create(
                        capturedMethod,
                        _ => ActivatorUtilities.CreateInstance(serviceProvider, capturedType),
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
