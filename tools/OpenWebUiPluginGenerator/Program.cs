using System.Reflection;
using AzureDevOpsServer.Mcp.Configuration;
using OpenWebUiPluginGenerator;

const string usage =
    "Usage: OpenWebUiPluginGenerator --output <file.py> [--toolsets <a,b>] [--read-only] [--download-url <url> --sha256 <hex>]";

string? output = null;
string? toolsets = null;
var readOnly = false;
var downloadUrl = string.Empty;
var sha256 = string.Empty;
string? error = null;

var index = 0;
while (index < args.Length && error is null)
{
    var argument = args[index++];
    switch (argument)
    {
        case "--output":
            output = RequireValue(argument);
            break;
        case "--toolsets":
            toolsets = RequireValue(argument);
            break;
        case "--read-only":
            readOnly = true;
            break;
        case "--download-url":
            downloadUrl = RequireValue(argument) ?? string.Empty;
            break;
        case "--sha256":
            sha256 = (RequireValue(argument) ?? string.Empty).ToLowerInvariant();
            break;
        default:
            error = $"Unknown argument '{argument}'.";
            break;
    }
}

if (error is not null)
{
    return Fail(error);
}

if (string.IsNullOrWhiteSpace(output))
{
    return Fail("--output is required.");
}

if (string.IsNullOrEmpty(downloadUrl) != string.IsNullOrEmpty(sha256))
{
    return Fail("--download-url and --sha256 must be given together.");
}

if (sha256.Length > 0 && (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigitLower)))
{
    return Fail("--sha256 must be 64 hexadecimal characters.");
}

var tools = PluginGenerator.CollectTools(toolsets, readOnly);
var version = typeof(AzureDevOpsServerOptions).Assembly
                  .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                  .InformationalVersion.Split('+')[0] ?? "0.0.0";
var label = (string.IsNullOrWhiteSpace(toolsets) ? "all" : toolsets) + (readOnly ? " (read-only)" : string.Empty);

File.WriteAllText(output, PluginGenerator.Generate(tools, new PluginSettings(version, label, downloadUrl, sha256)));
Console.WriteLine($"Generated {tools.Count} tools into {output}.");
return 0;

string? RequireValue(string name)
{
    if (index < args.Length)
    {
        return args[index++];
    }

    error ??= $"{name} requires a value.";
    return null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(usage);
    return 2;
}
