using System.Net;
using System.Text.Json;
using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class WorkItemRevisionTests : AzureDevOpsClientTestsBase
{
    private const string WorkItemJson = """{"id":42,"rev":4,"fields":{},"url":"https://example.test/42"}""";
    private static readonly Dictionary<string, string> Fields = new() { ["System.State"] = "Resolved" };
    private const string RevisionError = """{"message":"Original rejection","typeKey":"WorkItemRevisionMismatchException"}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedDiagnosticFailure_PreservesPatchError(bool ioFailure)
    {
        using var handler = new DiagnosticCancellationHandler(() =>
            ioFailure ? new IOException("Read failed") : new InvalidOperationException("Transport failed"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri($"{CollectionUrl}/") };
        var client = new AzureDevOpsClient(httpClient, CreateOptions(null));
        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, 3));
        Assert.Contains("Original rejection", error.Message);
        Assert.Equal(new[] { HttpMethod.Patch, HttpMethod.Get }, handler.Methods);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
    [InlineData(HttpStatusCode.OK)]
    public async Task LargeDiagnosticBody_PreservesPatchErrorWithoutBuffering(HttpStatusCode status)
    {
        using var rejected = JsonResponse(RevisionError, HttpStatusCode.BadRequest);
        using var content = new StreamingErrorContent(new string('x', ResponseLimits.DefaultMaxChars + 1));
        using var diagnostic = new HttpResponseMessage(status) { Content = content };
        var client = CreateClient(out var handler, rejected, diagnostic);
        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, 3));
        Assert.Contains("Original rejection", error.Message);
        Assert.Equal(status == HttpStatusCode.OK ? 1 : 0, content.StreamReads);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacySignature_RemainsCallableWithoutRevision(bool tool)
    {
        using var response = JsonResponse(WorkItemJson);
        var client = CreateClient(out var handler, response);
        object target = tool ? new AzureDevOpsServer.Mcp.Tools.WorkItemTools(client, CreateOptions(null)) : client;
        var fieldType = tool ? typeof(Dictionary<string, string>) : typeof(IReadOnlyDictionary<string, string>);
        var method = target.GetType().GetMethod("UpdateWorkItemAsync", [typeof(int), fieldType, typeof(CancellationToken)]);
        Assert.NotNull(method);
        var call = Assert.IsAssignableFrom<Task>(method.Invoke(target, [42, Fields, TestContext.Current.CancellationToken]));
        await call;
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal("add", Assert.Single(body.RootElement.EnumerateArray()).GetProperty("op").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorBody_IsStreamedOnceAndReportsTruncation(bool oversized)
    {
        var payload = oversized ? new string('x', ResponseLimits.DefaultMaxChars + 1) : """{"message":"Invalid field"}""";
        using var content = new StreamingErrorContent(payload);
        using var rejected = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content };
        var client = CreateClient(out var handler, rejected);
        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, 3));
        Assert.Single(handler.Requests);
        Assert.Equal(1, content.StreamReads);
        Assert.Equal(oversized, error.Message.Contains("Error response truncated.", StringComparison.Ordinal));
        Assert.True(error.Message.Length < 1000);
        if (!oversized)
        {
            Assert.Contains("Invalid field", error.Message);
        }
    }

    private sealed class StreamingErrorContent : HttpContent
    {
        private readonly string _payload;

        public StreamingErrorContent(string payload)
        {
            _payload = payload;
        }

        public int StreamReads { get; private set; }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            StreamReads++;
            return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_payload)));
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("Error responses must not be buffered or read twice.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Theory]
    [InlineData("""{"id":42,"fields":{}}""")]
    [InlineData("""{"id":42,"rev":0,"fields":{}}""")]
    [InlineData("""{"id":42,"rev":-1,"fields":{}}""")]
    [InlineData("""{"rev":"invalid"}""")]
    [InlineData("""{"rev":2147483648}""")]
    [InlineData("[]")]
    [InlineData("invalid json")]
    public async Task InvalidDiagnosticRevision_PreservesOriginalError(string json)
    {
        using var rejected = JsonResponse(RevisionError, HttpStatusCode.BadRequest);
        using var current = JsonResponse(json);
        var client = CreateClient(out var handler, rejected, current);
        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, 3));
        Assert.Contains("Original rejection", error.Message);
        Assert.DoesNotContain("current revision", error.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("""{"message":"Invalid field","typeKey":"ValidationException"}""")]
    [InlineData("""{"message":"Invalid field"}""")]
    [InlineData("""{"message":"Invalid field","typeKey":42}""")]
    [InlineData("Invalid field")]
    [InlineData("[]")]
    public async Task UnrecognizedPatchError_DoesNotDiagnoseConcurrentChanges(string json)
    {
        using var rejected = JsonResponse(json, HttpStatusCode.BadRequest);
        using var current = JsonResponse(WorkItemJson);
        var client = CreateClient(out var handler, rejected, current);
        var error = await Assert.ThrowsAsync<AzureDevOpsClientException>(() =>
            client.UpdateWorkItemAsync(42, Fields, TestContext.Current.CancellationToken, 3));
        Assert.Contains(AzureDevOpsClient.ExtractErrorMessage(json), error.Message);
        Assert.DoesNotContain("changed since", error.Message);
        Assert.Single(handler.Requests);
    }

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
                Task.FromResult(JsonResponse(RevisionError, HttpStatusCode.BadRequest)) :
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
        using var rejected = JsonResponse(RevisionError);
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
    [InlineData(HttpStatusCode.BadRequest, 3)]
    [InlineData(HttpStatusCode.Conflict, 3)]
    [InlineData(HttpStatusCode.PreconditionFailed, 3)]
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
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FailedRevisionRead_PreservesOriginalUpdateError()
    {
        using var rejected = JsonResponse(RevisionError);
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
