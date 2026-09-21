using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed class ConfiguredCredentialProvider : IAzureDevOpsCredentialProvider
{
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public ConfiguredCredentialProvider(IOptions<AzureDevOpsServerOptions> options)
    {
        _options = options;
    }

    public ValueTask<string> GetPersonalAccessTokenAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(_options.Value.PersonalAccessToken);
    }
}
