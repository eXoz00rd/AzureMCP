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
    private const int MaxProjectPages = 100;
    private const string ContinuationTokenHeader = "x-ms-continuationtoken";

    private readonly HttpClient _httpClient;
    private readonly IOptions<AzureDevOpsServerOptions> _options;

    public AzureDevOpsClient(HttpClient httpClient, IOptions<AzureDevOpsServerOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<LimitedList<TeamProject>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = new List<TeamProject>();
        string? continuationToken = null;
        var pageCount = 0;
        var truncated = false;

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

            pageCount++;

            continuationToken = response.Headers.TryGetValues(ContinuationTokenHeader, out var values) ?
                values.FirstOrDefault() :
                null;

            // A malformed or looping continuation token from the server would otherwise page
            // forever, so stop once a generous page ceiling is reached even if the server still
            // offers a token, and report that projects beyond it were left out.
            if (pageCount >= MaxProjectPages && !string.IsNullOrEmpty(continuationToken))
            {
                truncated = true;
                continuationToken = null;
            }
        } while (!string.IsNullOrEmpty(continuationToken));

        return new LimitedList<TeamProject>(projects, truncated);
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

    // Trims to top and reports whether more items came back than that. Most callers request
    // top + 1 from the server, so a full page means there is more beyond it; GetRepositoryItemsAsync
    // instead fetches the whole unbounded result and relies on this to cap it after the fact.
    private static LimitedList<T> LimitTo<T>(IReadOnlyList<T> items, int top)
    {
        return items.Count <= top ?
            new LimitedList<T>(items, false) :
            new LimitedList<T>(items.Take(top).ToList(), true);
    }

    private static string ToRefName(string branch)
    {
        return branch.StartsWith("refs/", StringComparison.Ordinal) ?
            branch :
            $"refs/heads/{branch}";
    }

    private static HttpContent CreateJsonPatchContent(IReadOnlyDictionary<string, string> fields, int? expectedRevision = null)
    {
        if (fields.Count == 0)
        {
            throw new AzureDevOpsClientException("At least one field is required.");
        }

        var operations = fields
                         .Select(field => new { op = "add", path = $"/fields/{field.Key}", value = (object)field.Value })
                         .ToList();
        if (expectedRevision is not null)
        {
            operations.Insert(0, new { op = "test", path = "/rev", value = (object)expectedRevision.Value });
        }

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
            $"Azure DevOps Server request failed with status {(int)response.StatusCode} ({response.StatusCode}). {ExtractErrorMessage(body)}"
        );
    }

    internal static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "The response body was empty.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return Truncate(text);
                }
            }
        }
        catch (JsonException)
        {
            // The body is not JSON, so fall back to the raw text below.
        }

        return Truncate(body);
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaxErrorBodyLength ?
            value :
            value[..MaxErrorBodyLength];
    }

    private static string FieldsOrRelations(IReadOnlyList<string>? fields, bool includeRelations)
    {
        return includeRelations || fields is null || fields.Count == 0 ?
            "$expand=relations" :
            $"fields={Uri.EscapeDataString(string.Join(',', fields))}";
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
