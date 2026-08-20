---
name: backlog-task
description: Pick up and implement the next AzureMCP backlog task end to end. Use when the user asks for the next task, the next backlog item, or to work through AzureMCP tasks one at a time.
---

# Backlog task workflow — AzureMCP

## 0. Project board

Work is tracked on the AzureMCP GitHub Projects v2 board owned by `eXoz00rd`:

- Project number: `4`
- Project ID: `PVT_kwHOBpFBus4Bg9mS`
- Status field ID: `PVTSSF_lAHOBpFBus4Bg9mSzhf7JSY`
- Priority field ID: `PVTSSF_lAHOBpFBus4Bg9mSzhf7JTI`
- Size field ID: `PVTSSF_lAHOBpFBus4Bg9mSzhf7JTM`

Status options:

| Status | Option ID |
|---|---|
| Backlog | `f75ad846` |
| Ready | `e18bf179` |
| In progress | `47fc9ee4` |
| In review | `aba860b9` |
| Done | `98236657` |

Move the item as work progresses. Let the board automation set `Done` when the linked issue closes after merge.

## 1. Check session state

- Inspect the current branch, worktree, commits ahead of `main`, and matching pull requests.
- Do not resume an in-flight branch without verifying whether its pull request is already merged.
- Preserve unrelated user changes in a dirty worktree.

## 2. Select one task

- Read both repository issues and draft items in `Backlog` or `Ready`.
- Prefer `P0`, then `P1`, then `P2`. Within the same priority, prefer correctness and reliability work over enhancements.
- Read the complete body and `CONVENTIONS.md` before starting.
- Choose one coherent task that fits approximately 400 changed lines and 15 files.
- If equally ranked tasks are available and the choice is not obvious, ask the user.
- If the selected task is a draft item, promote it to a GitHub Issue, add the issue to the board, and delete the old draft item.
- Move the issue to `In progress` before implementation.

## 3. Branch

Create a focused branch from an up-to-date `main`, using `fix/`, `feature/`, or `chore/` followed by a short description. Never implement directly on `main`.

## 4. Implement

- Use Rider MCP before changing backend code to inspect architecture, usages, dependency injection, logging, cancellation, and local conventions.
- Use the console only when Rider MCP cannot perform the operation or fails.
- Implement only the selected task.
- Never use primary constructors for services with injected dependencies.
- Guard logging with `if (_logger.IsEnabled(LogLevel.X))`.
- Async tests using a `CancellationTokenSource` pass `TestContext.Current.CancellationToken`.
- Add or update targeted tests for changed behavior.
- Do not create EF Core migrations manually.

## 5. Validate

After every modified backend file, use Rider MCP to reformat it and check file problems. Before review, run:

```bash
dotnet build AzureDevOpsServer.Mcp.slnx --configuration Release
dotnet test AzureDevOpsServer.Mcp.slnx --configuration Release --no-build
```

Run targeted tests while iterating and the full suite before review. No new warnings are allowed.

For Azure DevOps integration changes, distinguish stub-handler verification from testing against a real server. For MCP transport changes, verify the actual stdio server boundary.

## 6. Review and commit approval

- Mark the implementation ready for review and show the exact diff and validation results.
- Do not commit until the user gives explicit approval.
- Stage only approved code paths. Never stage unrelated files or use broad staging commands.
- Commit only code changes; do not include documentation or comments in a code commit.
- Use a concise summary line followed by short bullet points when a body is needed.
- Never include AI self-attribution in commits or pull requests.

## 7. Pull request

Push and pull-request creation each require explicit authorization. Open at most one focused draft pull request against `main` with:

```markdown
## Summary

- What changed and why

## Test plan

- [ ] What was actually run or observed

Closes #<issue-number>
```

Move the item to `In review` after the pull request is created.

## 8. Report

Report the selected task, implementation outcome, build and test results, and pull-request state concisely.
