using AzureDevOpsServer.Mcp.AzureDevOps;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
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

builder.Services.AddSingleton<IAzureDevOpsCredentialProvider, ConfiguredCredentialProvider>();

var clientBuilder = builder.Services.AddHttpClient<AzureDevOpsClient>((serviceProvider, httpClient) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<AzureDevOpsServerOptions>>().Value;
        httpClient.BaseAddress = new Uri(options.CollectionUrl.TrimEnd('/') + "/");
    }
);

clientBuilder.AddAzureDevOpsHandlers();

var startupOptions = new AzureDevOpsServerOptions();
startupOptions.LoadFromEnvironment();

var toolCount = ToolRegistration.AddTools(builder.Services, startupOptions);

builder.Services
       .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Build(startupOptions, toolCount))
       .WithStdioServerTransport()
       .WithPromptsFromAssembly();

await builder.Build().RunAsync();
