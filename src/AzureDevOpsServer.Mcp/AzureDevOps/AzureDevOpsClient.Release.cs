using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps.Models;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.Extensions.Options;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

public sealed partial class AzureDevOpsClient
{
    public async Task<IReadOnlyList<ReleaseApproval>> GetReleaseApprovalsAsync(
        string? project,
        int? releaseId,
        int top,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"{Scope(RequireProject(project))}_apis/release/approvals?statusFilter=pending&$top={top}&api-version={ApiVersion(ApiArea.Release)}";
        if (releaseId is not null)
        {
            requestUri += $"&releaseIdsFilter={releaseId}";
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<ReleaseApproval>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<ReleaseApproval> UpdateReleaseApprovalAsync(
        string? project,
        int approvalId,
        string status,
        string? comment,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = status.ToLowerInvariant() switch
        {
            "approve" or "approved" => "approved",
            "reject" or "rejected" => "rejected",
            "reassign" => "reassigned",
            _ => throw new AzureDevOpsClientException("Approval status must be approved or rejected.")
        };

        using var response = await _httpClient.PatchAsJsonAsync(
            $"{Scope(RequireProject(project))}_apis/release/approvals/{approvalId}?api-version={ApiVersion(ApiArea.Release)}",
            new { status = normalizedStatus, comments = comment ?? string.Empty },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var approval = await response.Content.ReadFromJsonAsync<ReleaseApproval>(cancellationToken);
        return approval ??
            throw new AzureDevOpsClientException($"The response for approval {approvalId} could not be parsed.");
    }

    public async Task<ReleaseEnvironment> DeployReleaseEnvironmentAsync(
        string? project,
        int releaseId,
        int environmentId,
        string? comment,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PatchAsJsonAsync(
            $"{Scope(RequireProject(project))}_apis/release/releases/{releaseId}/environments/{environmentId}?api-version={ApiVersion(ApiArea.Release)}",
            new { status = "inProgress", comment = comment ?? string.Empty },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var environment = await response.Content.ReadFromJsonAsync<ReleaseEnvironment>(cancellationToken);
        return environment ??
            throw new AzureDevOpsClientException(
                $"The deployment response for environment {environmentId} could not be parsed."
            );
    }

    public async Task<IReadOnlyList<ReleaseDefinition>> GetReleaseDefinitionsAsync(
        string? project,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/release/definitions?api-version={ApiVersion(ApiArea.Release)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<ReleaseDefinition>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<IReadOnlyList<Release>> GetReleasesAsync(
        string? project,
        int? definitionId,
        int top,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"{Scope(RequireProject(project))}_apis/release/releases?api-version={ApiVersion(ApiArea.Release)}&$top={top}";
        if (definitionId is not null)
        {
            requestUri += $"&definitionId={definitionId}";
        }

        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ListResult<Release>>(cancellationToken);
        return result?.Value ?? [];
    }

    public async Task<Release> GetReleaseAsync(
        string? project,
        int releaseId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{Scope(RequireProject(project))}_apis/release/releases/{releaseId}?api-version={ApiVersion(ApiArea.Release)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var release = await response.Content.ReadFromJsonAsync<Release>(cancellationToken);
        return release ??
            throw new AzureDevOpsClientException($"The response for release {releaseId} could not be parsed.");
    }

    public async Task<Release> CreateReleaseAsync(
        string? project,
        int definitionId,
        string? description,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{Scope(RequireProject(project))}_apis/release/releases?api-version={ApiVersion(ApiArea.Release)}",
            new { definitionId, description },
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var release = await response.Content.ReadFromJsonAsync<Release>(cancellationToken);
        return release ??
            throw new AzureDevOpsClientException("The create release response could not be parsed.");
    }
}
