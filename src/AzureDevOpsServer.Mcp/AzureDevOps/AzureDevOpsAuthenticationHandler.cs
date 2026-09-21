using System.Net.Http.Headers;
using System.Text;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed class AzureDevOpsAuthenticationHandler : DelegatingHandler
{
    private readonly IAzureDevOpsCredentialProvider _credentialProvider;

    public AzureDevOpsAuthenticationHandler(IAzureDevOpsCredentialProvider credentialProvider)
    {
        _credentialProvider = credentialProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var personalAccessToken = await _credentialProvider.GetPersonalAccessTokenAsync(cancellationToken);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{personalAccessToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        return await base.SendAsync(request, cancellationToken);
    }
}
