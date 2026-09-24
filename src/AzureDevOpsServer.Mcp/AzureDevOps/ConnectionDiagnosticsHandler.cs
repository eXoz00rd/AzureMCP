using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using AzureDevOpsServer.Mcp.Configuration;
using Polly.Timeout;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed class ConnectionDiagnosticsHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Redirects rewrite the request URI, so keep the address that was actually asked for.
        var requested = request.RequestUri;
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
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
        catch (TimeoutRejectedException exception)
        {
            // Writes are never retried, and one that timed out may have been applied before the answer was lost.
            var isWrite = request.Method != HttpMethod.Get && request.Method != HttpMethod.Head;
            throw new AzureDevOpsClientException(
                $"Azure DevOps Server at {Authority(request)} did not answer within {exception.Timeout.TotalSeconds:0} seconds. " +
                "Nothing refused the connection, so a firewall or proxy that silently drops it is the usual cause: " +
                "verify that the host and port are reachable from where this server runs." +
                (isWrite ? " The change may still have been applied, so check Azure DevOps before trying it again." : string.Empty)
            );
        }

        // Every API this server calls answers with JSON or plain text, so a web page means the request never reached one.
        // 203 is left to the client, which reports it as the rejected credential it is.
        if (response.IsSuccessStatusCode &&
            response.StatusCode != HttpStatusCode.NonAuthoritativeInformation &&
            string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            var answered = response.RequestMessage?.RequestUri ?? request.RequestUri;
            response.Dispose();
            throw new AzureDevOpsClientException(
                $"Azure DevOps Server answered {Location(requested)} with a web page instead of API data{RedirectNote(requested, answered)}. " +
                "The collection URL most likely points to a site rather than to a collection, or a proxy or sign-in page intercepted the request. " +
                $"Check {AzureDevOpsServerOptions.CollectionUrlVariable}: it must be the collection's own address, for example https://devops.example.local/tfs/DefaultCollection."
            );
        }

        return response;
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

    private static string Location(Uri? uri)
    {
        return uri?.GetLeftPart(UriPartial.Path) ?? "the request";
    }

    private static string RedirectNote(Uri? requested, Uri? answered)
    {
        return answered is not null && Location(answered) != Location(requested) ?
            $" after redirecting it to {Location(answered)}" :
            string.Empty;
    }
}
