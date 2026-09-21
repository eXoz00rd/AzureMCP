using System.Text;
using AzureDevOpsServer.Mcp.AzureDevOps;

namespace AzureDevOpsServer.Mcp.Tests.Infrastructure;

public sealed class StubCredentialProvider : IAzureDevOpsCredentialProvider
{
    private readonly string[] _tokens;
    private int _calls;

    public StubCredentialProvider(params string[] tokens)
    {
        _tokens = tokens;
    }

    public ValueTask<string> GetPersonalAccessTokenAsync(CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _calls) - 1;
        return ValueTask.FromResult(_tokens[Math.Min(index, _tokens.Length - 1)]);
    }

    public static string ExpectedAuthorization(string personalAccessToken)
    {
        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($":{personalAccessToken}"));
    }
}
