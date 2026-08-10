using System.Net.Http.Headers;
using System.Text;
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

builder.Services
       .AddHttpClient<AzureDevOpsClient>((serviceProvider, httpClient) =>
           {
               var options = serviceProvider.GetRequiredService<IOptions<AzureDevOpsServerOptions>>().Value;
               httpClient.BaseAddress = new Uri(options.CollectionUrl.TrimEnd('/') + "/");
               var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{options.PersonalAccessToken}"));
               httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
           }
       )
       .AddStandardResilienceHandler();

builder.Services
       .AddMcpServer()
       .WithStdioServerTransport()
       .WithToolsFromAssembly();

await builder.Build().RunAsync();