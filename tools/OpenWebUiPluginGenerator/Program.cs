using AzureDevOpsServer.Mcp.OpenWebUi;
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

var settings = PluginSettings.For(arguments.Toolsets, arguments.ReadOnly, arguments.ServerUrl);

File.WriteAllText(arguments.Output, PluginGenerator.Generate(tools, settings));
Console.WriteLine($"Generated {tools.Count} tools into {arguments.Output}.");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(GeneratorArguments.Usage);
    return 2;
}
