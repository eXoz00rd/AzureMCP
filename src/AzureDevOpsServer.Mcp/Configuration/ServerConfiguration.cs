using AzureDevOpsServer.Mcp.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class ServerConfiguration
{
    // Registers everything except the transport, which each host chooses for itself.
    public static IMcpServerBuilder AddAzureDevOpsMcpServer(
        this IHostApplicationBuilder builder,
        AzureDevOpsServerOptions startupOptions)
    {
        var overHttp = ServerTransports.Resolve(startupOptions.Transport) == ServerTransport.Http;

        builder.Logging.ClearProviders();
        if (overHttp)
        {
            builder.Logging.AddConsole();
        }
        else
        {
            // stdout carries the protocol over stdio, so every log record has to go to stderr.
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        }

        builder.Logging.SetMinimumLevel(
            Enum.TryParse<LogLevel>(
                Environment.GetEnvironmentVariable(AzureDevOpsServerOptions.LogLevelVariable),
                true,
                out var minimumLevel
            ) ?
                minimumLevel :
                LogLevel.Warning
        );

        builder.Services.AddSingleton<IValidateOptions<AzureDevOpsServerOptions>, AzureDevOpsServerOptionsValidator>();
        builder.Services
               .AddOptions<AzureDevOpsServerOptions>()
               .Configure(options => options.LoadFromEnvironment())
               .ValidateOnStart();

        if (overHttp)
        {
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IAzureDevOpsCredentialProvider, RequestCredentialProvider>();
            builder.Services.AddHealthChecks();
        }
        else
        {
            builder.Services.AddSingleton<IAzureDevOpsCredentialProvider, ConfiguredCredentialProvider>();
        }

        builder.Services
               .AddHttpClient<AzureDevOpsClient>((serviceProvider, httpClient) =>
                   {
                       var options = serviceProvider.GetRequiredService<IOptions<AzureDevOpsServerOptions>>().Value;
                       httpClient.BaseAddress = new Uri(options.CollectionUrl.TrimEnd('/') + "/");
                   }
               )
               .AddAzureDevOpsHandlers();

        var toolCount = ToolRegistration.AddTools(builder.Services, startupOptions);

        return builder.Services
                      .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Build(startupOptions, toolCount))
                      .WithPromptsFromAssembly(typeof(ServerConfiguration).Assembly);
    }
}
