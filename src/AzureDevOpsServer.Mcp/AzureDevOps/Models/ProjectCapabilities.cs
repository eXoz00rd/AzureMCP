namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record ProjectCapabilities(
    ProjectProcessTemplate? ProcessTemplate,
    ProjectVersionControl? VersionControl);
