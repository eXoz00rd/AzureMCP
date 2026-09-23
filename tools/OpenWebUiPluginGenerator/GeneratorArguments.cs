using System.Diagnostics.CodeAnalysis;

namespace OpenWebUiPluginGenerator;

internal sealed record GeneratorArguments(string Output, string? Toolsets, bool ReadOnly, string ServerUrl)
{
    public const string Usage =
        "Usage: OpenWebUiPluginGenerator --output <file.py> [--toolsets <a,b>] [--read-only] [--server-url <url>]";

    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out GeneratorArguments? arguments,
        [NotNullWhen(false)] out string? error)
    {
        arguments = null;
        string? output = null;
        string? toolsets = null;
        var readOnly = false;
        var serverUrl = string.Empty;

        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (name == "--read-only")
            {
                readOnly = true;
                continue;
            }

            if (name is not ("--output" or "--toolsets" or "--server-url"))
            {
                error = $"Unknown argument '{name}'.";
                return false;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{name} requires a value.";
                return false;
            }

            var value = args[++index];
            switch (name)
            {
                case "--output":
                    output = value;
                    break;
                case "--toolsets":
                    toolsets = value;
                    break;
                default:
                    serverUrl = value;
                    break;
            }
        }

        error = Validate(output, serverUrl);
        if (error is not null)
        {
            return false;
        }

        arguments = new GeneratorArguments(output!, toolsets, readOnly, serverUrl);
        return true;
    }

    private static string? Validate(string? output, string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "--output is required.";
        }

        if (serverUrl.Length > 0 &&
            !(Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
        {
            return "--server-url must be an absolute http or https URL.";
        }

        return null;
    }
}
