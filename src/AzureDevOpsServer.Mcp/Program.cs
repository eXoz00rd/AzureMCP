using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var startupOptions = new AzureDevOpsServerOptions();
startupOptions.LoadFromEnvironment();

var builder = Host.CreateApplicationBuilder(args);
builder.AddAzureDevOpsMcpServer(startupOptions).WithStdioServerTransport();

await builder.Build().RunAsync();
