# Agent instructions — AzureMCP

**This file is the single source of truth for AI tools working on this repository.**

| Document | Scope |
|---|---|
| [`CONVENTIONS.md`](CONVENTIONS.md) | Tasks, issues, backlog, and milestones |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Development workflow, commits, pull requests, and CI |
| [`README.md`](README.md) | Supported tools, configuration, usage, and releases |

## Verify before you trust

Read the implementation before assuming a tool, option, or behavior exists. Azure DevOps Server differs from Azure DevOps Services and varies by on-premises version. Do not infer endpoints, API versions, response shapes, or authentication behavior from the cloud service.

Tests using `StubHttpMessageHandler` verify client behavior against constructed HTTP responses. They are not evidence that an integration works against a real server.

## Build and test

Prefer Rider MCP for .NET and C# work when available. Use the console only when MCP tools are unavailable or fail.

```bash
dotnet restore AzureDevOpsServer.Mcp.slnx
dotnet format --verify-no-changes --no-restore
dotnet build AzureDevOpsServer.Mcp.slnx --configuration Release --no-restore
dotnet run --project tests/AzureDevOpsServer.Mcp.Tests --configuration Release --no-build
```

Never ask the user to perform verification that can be automated. If real-server verification is required and no suitable instance is available, report the limitation.

## Code rules

- No new warnings; `TreatWarningsAsErrors` applies repository-wide.
- Guard logging work with `if (_logger.IsEnabled(LogLevel.X))`.
- Async tests using a `CancellationTokenSource` pass `TestContext.Current.CancellationToken`.
- Use constructor injection with interfaces and `readonly` fields.
- Never use primary constructors for services with injected dependencies.
- Do not create EF Core migrations manually; use EF Core tooling if EF Core is adopted.
- Add comments only for constraints or decisions the code cannot express.
- Keep secrets, especially PATs, out of source, logs, arguments, fixtures, and committed configuration.

Never add self-attribution to commits or pull request bodies.

## Azure DevOps and MCP rules

- Keep API-area versions in configuration; do not scatter hard-coded versions through clients.
- Preserve compatibility with supported on-premises Azure DevOps Server versions.
- Treat authentication redirects and HTTP 203 responses as authentication failures.
- Never disable TLS certificate validation.
- Bound potentially large responses and report truncation.
- Mark mutability accurately so read-only mode excludes every write-capable tool.
- A tool is complete only when registered in `Toolsets`, visible through `tools/list`, and reachable through `tools/call`.

## Scope and workflow

- Work on one task at a time.
- Keep pull requests focused: approximately 400 changed lines and 15 files at most.
- Open pull requests ready for review, never as drafts.
- Do not commit until the work is reviewed and the user gives explicit approval.
- Write code, comments, commits, pull requests, tasks, and issues in English. Internal notes may be in Polish.
