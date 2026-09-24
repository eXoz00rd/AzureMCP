using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var startupOptions = new AzureDevOpsServerOptions();
startupOptions.LoadFromEnvironment();

IHost host;
try
{
    host = BuildHost(startupOptions, args);
}
catch (InvalidOperationException exception)
{
    // Settings that cannot even be wired up: an unknown transport or toolset, or an unusable endpoint path.
    return await RefuseToStartAsync(exception.Message);
}

try
{
    await host.RunAsync();
}
catch (IOException exception) when (host is WebApplication)
{
    // Kestrel reports an address it cannot bind this way, and its message already names the address and the reason.
    return await RefuseToStartAsync(exception.Message);
}
catch (OptionsValidationException exception)
{
    return await RefuseToStartAsync(string.Join(Environment.NewLine, exception.Failures));
}

return 0;

static IHost BuildHost(AzureDevOpsServerOptions startupOptions, string[] args)
{
    if (ServerTransports.Resolve(startupOptions.Transport) == ServerTransport.Http)
    {
        var webBuilder = WebApplication.CreateSlimBuilder(args);
        webBuilder.WebHost.UseUrls(startupOptions.HttpUrl);
        webBuilder.AddAzureDevOpsMcpServer(startupOptions).WithHttpTransport(options => options.Stateless = true);

        var app = webBuilder.Build();
        app.MapAzureDevOpsHttpEndpoints(startupOptions);
        return app;
    }

    var builder = Host.CreateApplicationBuilder(args);
    builder.AddAzureDevOpsMcpServer(startupOptions).WithStdioServerTransport();
    return builder.Build();
}

// A setting that makes the server unsafe or unusable ends it with the reason and a plain exit code, not a crash.
static async Task<int> RefuseToStartAsync(string reason)
{
    await Console.Error.WriteLineAsync(reason);
    return 1;
}
