namespace AzureDevOpsServer.Mcp.AzureDevOps;

// Consumed by a pooled HttpClient handler: resolve the credential inside the call, never cache it in a field.
public interface IAzureDevOpsCredentialProvider
{
    ValueTask<string> GetPersonalAccessTokenAsync(CancellationToken cancellationToken);
}
