namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record GitDiffs(int AheadCount, int BehindCount, IReadOnlyList<GitChange>? Changes);