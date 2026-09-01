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

        if (string.IsNullOrWhiteSpace(options.PersonalAccessToken))
        {
            failures.Add($"{AzureDevOpsServerOptions.PersonalAccessTokenVariable} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiVersion))
        {
            failures.Add($"{AzureDevOpsServerOptions.ApiVersionVariable} must not be empty when set.");
        }

        return failures.Count > 0 ?
            ValidateOptionsResult.Fail(failures) :
            ValidateOptionsResult.Success;
    }
}
