# Contributing

## Requirements

- .NET 10 SDK — verify with `dotnet --list-sdks`
- Git
- Rider, Visual Studio 2022, or VS Code
- An on-premises Azure DevOps Server only when integration verification is required

```bash
dotnet restore AzureDevOpsServer.Mcp.slnx
dotnet build AzureDevOpsServer.Mcp.slnx
dotnet run --project tests/AzureDevOpsServer.Mcp.Tests
```

## Workflow

1. Create a branch from `main`, such as `feat/short-description` or `fix/short-description`.
2. Make and verify one focused change.
3. Open a pull request to `main` ready for review. Never create a draft.
4. Merge by squashing and delete the branch.

Do not push directly to `main`.

## Commits

- First line: concise imperative summary without a leading `#`.
- Add details only when useful, as bullet points.
- Keep one coherent change per commit and avoid long messages.
- Never add self-attribution, AI-identifying text, or `Co-Authored-By` trailers.

## Pull requests

- Use a concise title in the same style as a commit summary.
- Keep changes focused: approximately 400 changed lines and 15 files at most.
- Open pull requests ready for review, never as drafts.
- Include `## Summary` and `## Test plan` sections.
- Do not commit or push until the user gives explicit approval.

## C# code

- `Directory.Build.props` enables nullable references, implicit usings, and warnings as errors.
- `.editorconfig` defines formatting; run `dotnet format --verify-no-changes`.
- `.gitattributes` pins text files to CRLF.
- Guard logging work with `if (_logger.IsEnabled(LogLevel.X))`.
- Async tests using a `CancellationTokenSource` pass `TestContext.Current.CancellationToken`.
- Use constructor injection with interfaces and `readonly` fields.
- Never use primary constructors for injected services.
- Add comments only for non-obvious constraints or decisions.
- Never disable TLS validation or expose PAT values.

## Integration changes

Stub-handler tests do not prove compatibility with a real Azure DevOps Server. Tasks changing the integration must state whether real-server verification is required and record what was verified.

A tool is complete only when registered, visible through `tools/list`, and invokable through `tools/call`. Verify annotations and ensure read-only mode excludes write tools.

## Documentation

`Docs/` contains ignored internal working documents. Public documentation belongs in tracked root files.

## Before a pull request

```bash
dotnet restore AzureDevOpsServer.Mcp.slnx
dotnet format --verify-no-changes --no-restore
dotnet build AzureDevOpsServer.Mcp.slnx --configuration Release --no-restore
dotnet run --project tests/AzureDevOpsServer.Mcp.Tests --configuration Release --no-build
```
