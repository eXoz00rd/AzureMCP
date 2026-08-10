namespace AzureDevOpsServer.Mcp.Configuration;

public sealed class AzureDevOpsServerOptions
{
    public const string CollectionUrlVariable = "ADOS_COLLECTION_URL";
    public const string PersonalAccessTokenVariable = "ADOS_PAT";
    public const string DefaultProjectVariable = "ADOS_DEFAULT_PROJECT";
    public const string ApiVersionVariable = "ADOS_API_VERSION";
    public const string LogLevelVariable = "ADOS_LOG_LEVEL";
    public const string DefaultApiVersion = "7.0";

    public string CollectionUrl { get; set; } = string.Empty;

    public string PersonalAccessToken { get; set; } = string.Empty;

    public string? DefaultProject { get; set; }

    public string ApiVersion { get; set; } = DefaultApiVersion;

    public void LoadFromEnvironment()
    {
        CollectionUrl = Environment.GetEnvironmentVariable(CollectionUrlVariable) ?? string.Empty;
        PersonalAccessToken = Environment.GetEnvironmentVariable(PersonalAccessTokenVariable) ?? string.Empty;
        DefaultProject = Environment.GetEnvironmentVariable(DefaultProjectVariable);
        ApiVersion = Environment.GetEnvironmentVariable(ApiVersionVariable) ?? DefaultApiVersion;
    }
}