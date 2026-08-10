using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<IValidateOptions<AzureDevOpsServerOptions>, AzureDevOpsServerOptionsValidator>();
builder.Services
       .AddOptions<AzureDevOpsServerOptions>()
       .Configure(options => options.LoadFromEnvironment())
       .ValidateOnStart();

builder.Services
       .AddMcpServer()
       .WithStdioServerTransport()
       .WithToolsFromAssembly();

await builder.Build().RunAsync();