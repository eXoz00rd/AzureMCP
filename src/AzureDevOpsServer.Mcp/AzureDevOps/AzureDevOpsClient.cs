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
    private const int MaxErrorBodyLength = 500;
    private const int ProjectPageSize = 100;
    private const string ContinuationTokenHeader = "x-ms-continuationtoken";

    private readonly HttpClient _httpClient;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public AzureDevOpsClient(HttpClient httpClient, IOptions<AzureDevOpsServerOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<TeamProject>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = new List<TeamProject>();
        string? continuationToken = null;

        do
        {
            var requestUri = $"_apis/projects?api-version={ApiVersion(ApiArea.Core)}&$top={ProjectPageSize}";
            if (!string.IsNullOrEmpty(continuationToken))
            {
                requestUri += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
            }

            using var response = await _httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<ListResult<TeamProject>>(cancellationToken);
            if (page?.Value is not null)
            {
                projects.AddRange(page.Value);
            }

            continuationToken = response.Headers.TryGetValues(ContinuationTokenHeader, out var values) ?
                values.FirstOrDefault() :
                null;
        } while (!string.IsNullOrEmpty(continuationToken));

        return projects;
    }

    public async Task<ProjectDetails> GetProjectAsync(string? project, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"_apis/projects/{Uri.EscapeDataString(RequireProject(project))}?includeCapabilities=true&api-version={ApiVersion(ApiArea.Core)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        await EnsureSuccessAsync(response, cancellationToken);

        var details = await response.Content.ReadFromJsonAsync<ProjectDetails>(cancellationToken);
        return details ??
            throw new AzureDevOpsClientException("The project response could not be parsed.");
    }

    private string ApiVersion(ApiArea area)
    {
        return _options.Value.ApiVersionFor(area);
    }

    private static string RequireProject(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            throw new AzureDevOpsClientException(
                "A project is required for this operation. Pass a project name or set ADOS_DEFAULT_PROJECT."
            ) :
            project;
    }

    private static string ToRefName(string branch)
    {
        return branch.StartsWith("refs/", StringComparison.Ordinal) ?
            branch :
            $"refs/heads/{branch}";
    }

    private static HttpContent CreateJsonPatchContent(IReadOnlyDictionary<string, string> fields)
    {
        if (fields.Count == 0)
        {
            throw new AzureDevOpsClientException("At least one field is required.");
        }

        var operations = fields
                         .Select(field => new { op = "add", path = $"/fields/{field.Key}", value = field.Value })
                         .ToList();
        return new StringContent(
            JsonSerializer.Serialize(operations),
            Encoding.UTF8,
            "application/json-patch+json"
        );
    }

    private static string Scope(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            string.Empty :
            $"{Uri.EscapeDataString(project)}/";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation)
        {
            throw new AzureDevOpsClientException(
                "Authentication against Azure DevOps Server failed. Verify that the PAT is valid, not expired, and has the required scopes."
            );
        }

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new AzureDevOpsClientException(
            $"Azure DevOps Server request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Truncate(body)}"
        );
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaxErrorBodyLength ?
            value :
            value[..MaxErrorBodyLength];
    }

    private static string FieldsOrRelations(IReadOnlyList<string>? fields)
    {
        return fields is null || fields.Count == 0 ?
            "$expand=relations" :
            $"fields={Uri.EscapeDataString(string.Join(',', fields))}";
    }

    private static (string Text, bool Truncated) Limit(string value, int maxChars)
    {
        return value.Length <= maxChars ?
            (value, false) :
            (value[..maxChars], true);
    }

    private static bool IsBinaryContent(string content)
    {
        var sampleLength = Math.Min(content.Length, 8000);
        var replacementCount = 0;
        for (var i = 0; i < sampleLength; i++)
        {
            var character = content[i];
            if (character == '\0')
            {
                return true;
            }

            if (character == '�')
            {
                replacementCount++;
            }
        }

        return sampleLength > 0 && replacementCount * 100 / sampleLength >= 10;
    }
}
