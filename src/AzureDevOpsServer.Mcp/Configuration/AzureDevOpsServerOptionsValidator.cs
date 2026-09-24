using AzureDevOpsServer.Mcp.AzureDevOps;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.Configuration;

public sealed class AzureDevOpsServerOptionsValidator : IValidateOptions<AzureDevOpsServerOptions>
{
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

    // Kestrel accepts several addresses separated by ';', and each of them has to stay on this machine.
    private static bool ListensOnLoopbackOnly(string urls)
    {
        var addresses = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return addresses.Length > 0 &&
            addresses.All(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsLoopback);
    }
}
