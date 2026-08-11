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

59 tools across 9 areas:

| Area | Tools |
|---|---|
| Server | `server_info` |
| Projects | `list_projects`, `get_project` |
| Work items | `query_work_items`, `get_work_item`, `get_work_items`, `get_work_item_revisions`, `create_work_item`, `update_work_item`, `add_work_item_comment`, `list_work_item_comments`, `link_work_item`, `add_work_item_attachment` |
| Queries & metadata | `list_queries`, `run_saved_query`, `list_work_item_types`, `list_work_item_states`, `list_iterations`, `list_areas` |
| Repositories | `list_repositories`, `list_branches`, `get_file_content`, `list_commits`, `get_commit`, `list_repository_items`, `diff_branches` |
| Pull requests | `list_pull_requests`, `list_my_pull_requests`, `get_pull_request`, `create_pull_request`, `get_pull_request_changes`, `get_pull_request_policies`, `list_pull_request_work_items`, `list_pull_request_threads`, `add_pull_request_comment`, `reply_to_pull_request_thread`, `set_pull_request_thread_status`, `vote_on_pull_request`, `update_pull_request`, `update_pull_request_status`, `add_pull_request_reviewer`, `remove_pull_request_reviewer` |
| Builds | `list_build_definitions`, `list_builds`, `queue_build`, `get_build_timeline`, `get_build_log`, `list_build_artifacts` |
| Releases | `list_release_definitions`, `list_releases`, `get_release`, `create_release`, `list_release_approvals`, `update_release_approval`, `deploy_release_environment` |
| Wiki | `list_wikis`, `list_wiki_pages`, `get_wiki_page`, `create_or_update_wiki_page` |

`get_pull_request` and both listing tools return reviewer votes, merge status, and draft state, so questions like "who approved this and can it merge?" are answered without extra calls.

Tools that operate inside a project fall back to `ADOS_DEFAULT_PROJECT` when no project is given. Every tool carries MCP annotations (`readOnlyHint` / `destructiveHint`), so clients can require confirmation only where it matters, and all HTTP calls go through a standard resilience pipeline with retries and timeouts.

### Trimming the tool list

59 tools is a lot for one client to carry, and clients cap how many tools they send per request. Two variables keep the surface small:

- **`ADOS_TOOLSETS=workitems,pullrequests`** exposes only the areas a team actually uses — the example above drops the list from 59 tools to 27.
- **`ADOS_READ_ONLY=true`** removes every write tool, leaving 40 read-only tools. Useful when an agent should be able to look at Azure DevOps but not change it, without relying on PAT scopes alone.

Both can be combined, and an unknown toolset name fails at startup with the list of valid names instead of silently exposing the wrong tools.

The server also sends MCP `instructions` on connect: the default project, whether it runs read-only, and how to use the tools well (field lists for work items, timeline before logs, raising limits instead of assuming something is missing). Tools publish output schemas, so clients receive structured results rather than opaque JSON, and failures carry the Azure DevOps error message instead of the raw error envelope.

Responses are bounded so a single call cannot flood an agent's context: build logs and file contents are capped (30 000 characters by default) and report their total length and whether they were truncated, binary files are detected instead of dumped, list tools take an explicit limit, and work item tools accept a field list instead of returning every field.

## Prompts

Ready-made workflows that chain the tools:

| Prompt | What it does |
|---|---|
| `review_pull_request` | Reads the pull request, its changed files, and existing threads, then reports findings by severity |
| `diagnose_build_failure` | Walks the build timeline to the failing task and reads the relevant part of its log |
| `sprint_status` | Finds the current iteration and summarizes states, blockers, and risks |

## Requirements

- .NET 10 SDK
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
| `ADOS_PAT` | yes | Personal Access Token used for all REST calls |
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
| `ADOS_LOG_LEVEL` | no | Minimum level of logs written to stderr (defaults to `Warning`; use `Information` or `Debug` for diagnostics) |

## Security

- The PAT is read **only** from environment variables — never from command-line arguments, committed configuration files, or source code
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

3. The workflow builds, tests, packs with the version taken from the tag (also synced into `.mcp/server.json`), and pushes the package to NuGet.org

## Roadmap

- Wiki search through the Search extension
- Work item attachments
- Code search

## Support the project

If this server saves you time, you can support its development here: [suppi.pl/exoz0rd](https://suppi.pl/exoz0rd). Entirely optional — the package stays free and MIT licensed either way.

## License

[MIT](LICENSE)
