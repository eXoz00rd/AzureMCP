using System.Security.Authentication;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed class TlsDiagnosticsHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception) when (IsTlsFailure(exception))
        {
            throw new AzureDevOpsClientException(
                $"The TLS connection to {request.RequestUri?.GetLeftPart(UriPartial.Authority)} could not be established: {exception.InnerException?.Message ?? exception.Message} " +
                "On-premises servers often use a certificate from an internal certificate authority. Import that authority certificate into the machine trust store so .NET trusts it, " +
                "or use a collection URL whose certificate is already trusted."
            );
        }
    }

    private static bool IsTlsFailure(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }
}
