using System.Net.Sockets;
using System.Security.Authentication;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed class ConnectionDiagnosticsHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception) when (ExceptionChain.Inner<AuthenticationException>(exception) is { } failure)
        {
            throw new AzureDevOpsClientException(
                $"The TLS connection to {Authority(request)} could not be established: {Sentence(failure.Message)} " +
                "On-premises servers often use a certificate from an internal certificate authority. Import that authority certificate into the machine trust store so .NET trusts it, " +
                "point SSL_CERT_FILE at a bundle that contains it, or use a collection URL whose certificate is already trusted."
            );
        }
        catch (HttpRequestException exception) when (ExceptionChain.Inner<SocketException>(exception) is { } failure)
        {
            throw new AzureDevOpsClientException(
                $"Azure DevOps Server at {Authority(request)} could not be reached: {Sentence(failure.Message)} " +
                "Verify the collection URL, that the host name resolves, and that the port is reachable from where this server runs."
            );
        }
    }

    private static string Sentence(string message)
    {
        var trimmed = message.TrimEnd();
        return trimmed.EndsWith('.') ? trimmed : $"{trimmed}.";
    }

    private static string Authority(HttpRequestMessage request)
    {
        return request.RequestUri?.GetLeftPart(UriPartial.Authority) ?? "the configured collection";
    }
}
