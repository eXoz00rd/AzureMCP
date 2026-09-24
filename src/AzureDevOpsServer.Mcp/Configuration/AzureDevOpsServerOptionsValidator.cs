using System.Globalization;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.Configuration;

public sealed class AzureDevOpsServerOptionsValidator : IValidateOptions<AzureDevOpsServerOptions>
{
    // The token is the only thing between the network and every caller's PAT, so it has to be a generated secret.
    public const int MinimumHttpTokenLength = 32;

    public ValidateOptionsResult Validate(string? name, AzureDevOpsServerOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CollectionUrl))
        {
            failures.Add($"{AzureDevOpsServerOptions.CollectionUrlVariable} is required.");
        }
        else if (!Uri.TryCreate(options.CollectionUrl, UriKind.Absolute, out var collectionUri) ||
                 (collectionUri.Scheme != Uri.UriSchemeHttps && collectionUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{AzureDevOpsServerOptions.CollectionUrlVariable} must be an absolute http(s) URL.");
        }

        var overHttp = ServerTransports.TryResolve(options.Transport, out var transport) && transport == ServerTransport.Http;

        if (!overHttp && string.IsNullOrWhiteSpace(options.PersonalAccessToken))
        {
            failures.Add($"{AzureDevOpsServerOptions.PersonalAccessTokenVariable} is required.");
        }

        if (overHttp && !string.IsNullOrWhiteSpace(options.PersonalAccessToken))
        {
            failures.Add(
                $"{AzureDevOpsServerOptions.PersonalAccessTokenVariable} is not used when {AzureDevOpsServerOptions.TransportVariable} is http: " +
                $"every request carries its caller's own PAT in the {RequestCredentialProvider.HeaderName} header. " +
                $"Remove {AzureDevOpsServerOptions.PersonalAccessTokenVariable} so no request can fall back to a shared identity."
            );
        }

        if (string.IsNullOrWhiteSpace(options.ApiVersion))
        {
            failures.Add($"{AzureDevOpsServerOptions.ApiVersionVariable} must not be empty when set.");
        }

        if (overHttp && string.IsNullOrWhiteSpace(options.HttpToken) && !options.HttpAllowAnonymous)
        {
            failures.Add(
                $"{AzureDevOpsServerOptions.HttpTokenVariable} is required when {AzureDevOpsServerOptions.TransportVariable} is http. " +
                $"Set {AzureDevOpsServerOptions.HttpAllowAnonymousVariable}=true only for a loopback development endpoint."
            );
        }

        // Measured after trimming, because the access guard compares the trimmed value.
        if (overHttp && !string.IsNullOrWhiteSpace(options.HttpToken) && options.HttpToken.Trim().Length < MinimumHttpTokenLength)
        {
            failures.Add(
                $"{AzureDevOpsServerOptions.HttpTokenVariable} must be at least {MinimumHttpTokenLength} characters long, because anyone who guesses it can call the server. " +
                "Generate one, for example with: openssl rand -hex 32"
            );
        }

        if (overHttp && UnusableAddress(options.HttpUrl) is { } address)
        {
            failures.Add(
                $"{AzureDevOpsServerOptions.HttpUrlVariable} contains '{address}', which is not a plain http:// address with a host and a numeric port. " +
                "Use addresses such as http://0.0.0.0:8080 or http://+:8080, separated by ';'; the server serves HTTP and leaves TLS to whatever runs in front of it."
            );
        }

        if (overHttp &&
            string.IsNullOrWhiteSpace(options.HttpToken) &&
            options.HttpAllowAnonymous &&
            !ListensOnLoopbackOnly(options.HttpUrl))
        {
            failures.Add(
                $"{AzureDevOpsServerOptions.HttpAllowAnonymousVariable} is allowed only while {AzureDevOpsServerOptions.HttpUrlVariable} listens on loopback, " +
                $"such as {AzureDevOpsServerOptions.DefaultHttpUrl}. Set {AzureDevOpsServerOptions.HttpTokenVariable} to serve any other interface."
            );
        }

        return failures.Count > 0 ?
            ValidateOptionsResult.Fail(failures) :
            ValidateOptionsResult.Success;
    }

    // Kestrel binds wildcard hosts such as + and * that Uri rejects, and binds port 80 when it cannot read a port,
    // so each address is checked by the parts Kestrel uses rather than parsed as a Uri.
    private static string? UnusableAddress(string urls)
    {
        var addresses = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (addresses.Length == 0)
        {
            // Kestrel would fall back to its own default address instead of the one this server documents.
            return urls;
        }

        foreach (var address in addresses)
        {
            if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return address;
            }

            var authority = address["http://".Length..].TrimEnd('/');
            var portSeparator = authority.LastIndexOf(':');
            var hasPort = portSeparator > authority.LastIndexOf(']');
            var host = hasPort ? authority[..portSeparator] : authority;
            var port = hasPort ? authority[(portSeparator + 1)..] : "80";

            if (host.Length == 0 ||
                authority.Contains('/', StringComparison.Ordinal) ||
                !int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                number > 65535)
            {
                return address;
            }
        }

        return null;
    }

    // Kestrel accepts several addresses separated by ';', and each of them has to stay on this machine.
    private static bool ListensOnLoopbackOnly(string urls)
    {
        var addresses = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return addresses.Length > 0 &&
            addresses.All(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsLoopback);
    }
}
