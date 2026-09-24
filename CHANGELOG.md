# Changelog

All notable changes to this project are documented in this file. Versions
correspond to tags published by [`release.yml`](.github/workflows/release.yml),
which also publishes to [NuGet.org](https://www.nuget.org/packages/AzureDevOpsServer.Mcp).

## [Unreleased]

## [0.2.1] — 2026-09-24

### Security
- **The server no longer keeps cookies from Azure DevOps.** Over HTTP every caller shares one pooled connection handler, and in 0.2.0 it stored the cookies Azure DevOps set and sent them with later requests from other callers, next to those callers' own PATs. Each request now carries its caller's PAT and nothing another caller left behind. stdio serves a single user and was not affected ([#64](https://github.com/eXoz00rd/AzureMCP/pull/64))

## [0.2.0] — 2026-09-24

### Added
- **Streamable HTTP transport** — `ADOS_TRANSPORT=http` serves MCP over a stateless HTTP endpoint behind a shared bearer token, so Open WebUI and other shared front ends can use the server. The access guard runs on the endpoint that routing has selected, rejects unknown browser origins, and allows anonymous access only on loopback ([#55](https://github.com/eXoz00rd/AzureMCP/pull/55))
- **Each HTTP caller's own PAT** — over HTTP every request carries its caller's PAT in `X-Azure-DevOps-Pat`, so Azure DevOps attributes every comment, vote, and queued build to the person who asked. `ADOS_PAT` is refused over HTTP so no request can fall back to a shared identity ([#56](https://github.com/eXoz00rd/AzureMCP/pull/56))
- **Health endpoint and container logging** — `/healthz` answers probes without a token, logs go to the ordinary console over HTTP, and an address that cannot be bound ends with one readable line ([#57](https://github.com/eXoz00rd/AzureMCP/pull/57))
- **Container image** — `ghcr.io/exoz00rd/azuremcp` for `linux/amd64` and `linux/arm64` on the chiseled ASP.NET 10 runtime, non-root, signed with cosign, with a build provenance attestation and an SBOM ([#58](https://github.com/eXoz00rd/AzureMCP/pull/58))
- **Helm chart** — `oci://ghcr.io/exoz00rd/charts/azuremcp` with a hardened pod, a `NetworkPolicy` admitting only Open WebUI, and an internal certificate authority from a ConfigMap that keeps the public roots ([#59](https://github.com/eXoz00rd/AzureMCP/pull/59))

### Changed
- **The Open WebUI plugin calls the server over HTTP** instead of downloading and starting it inside Open WebUI. Its valves are now the server URL, its token, and the timeout; the collection and certificate settings moved to the server. Releases no longer attach the self-contained linux-x64 server ([#60](https://github.com/eXoz00rd/AzureMCP/pull/60))
- **The package needs the ASP.NET Core runtime** in every mode, stdio included. The .NET 10 SDK already ships it ([#55](https://github.com/eXoz00rd/AzureMCP/pull/55))

## [0.1.3] — 2026-09-23

### Added
- **Open WebUI plugin** (`azure_devops_openwebui.py`) — generated from the tools this server registers; it downloads the self-contained linux-x64 server attached to the release, verifies its SHA-256, and starts it for each call with the calling user's own PAT ([#49](https://github.com/eXoz00rd/AzureMCP/pull/49), [#51](https://github.com/eXoz00rd/AzureMCP/pull/51))
- **Support for an internal certificate authority** in the plugin — its PEM is merged with the container's system roots into the trust bundle handed to the server ([#52](https://github.com/eXoz00rd/AzureMCP/pull/52))

### Changed
- **A server that cannot be reached is explained instead of surfacing a raw socket or SSL failure.** An untrusted certificate is reported on the first attempt, because retrying it can only produce the same answer ([#53](https://github.com/eXoz00rd/AzureMCP/pull/53))

62 tools.

**Full changelog:** https://github.com/eXoz00rd/AzureMCP/compare/v0.1.2...v0.1.3

## [0.1.2] — 2026-09-14

Eight changes since v0.1.1, all additive or defensive — no breaking changes.

### Added
- **`expectedRevision` on `update_work_item`** — optional optimistic-concurrency guard: pass the revision you last read and a concurrent edit is reported with the current revision and reconciliation guidance instead of being silently overwritten ([#29](https://github.com/eXoz00rd/AzureMCP/pull/29))
- **`includeRelations` on `get_work_item`/`get_work_items`** — fetches work item relations without forcing a full-field pull; combines with a `fields` filter ([#26](https://github.com/eXoz00rd/AzureMCP/pull/26))
- **`descriptionFormat` on `get_work_item`, `get_work_items`, `get_work_item_revisions`** — `text` strips Azure DevOps' rich-text HTML from fields like `System.Description`, `Repro Steps`, and `Acceptance Criteria`; `html` (default) is unchanged ([#34](https://github.com/eXoz00rd/AzureMCP/pull/34))

### Changed
- `update_work_item`, `create_work_item`, `link_work_item`, and `add_work_item_attachment` now return a minimal `WorkItemWriteResult` (id, rev, changed field names, success) instead of the full `WorkItem`, cutting the tokens a caller pays for a write it only needs to confirm ([#31](https://github.com/eXoz00rd/AzureMCP/pull/31))
- Truncation is now reported everywhere a list is capped. `list_branches`, `list_commits`, `list_pull_requests`, `list_my_pull_requests`, `list_releases`, `list_release_approvals`, `list_builds`, and `get_work_item_revisions` previously forwarded a caller `top` straight to Azure DevOps Server and returned the raw list — a result that exactly filled the cap was indistinguishable from one that captured everything. They now request one extra item and trim, matching `list_repository_items` ([#36](https://github.com/eXoz00rd/AzureMCP/pull/36))

### Fixed
- **`get_work_items` could surface an opaque Azure DevOps server error** for an id batch over roughly 200 items; it's now rejected client-side with a clear message. `GetProjectsAsync`'s pagination had no upper bound — a malformed or looping continuation token from the server could page forever; it's now capped at 100 pages ([#39](https://github.com/eXoz00rd/AzureMCP/pull/39))
- **Auth failures against on-premises servers collapsed every 401/203 response into one static "authentication failed" message**, hiding the real status code, request URI, and server response. The error now surfaces the request URI, HTTP status, and the `WWW-Authenticate` schemes the server offered (e.g. `Negotiate`/`NTLM` vs `Basic`), without ever including the outgoing `Authorization` header or the PAT ([#40](https://github.com/eXoz00rd/AzureMCP/pull/40))

62 tools, 391 tests.

**Full changelog:** https://github.com/eXoz00rd/AzureMCP/compare/v0.1.1...v0.1.2

## [0.1.1] — 2026-09-10

Five fixes and one API correction since v0.1.0, plus release/CI hygiene.

### Changed
- **Breaking:** `add_work_item_comment` now posts through `_apis/wit/workItems/{id}/comments` instead of writing to `System.History`, and returns a `WorkItemComment` (real id, version) instead of the whole `WorkItem`. It also accepts an optional `project` parameter, matching `list_work_item_comments`.

### Added
- **`get_work_item_comment`** reads a single work item comment by id.

### Fixed
- `vote_on_pull_request` silently cleared the reviewer's required flag — the PUT only sent `{ vote }`, and that endpoint replaces the whole reviewer resource. The client now looks up the reviewer's current `isRequired` before voting and includes it in the request.
- Unsafe HTTP methods (POST/PATCH/PUT/DELETE) were retried automatically on transient Azure DevOps failures, which could create duplicate work items, comments, releases, attachments, or queued builds when a request succeeded but its response was lost. Only GET still retries.
- Repository file content was fully buffered before `maxChars` could limit it. Large files now stream through a byte-level JSON decoder instead of being materialized in full first.
- Build logs stream instead of buffering the whole response.
- The NuGet package version is now the single source for the MCP manifest version, so the two can no longer disagree.

62 tools, 263 tests.

**Full changelog:** https://github.com/eXoz00rd/AzureMCP/compare/v0.1.0...v0.1.1

## [0.1.0] — 2026-08-12

First non-preview release. Nine previews landed 60 tools, toolset/read-only configuration, and on-premises-specific error handling; this one is a compatibility fix found through real on-premises use.

### Fixed
- `create_or_update_wiki_page` failed with `405 Method Not Allowed` against some on-premises Azure DevOps Server installations. The page-version lookup that runs before every write sent an HTTP `HEAD` request, and not every on-prem server accepts `HEAD` on that route. The lookup now uses `GET` instead, with `If-Match` semantics unchanged.

60 tools, 185 tests.

## [0.1.0-preview.9] — 2026-08-12

- **Added:** `link_pull_request_to_work_item` builds the `vstfs` artifact URL from the pull request itself; `link_work_item` accepts an explicit `artifactLinkName` and infers it from known `vstfs` URLs when omitted; Azure DevOps error responses now reach the caller with their real status and message.
- **Fixed:** every optional tool parameter was marked `required` in the generated JSON schema, because nullable parameters had no default value in the C# signature.

60 tools, 185 tests.

## [0.1.0-preview.8] — 2026-08-11

- **Added:** `ADOS_TOOLSETS` to expose only the areas a team uses; `ADOS_READ_ONLY=true` to remove every write tool; MCP `instructions` sent on connect; output schemas on all tools.
- **Fixed:** `query_work_items` did not work in preview.7 — its attributes decorated a private helper method, so the tool never ran the WIQL query. Errors now carry the Azure DevOps message instead of the full error envelope.

169 tests.

## [0.1.0-preview.7] — 2026-08-11

- **Added:** wiki writes (`create_or_update_wiki_page`); work item `list_work_item_comments`, `get_work_item_revisions`, `link_work_item`, `add_work_item_attachment`; pull request `update_pull_request`, `add_pull_request_reviewer`, `remove_pull_request_reviewer`; release `list_release_approvals`, `update_release_approval`, `deploy_release_environment`; `get_project`.

59 tools, 150 tests.

## [0.1.0-preview.6] — 2026-08-11

- **Added:** `list_my_pull_requests`, `get_pull_request_policies`, `list_pull_request_work_items`, `reply_to_pull_request_thread`, `set_pull_request_thread_status`.
- **Changed:** pull requests now return reviewer votes, `mergeStatus`, `isDraft`, creation/closed dates, and the repository reference.

47 tools, 119 tests.

## [0.1.0-preview.5] — 2026-08-10

- **Added:** prompts (`review_pull_request`, `diagnose_build_failure`, `sprint_status`); per-area API version overrides; TLS handshake diagnostics.
- **Changed:** responses are bounded so one call cannot exhaust an agent's context (build logs, file contents, and list tools all cap and report truncation); work item tools accept a field list.

Tool count unchanged at 42; test count grew from 65 to 108.

## [0.1.0-preview.4] — 2026-08-10

- **Added:** `vote_on_pull_request`, `update_pull_request_status`; wiki reads (`list_wikis`, `list_wiki_pages`, `get_wiki_page`); classic release pipelines (`list_release_definitions`, `list_releases`, `get_release`, `create_release`); build diagnostics (`get_build_timeline`, `get_build_log`, `list_build_artifacts`); git navigation (`list_commits`, `get_commit`, `list_repository_items`, `diff_branches`); queries and metadata (`list_queries`, `run_saved_query`, `list_work_item_types`, `list_work_item_states`, `list_iterations`, `list_areas`, `get_work_items`).
- **Changed:** all 42 tools carry MCP annotations; HTTP calls run through a resilience pipeline with retries, timeouts, and a circuit breaker.

42 tools.

## [0.1.0-preview.3] — 2026-08-10

- **Added:** `get_pull_request_changes`, `list_pull_request_threads`, `add_pull_request_comment`.
- **Fixed:** server logs no longer flood MCP clients — the minimum stderr log level is now `Warning`.

## [0.1.0-preview.2] — 2026-08-10

Documentation and licensing release; no tool changes. Added the MIT license and expanded the GitHub Copilot guide.

## [0.1.0-preview.1] — 2026-08-10

First public preview.

- .NET 10 stdio MCP server on the official ModelContextProtocol SDK, shipped as a `dnx`-runnable NuGet package
- PAT authentication read only from environment variables, validated at startup; TFS sign-in-page responses (HTTP 203) reported as authentication failures instead of parse errors
- 16 tools across projects, work items, repositories, pull requests, and builds
- CI on GitHub Actions plus a tag-triggered release workflow publishing through NuGet Trusted Publishing

[Unreleased]: https://github.com/eXoz00rd/AzureMCP/compare/v0.2.1...HEAD
[0.2.1]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.2.1
[0.2.0]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.2.0
[0.1.3]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.3
[0.1.2]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.2
[0.1.1]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.1
[0.1.0]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0
[0.1.0-preview.9]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.9
[0.1.0-preview.8]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.8
[0.1.0-preview.7]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.7
[0.1.0-preview.6]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.6
[0.1.0-preview.5]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.5
[0.1.0-preview.4]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.4
[0.1.0-preview.3]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/eXoz00rd/AzureMCP/releases/tag/v0.1.0-preview.1
