# AzureMCP

[![CI](https://github.com/eXoz00rd/AzureMCP/actions/workflows/ci.yml/badge.svg)](https://github.com/eXoz00rd/AzureMCP/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/AzureDevOpsServer.Mcp?logo=nuget)](https://www.nuget.org/packages/AzureDevOpsServer.Mcp)
[![Downloads](https://img.shields.io/nuget/dt/AzureDevOpsServer.Mcp)](https://www.nuget.org/packages/AzureDevOpsServer.Mcp)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

MCP (Model Context Protocol) server for **Azure DevOps Server (on-premises)**, built with **.NET 10 / C#** on top of the official [ModelContextProtocol C# SDK](https://www.nuget.org/packages/ModelContextProtocol) and distributed as a NuGet package.

> **Status:** preview — available on NuGet.org as [AzureDevOpsServer.Mcp](https://www.nuget.org/packages/AzureDevOpsServer.Mcp).

## Why

Most existing MCP integrations for Azure DevOps target Azure DevOps Services (cloud). This project focuses on **on-premises Azure DevOps Server** installations (2019 / 2020 / 2022+): collection URLs, REST API versions supported by on-prem servers, and Personal Access Token (PAT) authentication — including classic TFS behaviors such as sign-in page responses on failed authentication.

## Tools

62 tools across 9 areas:

| Area | Tools |
|---|---|
| Server | `server_info` |
| Projects | `list_projects`, `get_project` |
| Work items | `query_work_items`, `get_work_item`, `get_work_items`, `get_work_item_revisions`, `create_work_item`, `update_work_item`, `add_work_item_comment`, `get_work_item_comment`, `list_work_item_comments`, `link_work_item`, `add_work_item_attachment` |
| Queries & metadata | `list_queries`, `run_saved_query`, `list_work_item_types`, `list_work_item_states`, `list_iterations`, `list_areas` |
| Repositories | `list_repositories`, `list_branches`, `get_file_content`, `list_commits`, `get_commit`, `list_repository_items`, `diff_branches` |
| Pull requests | `list_pull_requests`, `list_my_pull_requests`, `get_pull_request`, `create_pull_request`, `get_pull_request_changes`, `get_pull_request_policies`, `list_pull_request_work_items`, `link_pull_request_to_work_item`, `list_pull_request_threads`, `add_pull_request_comment`, `reply_to_pull_request_thread`, `update_pull_request_comment`, `set_pull_request_thread_status`, `vote_on_pull_request`, `update_pull_request`, `update_pull_request_status`, `add_pull_request_reviewer`, `remove_pull_request_reviewer` |
| Builds | `list_build_definitions`, `list_builds`, `queue_build`, `get_build_timeline`, `get_build_log`, `list_build_artifacts` |
| Releases | `list_release_definitions`, `list_releases`, `get_release`, `create_release`, `list_release_approvals`, `update_release_approval`, `deploy_release_environment` |
| Wiki | `list_wikis`, `list_wiki_pages`, `get_wiki_page`, `create_or_update_wiki_page` |

`get_pull_request` and both listing tools return reviewer votes, merge status, and draft state, so questions like "who approved this and can it merge?" are answered without extra calls.

Tools that operate inside a project fall back to `ADOS_DEFAULT_PROJECT` when no project is given. Every tool carries MCP annotations (`readOnlyHint` / `destructiveHint`), so clients can require confirmation only where it matters, and all HTTP calls go through a standard resilience pipeline with retries and timeouts.

### Trimming the tool list

62 tools is a lot for one client to carry, and clients cap how many tools they send per request. Two variables keep the surface small:

- **`ADOS_TOOLSETS=workitems,pullrequests`** exposes only the areas a team actually uses — the example above drops the list from 62 tools to 29.
- **`ADOS_READ_ONLY=true`** removes every write tool, leaving 41 read-only tools. Useful when an agent should be able to look at Azure DevOps but not change it, without relying on PAT scopes alone.

Both can be combined, and an unknown toolset name fails at startup with the list of valid names instead of silently exposing the wrong tools.

The server also sends MCP `instructions` on connect: the default project, whether it runs read-only, and how to use the tools well (field lists for work items, timeline before logs, raising limits instead of assuming something is missing). Tools publish output schemas, so clients receive structured results rather than opaque JSON, and failures carry the Azure DevOps error message instead of the raw error envelope.

Responses are bounded so a single call cannot flood an agent's context: build logs and file contents are capped (8 000 characters by default) and report their total length and whether they were truncated, binary files are detected instead of dumped, list tools take an explicit limit (branch, pull request, release approval, work item comment, and revision lists return 25 items by default, build, release, and commit lists 20), and work item tools accept a field list instead of returning every field.

## Prompts

Ready-made workflows that chain the tools:

| Prompt | What it does |
|---|---|
| `review_pull_request` | Reads the pull request, its changed files, and existing threads, then reports findings by severity |
| `diagnose_build_failure` | Walks the build timeline to the failing task and reads the relevant part of its log |
| `sprint_status` | Finds the current iteration and summarizes states, blockers, and risks |

## Requirements

- .NET 10 SDK, which includes the ASP.NET Core runtime the package needs in every mode — or the [container image](#docker), which needs neither
- A reachable Azure DevOps Server (on-premises) collection URL
- A PAT created in that collection, with the minimal scopes required for the tools you use

## Setting up from scratch on a new machine

### 1. Install prerequisites

- **.NET 10 SDK** — `winget install Microsoft.DotNet.SDK.10` on Windows, or download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0); verify with `dotnet --list-sdks`
- **Git** — only needed while running from source

### 2. Create a PAT on your Azure DevOps Server

1. Open your collection in a browser and sign in
2. Click your avatar → **Security** → **Personal access tokens** → **New Token**
3. Pick a short expiration and only the scopes you need:
   - **Work Items — Read & write** (queries, details, create/update/comment)
   - **Code — Read & write** (repositories, file content, pull requests; Read is enough without `create_pull_request`)
   - **Build — Read & execute** (definitions, builds; Read is enough without `queue_build`)
4. Copy the token immediately — it is shown only once

### 3. Get the server

**Option A — from NuGet (recommended):** nothing to download manually; the MCP client fetches and runs the [published package](https://www.nuget.org/packages/AzureDevOpsServer.Mcp) via `dnx AzureDevOpsServer.Mcp --yes` on first start.

**Option B — from source (for development):**

```bash
git clone https://github.com/eXoz00rd/AzureMCP.git
cd AzureMCP
dotnet build AzureDevOpsServer.Mcp.slnx
dotnet run --project tests/AzureDevOpsServer.Mcp.Tests
```

### 4. Configure your MCP client

Use one of the configurations from [Usage](#usage) below — VS Code Copilot (`.vscode/mcp.json`), Visual Studio (`.mcp.json`), or any other MCP-capable client. For Claude Code:

```bash
claude mcp add azure-devops-server -e ADOS_COLLECTION_URL=https://devops.example.local/DefaultCollection -e ADOS_PAT=YOUR_PAT -- dnx AzureDevOpsServer.Mcp --yes
```

### 5. Verify

Ask the agent to call `server_info` or to "list projects on our DevOps server". The server refuses to start when `ADOS_COLLECTION_URL` or `ADOS_PAT` is missing and logs the exact reason to stderr, so a misconfigured client fails fast with a clear message.

## Usage

The server runs directly from the published NuGet package via `dnx`. Example MCP client configuration (Claude Code, VS Code, etc.):

```json
{
  "mcpServers": {
    "azure-devops-server": {
      "command": "dnx",
      "args": ["AzureDevOpsServer.Mcp", "--yes"],
      "env": {
        "ADOS_COLLECTION_URL": "https://devops.example.local/DefaultCollection",
        "ADOS_PAT": "${env:ADOS_PAT}"
      }
    }
  }
}
```

### Running from source

```json
{
  "mcpServers": {
    "azure-devops-server": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/Projects/AzureMCP/src/AzureDevOpsServer.Mcp"],
      "env": {
        "ADOS_COLLECTION_URL": "https://devops.example.local/DefaultCollection",
        "ADOS_PAT": "${env:ADOS_PAT}"
      }
    }
  }
}
```

### GitHub Copilot in VS Code

1. Create `.vscode/mcp.json` in your workspace (or run the `MCP: Add Server` command):

   ```json
   {
     "inputs": [
       {
         "id": "ados-pat",
         "type": "promptString",
         "description": "Azure DevOps Server Personal Access Token",
         "password": true
       }
     ],
     "servers": {
       "azure-devops-server": {
         "type": "stdio",
         "command": "dnx",
         "args": ["AzureDevOpsServer.Mcp", "--yes"],
         "env": {
           "ADOS_COLLECTION_URL": "https://devops.example.local/DefaultCollection",
           "ADOS_PAT": "${input:ados-pat}",
           "ADOS_DEFAULT_PROJECT": "MyProject"
         }
       }
     }
   }
   ```

   To run from source instead (e.g. for development), replace the command:

   ```json
   "command": "dotnet",
   "args": ["run", "--project", "D:/Projects/AzureMCP/src/AzureDevOpsServer.Mcp"]
   ```

2. Open Copilot Chat, switch to **Agent** mode, and start the server when prompted. VS Code asks for the PAT on first start and stores it securely — the token never lands in the config file.
3. Click the tools icon in the chat input to confirm the `azure-devops-server` tools are enabled, then ask Copilot for example to "list projects on our DevOps server".

### GitHub Copilot in Visual Studio

Visual Studio 2022 (17.14+) uses the same configuration format. Put the JSON above in a file named `.mcp.json` next to your solution (or `%USERPROFILE%\.mcp.json` to make it global), restart Visual Studio, and enable the server's tools in the Copilot Chat tool picker while in Agent mode.

### Testing the integration

Try these prompts in Copilot agent mode and watch which tool gets called:

- "Show the Azure DevOps server info" → `server_info`, returns the collection URL and defaults without the PAT
- "List projects on our DevOps server" → `list_projects`
- "Find active bugs in project X" → `query_work_items` with a WIQL query
- "Show file /README.md from repository Y" → `get_file_content`
- "Queue a build for definition 12" → `queue_build` (Copilot asks for confirmation before write operations)

### Troubleshooting

- **Server does not start** — open the MCP log (VS Code: **Output** panel → the `azure-devops-server` channel). A missing `ADOS_COLLECTION_URL` or `ADOS_PAT` is reported explicitly at startup
- **"Authentication against Azure DevOps Server failed"** — the PAT is invalid, expired, or missing scopes; TFS sign-in page responses (HTTP 203) are detected and reported as this error as well
- **TLS / certificate errors** — on-premises servers usually present a certificate from an internal certificate authority. The server reports this explicitly instead of failing with an opaque SSL error; fix it by importing the authority certificate into the machine trust store (Windows: `Manage computer certificates` → **Trusted Root Certification Authorities** → Import). Certificate validation is never disabled, because the PAT travels on that connection
- **Older servers** — for Azure DevOps Server 2019 / 2020 set `ADOS_API_VERSION` to `5.0` / `6.0`
- **`dnx` not found** — the .NET 10 SDK is required; verify with `dotnet --list-sdks`

### Configuration

| Variable | Required | Description |
|---|---|---|
| `ADOS_COLLECTION_URL` | yes | Full collection URL, e.g. `https://devops.example.local/DefaultCollection` |
| `ADOS_PAT` | stdio only | Personal Access Token used for all REST calls over stdio. Over HTTP each request carries its caller's own PAT instead, and setting this variable stops the server from starting |
| `ADOS_DEFAULT_PROJECT` | no | Default project used when a tool call does not specify one |
| `ADOS_API_VERSION` | no | Override the REST API version for every area (defaults to `7.0`) |
| `ADOS_API_VERSION_WIT` | no | REST API version for work item and query calls |
| `ADOS_API_VERSION_GIT` | no | REST API version for repository and pull request calls |
| `ADOS_API_VERSION_BUILD` | no | REST API version for build calls |
| `ADOS_API_VERSION_RELEASE` | no | REST API version for release calls |
| `ADOS_API_VERSION_WIKI` | no | REST API version for wiki calls |
| `ADOS_API_VERSION_WIT_COMMENTS` | no | REST API version for the work item comments API (defaults to `7.0-preview.3`) |
| `ADOS_TOOLSETS` | no | Comma-separated toolsets to expose: `projects`, `workitems`, `queries`, `repositories`, `pullrequests`, `builds`, `releases`, `wiki`. All are enabled by default; `server_info` is always available |
| `ADOS_READ_ONLY` | no | Set to `true` to expose only read-only tools; every create, update, and delete tool disappears from the tool list |
| `ADOS_LOG_LEVEL` | no | Minimum level of logs (defaults to `Warning`; use `Information` or `Debug` for diagnostics). Over stdio they go to stderr, over HTTP to the console |

The variables for serving over HTTP are listed in [HTTP configuration](#http-configuration).

## Running over HTTP

For shared deployments — Open WebUI, containers, Kubernetes — the server can serve MCP over [Streamable HTTP](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http) instead of stdio. It then holds no PAT of its own: every request carries the caller's own PAT, so Azure DevOps attributes every comment, vote, and queued build to the person who asked.

```
Open WebUI ── Bearer token + X-Azure-DevOps-Pat ──▶ AzureMCP (HTTP) ── caller's PAT ──▶ Azure DevOps Server
```

- **Transport** — `ADOS_TRANSPORT=http` serves a stateless Streamable HTTP endpoint. stdio stays the default, so Copilot, Claude Code, and every existing client are unaffected
- **Access** — every request to the MCP endpoint must present `Authorization: Bearer <ADOS_HTTP_TOKEN>`, and one with a browser `Origin` outside `ADOS_HTTP_ALLOWED_ORIGINS` gets `403`. The exceptions: `/healthz` and the Open WebUI plugin at `/openwebui/azure_devops.py` need no token, `ADOS_HTTP_ALLOW_ANONYMOUS` lifts the token for loopback development, and a request that routing rejects before it chooses an endpoint — a wrong method or content type — gets `405` or `415` without reaching MCP
- **Identity** — every request carries `X-Azure-DevOps-Pat: <the caller's PAT>`. Tools that never call Azure DevOps, such as `server_info`, work without it
- **Health** — `GET /healthz` answers `200 Healthy` without a token, for liveness and readiness probes
- **Scaling** — the endpoint keeps no sessions, so any number of replicas can run behind one service without affinity

### HTTP configuration

| Variable | Default | Description |
|---|---|---|
| `ADOS_TRANSPORT` | `stdio` | `stdio` or `http` |
| `ADOS_HTTP_URL` | `http://127.0.0.1:8080` | Listen address. Loopback by default, so exposing the endpoint is always a deliberate choice; the container image sets `http://+:8080` |
| `ADOS_HTTP_PATH` | `/mcp` | Endpoint path. Must start with `/` and cannot be `/healthz` |
| `ADOS_HTTP_TOKEN` | — | Bearer token every request must present. Required over HTTP, except for loopback development with `ADOS_HTTP_ALLOW_ANONYMOUS` |
| `ADOS_HTTP_ALLOW_ANONYMOUS` | `false` | Serve without a token for local development. Refused unless every listen address is loopback |
| `ADOS_HTTP_ALLOWED_ORIGINS` | — | Comma-separated browser origins allowed to call the endpoint. Server-side callers such as Open WebUI send no `Origin` and are unaffected |
| `ADOS_HTTP_SERVE_PLUGIN` | `true` | Serves the Open WebUI plugin for this server's tools at `/openwebui/azure_devops.py` without a token. The plugin holds no secret; set `false` to stop serving it |

`ADOS_COLLECTION_URL` and the API version, toolset, read-only, and log level variables from [Configuration](#configuration) apply as well. The server refuses to start over HTTP without a token, with `ADOS_PAT` set, or with anonymous access on a routable address.

### Docker

Every release publishes `ghcr.io/exoz00rd/azuremcp` for `linux/amd64` and `linux/arm64`, built on the chiseled ASP.NET 10 runtime: no shell, no package manager, non-root.

Generate the bearer token into a file only you can read, so it never appears on a command line or in shell history:

```bash
(umask 077 && printf 'ADOS_HTTP_TOKEN=%s\n' "$(openssl rand -hex 32)" > azuremcp.env)
```

Give the server and Open WebUI a network of their own. Docker's default `bridge` network, where a plain `docker run` of Open WebUI lands, does not resolve container names, so Open WebUI could not find `azuremcp` there:

```bash
docker network create azuremcp
docker network connect azuremcp <open-webui-container>
```

Run the server on that network, and publish no port:

```bash
docker run --detach --name azuremcp --network azuremcp --read-only --tmpfs /tmp \
  --env ADOS_COLLECTION_URL=https://devops.example.local/DefaultCollection \
  --env-file azuremcp.env \
  ghcr.io/exoz00rd/azuremcp:<version>
```

Open WebUI then reaches it at `http://azuremcp:8080/mcp` with the token from `azuremcp.env`, and nothing outside that network can connect — which matters, because the PAT travels in every request. When Open WebUI runs under Docker Compose, add the server as a service of the same project instead: Compose networks resolve service names. To try the server from the host alone, add `--publish 127.0.0.1:8080:8080` and use `http://127.0.0.1:8080/mcp`.

When Azure DevOps Server uses a certificate from an internal certificate authority, mount the authority's PEM and add its directory to the trust store. The public roots stay trusted:

```bash
docker run --detach --name azuremcp --network azuremcp --read-only --tmpfs /tmp \
  --env ADOS_COLLECTION_URL=https://devops.example.local/DefaultCollection \
  --env-file azuremcp.env \
  --volume "$PWD/company-ca.crt:/etc/azuremcp/ca/ca.crt:ro" \
  --env SSL_CERT_DIR=/etc/ssl/certs:/etc/azuremcp/ca \
  ghcr.io/exoz00rd/azuremcp:<version>
```

### Kubernetes

Every release publishes a Helm chart to `oci://ghcr.io/exoz00rd/charts/azuremcp`:

```bash
kubectl create secret generic azuremcp-token --from-file=token=<(printf %s "$(openssl rand -hex 32)")
# Only when the Azure DevOps certificate comes from an internal certificate authority:
kubectl create configmap company-ca --from-file=ca.crt=./company-ca.crt
helm install azuremcp oci://ghcr.io/exoz00rd/charts/azuremcp --version <version> \
  --set azureDevOps.collectionUrl=https://devops.example.local/DefaultCollection \
  --set http.tokenSecret.name=azuremcp-token \
  --set caBundle.configMap=company-ca
```

The token is generated and handed over as a file, so it never appears in a command line; read it back for the Open WebUI plugin with `kubectl get secret azuremcp-token --output jsonpath='{.data.token}' | base64 --decode`. Leave out the ConfigMap and `caBundle.configMap` when the Azure DevOps certificate is publicly trusted. The chart runs two stateless replicas as non-root on a read-only root filesystem with no service account token, probes `/healthz`, exposes only a `ClusterIP` service, and adds a `NetworkPolicy` that admits pods labelled `app.kubernetes.io/name: open-webui` — adjust `networkPolicy.ingressFrom` to match your Open WebUI pods. Every setting is described in [`values.yaml`](charts/azuremcp/values.yaml).

Pin the chart and image version and raise it deliberately, for example with Renovate: with `latest`, nodes can run different versions and there is nothing to roll back to. Organizations usually mirror the image into an internal registry, verify it there, and deploy from the mirror.

### Open WebUI

Open WebUI reaches the server through a tool plugin that keeps each user's PAT in Open WebUI's per-user valves and sends it with every request. The server serves the plugin itself at `/openwebui/azure_devops.py`, generated from the tools it registers under its own `ADOS_TOOLSETS` and `ADOS_READ_ONLY`, so the plugin always matches the server it came from. Every release also attaches it as `azure_devops_openwebui.py`.

1. As an administrator, open **Workspace → Tools → Import From Link** and enter the server's plugin address, for example `http://azuremcp.<namespace>.svc.cluster.local:8080/openwebui/azure_devops.py`, then save. Open WebUI fetches the link from its own backend, so it works where GitHub is blocked, and the plugin's `server_url` is already set to the address used. Pasting the release asset works too
2. In the tool's **Valves**, check `server_url` — for example `http://azuremcp.<namespace>.svc.cluster.local:8080/mcp` — and set `server_token` to the value of `ADOS_HTTP_TOKEN`
3. Grant access to the users or groups that should see the tool
4. Each user opens **Integrations → Tools → Valves** in a chat and enters their own PAT

Open WebUI stores valve values in its database in plain text unless `ENABLE_VALVE_ENCRYPTION=true` is set together with a pinned `WEBUI_SECRET_KEY`; enable both before rolling out. The plugin is tested with the `mcp` 1.27.2 client that Open WebUI 0.11.3 ships. The model has to support tool calling; for one that does not, set **Function Calling** to **Legacy** in the model's advanced parameters.

After upgrading the server, import the plugin again so its tools match. To generate it without a running server, for example with fewer tools, use the generator:

```bash
dotnet run --project tools/OpenWebUiPluginGenerator -- --output azure_devops_openwebui.py --toolsets projects,workitems --read-only --server-url http://azuremcp.tools.svc.cluster.local:8080/mcp
```

### Verifying the image

Every image is signed with cosign keyless signing and carries a GitHub build provenance attestation, an SBOM, and BuildKit provenance, so you can check that it was built by this repository's release workflow before deploying it:

```bash
gh attestation verify oci://ghcr.io/exoz00rd/azuremcp:<version> --repo eXoz00rd/AzureMCP
```

```bash
cosign verify ghcr.io/exoz00rd/azuremcp:<version> --certificate-oidc-issuer https://token.actions.githubusercontent.com --certificate-identity-regexp '^https://github.com/eXoz00rd/AzureMCP/.github/workflows/release.yml@refs/tags/v'
```

### Troubleshooting HTTP

- **"The AzureMCP server rejected the plugin's token"** — the plugin's `server_token` differs from the AzureMCP server's `ADOS_HTTP_TOKEN`, often by a character pasted along with it. The users' PATs play no part
- **"This request carries no Azure DevOps PAT"** — the user has not entered a PAT in the plugin's valves, or a caller does not send `X-Azure-DevOps-Pat`
- **"could not be reached"** from the plugin — `server_url` is wrong, Open WebUI and the server share no Docker network other than the default `bridge`, or a network policy does not admit the Open WebUI pods
- **`415` or `405` instead of `401`** — the request was not a well-formed MCP request, so routing rejected it before choosing the endpoint; no MCP code ran
- **The server exits at startup** — the message names the setting: a missing token, `ADOS_PAT` set over HTTP, anonymous access on a routable address, a path that is not absolute or is `/healthz` or the plugin's `/openwebui/azure_devops.py`, or an address already in use

## Security

- Over stdio, the PAT is read **only** from environment variables — never from command-line arguments, committed configuration files, or source code
- Over HTTP, the server keeps no PAT: each request carries its caller's own in `X-Azure-DevOps-Pat`, and `ADOS_PAT` is refused so no request can fall back to a shared identity
- The HTTP endpoint requires a bearer token, compared in constant time. The access guard runs on the endpoint that routing has selected, so no spelling of the path reaches MCP without it, and anonymous access is allowed only on loopback
- Keep the HTTP endpoint on a private network — a Docker network or the chart's `NetworkPolicy` — because a PAT crosses it with every request
- Neither the PAT nor the bearer token is ever logged, at any log level
- Container images are signed and attested; [verify them](#verifying-the-image) before deploying
- No secrets are ever stored in this repository
- Failed authentication (including TFS sign-in page responses with status 203) surfaces a clear error instead of confusing parse failures
- Use a PAT with the minimal scopes needed and a short expiration date
- Use HTTPS for the collection URL

## Building from source

```bash
git clone https://github.com/eXoz00rd/AzureMCP.git
cd AzureMCP
dotnet build AzureDevOpsServer.Mcp.slnx
dotnet run --project tests/AzureDevOpsServer.Mcp.Tests
dotnet format --verify-no-changes
```

CI runs the same steps on every push and pull request, then collects coverage and packs the NuGet package.

## Publishing a release

Releases are published to NuGet.org by the [release workflow](.github/workflows/release.yml):

1. Publishing uses [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) — a policy on nuget.org tied to this repository and `release.yml`; no API key secret is stored. The workflow exchanges the GitHub OIDC token for a short-lived key via `NuGet/login`
2. Tag the commit and push the tag:

   ```bash
   git tag v0.1.0-preview.2
   git push origin v0.1.0-preview.2
   ```

3. The workflow builds, tests, packs with the version taken from the tag (also synced into `.mcp/server.json`), and pushes the package to NuGet.org. It then builds the multi-architecture container image, signs and attests it, pushes it and the Helm chart to GHCR, and attaches the Open WebUI plugin to the GitHub release

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release notes, or the [GitHub Releases](https://github.com/eXoz00rd/AzureMCP/releases) page.

## Roadmap

- Wiki search through the Search extension
- Work item attachments
- Code search

## Support the project

If this server saves you time, you can support its development here: [suppi.pl/exoz0rd](https://suppi.pl/exoz0rd). Entirely optional — the package stays free and MIT licensed either way.

## License

[MIT](LICENSE)
