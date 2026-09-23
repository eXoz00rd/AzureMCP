using Microsoft.AspNetCore.Http;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

// Over HTTP each caller brings their own PAT, so Azure DevOps sees who actually asked instead of a shared account.
public sealed class RequestCredentialProvider : IAzureDevOpsCredentialProvider
{
    public const string HeaderName = "X-Azure-DevOps-Pat";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestCredentialProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ValueTask<string> GetPersonalAccessTokenAsync(CancellationToken cancellationToken)
    {
        var values = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName] ?? default;

        if (values.Count > 1)
        {
            throw new AzureDevOpsClientException($"The request sends more than one {HeaderName} header; send exactly one.");
        }

        var personalAccessToken = values.ToString().Trim();
        if (personalAccessToken.Length == 0)
        {
            throw new AzureDevOpsClientException(
                $"This request carries no Azure DevOps PAT. Over HTTP every request must send its caller's own PAT in the {HeaderName} header."
            );
        }

        return ValueTask.FromResult(personalAccessToken);
    }
}
