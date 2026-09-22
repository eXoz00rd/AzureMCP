using System.Diagnostics.CodeAnalysis;

namespace OpenWebUiPluginGenerator;

internal sealed record GeneratorArguments(string Output, string? Toolsets, bool ReadOnly, string DownloadUrl, string Sha256)
{
    public const string Usage =
        "Usage: OpenWebUiPluginGenerator --output <file.py> [--toolsets <a,b>] [--read-only] [--download-url <url> --sha256 <hex>]";

    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out GeneratorArguments? arguments,
        [NotNullWhen(false)] out string? error)
    {
        arguments = null;
        string? output = null;
        string? toolsets = null;
        var readOnly = false;
        var downloadUrl = string.Empty;
        var sha256 = string.Empty;

        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (name == "--read-only")
            {
                readOnly = true;
                continue;
            }

            if (name is not ("--output" or "--toolsets" or "--download-url" or "--sha256"))
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
                case "--download-url":
                    downloadUrl = value;
                    break;
                default:
                    sha256 = value.ToLowerInvariant();
                    break;
            }
        }

        error = Validate(output, downloadUrl, sha256);
        if (error is not null)
        {
            return false;
        }

        arguments = new GeneratorArguments(output!, toolsets, readOnly, downloadUrl, sha256);
        return true;
    }

    private static string? Validate(string? output, string downloadUrl, string sha256)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "--output is required.";
        }

        if (string.IsNullOrEmpty(downloadUrl) != string.IsNullOrEmpty(sha256))
        {
            return "--download-url and --sha256 must be given together.";
        }

        if (downloadUrl.Length > 0 &&
            !(Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
        {
            return "--download-url must be an absolute http or https URL.";
        }

        if (sha256.Length > 0 && (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigitLower)))
        {
            return "--sha256 must be 64 hexadecimal characters.";
        }

        return null;
    }
}
