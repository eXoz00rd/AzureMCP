namespace AzureDevOpsServer.Mcp.Configuration;

public sealed class AzureDevOpsServerOptions
{
    public const string CollectionUrlVariable = "ADOS_COLLECTION_URL";
    public const string PersonalAccessTokenVariable = "ADOS_PAT";
    public const string DefaultProjectVariable = "ADOS_DEFAULT_PROJECT";
    public const string ApiVersionVariable = "ADOS_API_VERSION";
    public const string WorkItemApiVersionVariable = "ADOS_API_VERSION_WIT";
    public const string GitApiVersionVariable = "ADOS_API_VERSION_GIT";
    public const string BuildApiVersionVariable = "ADOS_API_VERSION_BUILD";
    public const string ReleaseApiVersionVariable = "ADOS_API_VERSION_RELEASE";
    public const string WikiApiVersionVariable = "ADOS_API_VERSION_WIKI";
    public const string WorkItemCommentsApiVersionVariable = "ADOS_API_VERSION_WIT_COMMENTS";
    public const string ToolsetsVariable = "ADOS_TOOLSETS";
    public const string ReadOnlyVariable = "ADOS_READ_ONLY";
    public const string LogLevelVariable = "ADOS_LOG_LEVEL";
    public const string TransportVariable = "ADOS_TRANSPORT";
    public const string HttpUrlVariable = "ADOS_HTTP_URL";
    public const string HttpPathVariable = "ADOS_HTTP_PATH";
    public const string HttpTokenVariable = "ADOS_HTTP_TOKEN";
    public const string HttpAllowAnonymousVariable = "ADOS_HTTP_ALLOW_ANONYMOUS";
    public const string HttpAllowedOriginsVariable = "ADOS_HTTP_ALLOWED_ORIGINS";
    public const string DefaultApiVersion = "7.0";
    public const string DefaultWorkItemCommentsApiVersion = "7.0-preview.3";

    // Loopback, so exposing a PAT-backed endpoint on a routable interface is always an explicit choice.
    public const string DefaultHttpUrl = "http://127.0.0.1:8080";
    public const string DefaultHttpPath = "/mcp";

    public string CollectionUrl { get; set; } = string.Empty;

    public string PersonalAccessToken { get; set; } = string.Empty;

    public string? DefaultProject { get; set; }

    public string ApiVersion { get; set; } = DefaultApiVersion;

    public string? WorkItemApiVersion { get; set; }

    public string? GitApiVersion { get; set; }

    public string? BuildApiVersion { get; set; }

    public string? ReleaseApiVersion { get; set; }

    public string? WikiApiVersion { get; set; }

    public string WorkItemCommentsApiVersion { get; set; } = DefaultWorkItemCommentsApiVersion;

    public string? Toolsets { get; set; }

    public bool ReadOnly { get; set; }

    public string? Transport { get; set; }

    public string HttpUrl { get; set; } = DefaultHttpUrl;

    public string HttpPath { get; set; } = DefaultHttpPath;

    public string? HttpToken { get; set; }

    public bool HttpAllowAnonymous { get; set; }

    public string? HttpAllowedOrigins { get; set; }

    public void LoadFromEnvironment()
    {
        CollectionUrl = Environment.GetEnvironmentVariable(CollectionUrlVariable) ?? string.Empty;
        PersonalAccessToken = Environment.GetEnvironmentVariable(PersonalAccessTokenVariable) ?? string.Empty;
        DefaultProject = Environment.GetEnvironmentVariable(DefaultProjectVariable);
        ApiVersion = Environment.GetEnvironmentVariable(ApiVersionVariable) ?? DefaultApiVersion;
        WorkItemApiVersion = Environment.GetEnvironmentVariable(WorkItemApiVersionVariable);
        GitApiVersion = Environment.GetEnvironmentVariable(GitApiVersionVariable);
        BuildApiVersion = Environment.GetEnvironmentVariable(BuildApiVersionVariable);
        ReleaseApiVersion = Environment.GetEnvironmentVariable(ReleaseApiVersionVariable);
        WikiApiVersion = Environment.GetEnvironmentVariable(WikiApiVersionVariable);
        WorkItemCommentsApiVersion = Environment.GetEnvironmentVariable(WorkItemCommentsApiVersionVariable) ??
            DefaultWorkItemCommentsApiVersion;
        Toolsets = Environment.GetEnvironmentVariable(ToolsetsVariable);
        ReadOnly = ParseBoolean(Environment.GetEnvironmentVariable(ReadOnlyVariable));
        Transport = Environment.GetEnvironmentVariable(TransportVariable);
        HttpUrl = Environment.GetEnvironmentVariable(HttpUrlVariable) ?? DefaultHttpUrl;
        HttpPath = Environment.GetEnvironmentVariable(HttpPathVariable) ?? DefaultHttpPath;
        HttpToken = Environment.GetEnvironmentVariable(HttpTokenVariable);
        HttpAllowAnonymous = ParseBoolean(Environment.GetEnvironmentVariable(HttpAllowAnonymousVariable));
        HttpAllowedOrigins = Environment.GetEnvironmentVariable(HttpAllowedOriginsVariable);
    }

    private static bool ParseBoolean(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("1", StringComparison.Ordinal) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    public string ApiVersionFor(ApiArea area)
    {
        var areaVersion = area switch
        {
            ApiArea.WorkItems => WorkItemApiVersion,
            ApiArea.Git => GitApiVersion,
            ApiArea.Build => BuildApiVersion,
            ApiArea.Release => ReleaseApiVersion,
            ApiArea.Wiki => WikiApiVersion,
            _ => null
        };

        return string.IsNullOrWhiteSpace(areaVersion) ? ApiVersion : areaVersion;
    }
}
