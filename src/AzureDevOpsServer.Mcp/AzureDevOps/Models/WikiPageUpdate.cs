namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record WikiPageUpdate(string Path, bool Created, string Version);
