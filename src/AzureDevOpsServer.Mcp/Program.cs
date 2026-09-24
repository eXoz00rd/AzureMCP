using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var startupOptions = new AzureDevOpsServerOptions();
startupOptions.LoadFromEnvironment();

ServerTransport transport;
try
{
    transport = ServerTransports.Resolve(startupOptions.Transport);
}
catch (InvalidOperationException exception)
{
    return await RefuseToStartAsync(exception.Message);
}

if (transport == ServerTransport.Http)
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
    catch (OptionsValidationException exception)
    {
        return await RefuseToStartAsync(string.Join(Environment.NewLine, exception.Failures));
    }
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.AddAzureDevOpsMcpServer(startupOptions).WithStdioServerTransport();
    try
    {
        await builder.Build().RunAsync();
    }
    catch (OptionsValidationException exception)
    {
        return await RefuseToStartAsync(string.Join(Environment.NewLine, exception.Failures));
    }
}

return 0;

// A setting that makes the server unsafe or unusable ends it with the reason and a plain exit code, not a crash.
static async Task<int> RefuseToStartAsync(string reason)
{
    await Console.Error.WriteLineAsync(reason);
    return 1;
}
