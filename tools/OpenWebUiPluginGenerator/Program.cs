using System.Reflection;
using AzureDevOpsServer.Mcp.Configuration;
using OpenWebUiPluginGenerator;

if (!GeneratorArguments.TryParse(args, out var arguments, out var error))
{
    return Fail(error);
}

IReadOnlyList<ModelContextProtocol.Protocol.Tool> tools;
try
{
    tools = PluginGenerator.CollectTools(arguments.Toolsets, arguments.ReadOnly);
}
catch (InvalidOperationException exception)
{
    return Fail(exception.Message);
}

var version = typeof(AzureDevOpsServerOptions).Assembly
                  .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                  .InformationalVersion.Split('+')[0] ?? "0.0.0";
var label = (string.IsNullOrWhiteSpace(arguments.Toolsets) ? "all" : arguments.Toolsets) +
    (arguments.ReadOnly ? " (read-only)" : string.Empty);
var settings = new PluginSettings(version, label, arguments.ServerUrl);

File.WriteAllText(arguments.Output, PluginGenerator.Generate(tools, settings));
Console.WriteLine($"Generated {tools.Count} tools into {arguments.Output}.");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(GeneratorArguments.Usage);
    return 2;
}
