using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var startupOptions = new AzureDevOpsServerOptions();
startupOptions.LoadFromEnvironment();

if (ServerTransports.Resolve(startupOptions.Transport) == ServerTransport.Http)
{
    var webBuilder = WebApplication.CreateSlimBuilder(args);
    webBuilder.WebHost.UseUrls(startupOptions.HttpUrl);
    webBuilder.AddAzureDevOpsMcpServer(startupOptions).WithHttpTransport(options => options.Stateless = true);

    var app = webBuilder.Build();
    app.MapAzureDevOpsMcp(startupOptions);
    await app.RunAsync();
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.AddAzureDevOpsMcpServer(startupOptions).WithStdioServerTransport();
    await builder.Build().RunAsync();
}
