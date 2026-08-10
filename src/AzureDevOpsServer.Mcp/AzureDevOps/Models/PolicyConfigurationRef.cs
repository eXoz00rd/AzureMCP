namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record PolicyConfigurationRef(bool? IsBlocking, bool? IsEnabled, PolicyTypeRef? Type);
