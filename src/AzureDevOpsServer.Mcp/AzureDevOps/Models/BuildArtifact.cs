namespace AzureDevOpsServer.Mcp.AzureDevOps.Models;

public sealed record BuildArtifact(int Id, string Name, ArtifactResource? Resource);
