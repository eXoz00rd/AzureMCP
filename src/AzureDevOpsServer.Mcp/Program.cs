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
    app.MapAzureDevOpsHttpEndpoints(startupOptions);

    try
    {
        await app.RunAsync();
    }
    catch (IOException exception)
    {
        // Kestrel reports an address it cannot bind this way, and its message already names the address and the reason.
        await Console.Error.WriteLineAsync(exception.Message);
        return 1;
    }
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.AddAzureDevOpsMcpServer(startupOptions).WithStdioServerTransport();
    await builder.Build().RunAsync();
}

return 0;
