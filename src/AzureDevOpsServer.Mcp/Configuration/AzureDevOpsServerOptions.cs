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
    public const string DefaultApiVersion = "7.0";
    public const string DefaultWorkItemCommentsApiVersion = "7.0-preview.3";

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
