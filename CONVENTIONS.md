# Task-writing conventions — AzureMCP

Applies to tasks and issues in this repository. This document is the source of truth for humans and AI tools. Development rules live in [`CONTRIBUTING.md`](CONTRIBUTING.md), while repository instructions for AI tools live in [`AGENTS.md`](AGENTS.md).

This is a living document and should be extended as the conventions mature.

## Language

Tasks, issues, comments, commits, and pull requests are written in English. Internal working notes may be written in Polish.

## Backlog items vs. issues

Future work is filed as a draft item on the AzureMCP GitHub Projects v2 board. Draft items keep speculative or not-yet-started work out of the repository Issues list.

A draft item is promoted to a GitHub Issue only immediately before implementation starts. Add the resulting issue to the board and remove the original draft item so the work is not duplicated.

Rule of thumb: if implementation is not starting now, use a draft backlog item rather than an issue.

## Task title

Format: `[Area] Description of the problem or topic`

Prefer describing the problem over prescribing a solution. A verb is acceptable for simple, unambiguous technical work where the action does not hide an unresolved design decision.

Examples:

- `[Reliability] Unsafe HTTP operations can be retried automatically`
- `[Testing] MCP stdio behavior is not covered end to end`
- `[Release] NuGet package and MCP manifest can report different versions`
- `[Tech] Update ModelContextProtocol to 2.2.0`

Use one of these areas when applicable: `MCP`, `Azure DevOps`, `HTTP`, `Reliability`, `Security`, `Testing`, `CI`, `Release`, `Observability`, `Architecture`, `Build`, `Docs`, or `Tech`.

## Task type

Use repository labels rather than encoding the type in the title: `bug`, `enhancement`, `documentation`, `architecture`, or `tech-debt`.

## Priority and size

Set board priority as follows:

- `P0`: risk of security failure, data loss, duplication, or uncontrolled external mutation
- `P1`: material production-readiness or quality gap
- `P2`: maintainability or developer-experience improvement

Set a board size of `XS`, `S`, `M`, `L`, or `XL`. Prefer work that fits in approximately 400 changed lines and 15 files. Split larger work into independently verifiable tasks.

## Task description

Every task must be self-contained. Include enough context to understand and implement the work without relying solely on an external document.

Use this structure:

```markdown
## Context

One to three sentences describing the problem and why it matters.

## Details

Concrete technical information, constraints, and scope boundaries.

## References

Relevant source files, analysis documents, external documentation, or related tasks.

## Definition of Done

- [ ] Concrete, objectively verifiable outcome
- [ ] Targeted tests pass
- [ ] Full solution builds and all tests pass
- [ ] No new build warnings
```

Definition of Done items must be objectively checkable. Avoid statements such as "works correctly."

Tasks that change Azure DevOps integration must state whether verification against a real Azure DevOps Server is required. Green tests using a stubbed `HttpMessageHandler` are not evidence of compatibility with a real server.

Tasks that change MCP hosting or transport must verify the actual server boundary where practical, including stdio output separation and protocol initialization. `StdioServerSmokeTests` (`tests/AzureDevOpsServer.Mcp.Tests/EndToEnd/StdioServerSmokeTests.cs`) already does this: it runs the packaged MCP server as a real stdio process against `StubAzureDevOpsServer`, an in-process loopback HTTP server standing in for an Azure DevOps Server collection. This proves the MCP process boundary and protocol wiring (stdio framing, initialization, `tools/list`, `tools/call`) — it does not prove REST API compatibility with a real server, so it never substitutes for the real-server verification called out above when request/response shapes or on-prem-specific behavior are in scope.

Tasks that add or change an MCP tool must include end-to-end reachability in the Definition of Done: registered, visible through `tools/list`, and callable through `tools/call`. The `StdioServerSmokeTests`/`StubAzureDevOpsServer` pair satisfies this mechanically for common cases; state in the task when a change needs additional stub coverage or real-server verification beyond it, so "manual testing needed?" has a clear answer instead of being re-litigated per task.

## Board workflow

The AzureMCP board uses these statuses:

- `Backlog`: accepted future work that is not ready to start
- `Ready`: sufficiently scoped and ready to implement
- `In progress`: currently being implemented
- `In review`: implementation has an open pull request
- `Done`: linked issue is closed after merge

Work on one task at a time. Move its status as work progresses and do not leave completed work in `In progress`.

## Pull requests

Pull requests are always opened ready for review. Do not create draft pull requests in this repository.

## Milestones

Group milestones by capability or version rather than time-based sprints. Capability names remain meaningful when delivery dates change.
