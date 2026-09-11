using System.Net;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class WorkItemRevisionTests : AzureDevOpsClientTestsBase
{
    private const string WorkItemJson = """{"id":42,"rev":4,"fields":{},"url":"https://example.test/42"}""";
    private static readonly Dictionary<string, string> Fields = new() { ["System.State"] = "Resolved" };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DiagnosticCancellation_PreservesPatchErrorUnlessCallerCanceled(bool cancelCaller, bool taskCanceled)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new DiagnosticCancellationHandler(() =>
        {
            if (cancelCaller)
            {
                cancellation.Cancel();
            }

            return taskCanceled ? new TaskCanceledException("Diagnostic timeout") :
                new OperationCanceledException("Diagnostic canceled");
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri($"{CollectionUrl}/") };
        var client = new AzureDevOpsClient(httpClient, CreateOptions(null));

        if (cancelCaller)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.UpdateWorkItemAsync(42, Fields, cancellation.Token, 3));
            Assert.True(cancellation.IsCancellationRequested);
        }
        else
        {
            var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
                client.UpdateWorkItemAsync(42, Fields, cancellation.Token, 3));
            Assert.Contains("Original rejection", error.Message);
            Assert.False(cancellation.IsCancellationRequested);
        }

        Assert.Equal(new[] { HttpMethod.Patch, HttpMethod.Get }, handler.Methods);
    }

    private sealed class DiagnosticCancellationHandler : HttpMessageHandler
    {
        private readonly Func<Exception> _diagnosticException;

        public DiagnosticCancellationHandler(Func<Exception> diagnosticException)
        {
            _diagnosticException = diagnosticException;
        }

        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return request.Method == HttpMethod.Patch ?
                Task.FromResult(JsonResponse("""{"message":"Original rejection"}""", HttpStatusCode.BadRequest)) :
                Task.FromException<HttpResponseMessage>(_diagnosticException());
        }
    }

    [Fact]
    public async Task MatchingRevision_PrependsNumericTestBeforeFieldUpdates()
    {
        using var response = JsonResponse(WorkItemJson);
        var client = CreateClient(out var handler, response);

        var result = await client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, 3);

        Assert.Equal(4, result.Rev);
        Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var operations = body.RootElement;
        Assert.Equal(2, operations.GetArrayLength());
        Assert.Equal("test", operations[0].GetProperty("op").GetString());
        Assert.Equal("/rev", operations[0].GetProperty("path").GetString());
        Assert.Equal(3, operations[0].GetProperty("value").GetInt32());
        Assert.Equal("add", operations[1].GetProperty("op").GetString());
        Assert.Equal("/fields/System.State", operations[1].GetProperty("path").GetString());
        Assert.Equal("Resolved", operations[1].GetProperty("value").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    public async Task RejectedUpdate_WithChangedRevision_ReportsConflictWithoutRetry(HttpStatusCode status)
    {
        using var rejected = JsonResponse("""{"message":"Patch rejected"}""");
        rejected.StatusCode = status;
        using var current = JsonResponse(WorkItemJson);
        var client = CreateClient(out var handler, rejected, current);

        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, 3));

        Assert.Contains("changed since revision 3; current revision is 4", error.Message);
        Assert.Contains("reconcile", error.Message);
        Assert.Equal(new[] { HttpMethod.Patch, HttpMethod.Get }, handler.Requests.Select(request => request.Method));
        Assert.Contains("fields=System.Id", handler.Requests[1].RequestUri!.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 4)]
    [InlineData(HttpStatusCode.Forbidden, 3)]
    [InlineData(HttpStatusCode.Unauthorized, 3)]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation, 3)]
    public async Task OtherFailures_PreserveOriginalError(HttpStatusCode status, int revision)
    {
        using var rejected = JsonResponse("""{"message":"Original rejection"}""");
        rejected.StatusCode = status;
        using var current = JsonResponse(WorkItemJson);
        var client = CreateClient(out var handler, rejected, current);

        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, revision));

        Assert.DoesNotContain("changed since", error.Message);
        Assert.Contains(status is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation
            ? "Authentication" : "Original rejection", error.Message);
        Assert.Equal(status == HttpStatusCode.BadRequest ? 2 : 1, handler.Requests.Count);
    }

    [Fact]
    public async Task FailedRevisionRead_PreservesOriginalUpdateError()
    {
        using var rejected = JsonResponse("""{"message":"Original rejection"}""");
        rejected.StatusCode = HttpStatusCode.BadRequest;
        using var current = JsonResponse("""{"message":"Not readable"}""");
        current.StatusCode = HttpStatusCode.Forbidden;
        var client = CreateClient(out var handler, rejected, current);

        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, 3));

        Assert.Contains("Original rejection", error.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidRevision_FailsBeforeSendingRequest(int revision)
    {
        var client = CreateClient(out var handler);

        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, revision));

        Assert.Contains("positive", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OmittedRevisionFailure_DoesNotReadOrRetry()
    {
        using var rejected = JsonResponse("""{"message":"Original rejection"}""");
        rejected.StatusCode = HttpStatusCode.BadRequest;
        var client = CreateClient(out var handler, rejected);

        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken));

        Assert.Contains("Original rejection", error.Message);
        Assert.Single(handler.Requests);
    }
}
