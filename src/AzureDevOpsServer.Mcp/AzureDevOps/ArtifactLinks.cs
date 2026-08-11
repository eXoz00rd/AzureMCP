namespace AzureDevOpsServer.Mcp.AzureDevOps;

public static class ArtifactLinks
{
    public const string Relation = "ArtifactLink";
    public const string PullRequestName = "Pull Request";
    public const string CommitName = "Fixed in Commit";
    public const string BuildName = "Integrated in build";

    private const string PullRequestPrefix = "vstfs:///Git/PullRequestId/";
    private const string CommitPrefix = "vstfs:///Git/Commit/";
    private const string BuildPrefix = "vstfs:///Build/Build/";

    public static string PullRequestUrl(Guid projectId, Guid repositoryId, int pullRequestId)
    {
        return $"{PullRequestPrefix}{projectId}%2F{repositoryId}%2F{pullRequestId}";
    }

    public static string? NameFor(string url)
    {
        if (url.StartsWith(PullRequestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return PullRequestName;
        }

        if (url.StartsWith(CommitPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return CommitName;
        }

        return url.StartsWith(BuildPrefix, StringComparison.OrdinalIgnoreCase) ?
            BuildName :
            null;
    }
}
