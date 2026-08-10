# AzureMCP

MCP (Model Context Protocol) server for **Azure DevOps Server (on-premises)**, built with **.NET 10 / C#** on top of the official [ModelContextProtocol C# SDK](https://www.nuget.org/packages/ModelContextProtocol) and distributed as a NuGet package.

> **Status:** preview — the core tool set is implemented; the package is not yet published to NuGet.org.

## Why

Most existing MCP integrations for Azure DevOps target Azure DevOps Services (cloud). This project focuses on **on-premises Azure DevOps Server** installations (2019 / 2020 / 2022+): collection URLs, REST API versions supported by on-prem servers, and Personal Access Token (PAT) authentication — including classic TFS behaviors such as sign-in page responses on failed authentication.

## Tools

| Area | Tool | Description |
|---|---|---|
| Server | `server_info` | Returns the configured connection details; never exposes the PAT |
| Projects | `list_projects` | Lists all projects in the collection, paging through continuation tokens |
| Work items | `query_work_items` | Runs a WIQL query scoped to a project or the whole collection |
| Work items | `get_work_item` | Gets a single work item with all of its fields |
| Work items | `create_work_item` | Creates a work item of a given type with title and optional fields |
| Work items | `update_work_item` | Updates work item fields via JSON Patch |
| Work items | `add_work_item_comment` | Adds a discussion comment (`System.History`) |
| Repositories | `list_repositories` | Lists Git repositories in a project or the whole collection |
| Repositories | `list_branches` | Lists the branches of a repository |
| Repositories | `get_file_content` | Gets the content of a text file, optionally from a specific branch |
| Pull requests | `list_pull_requests` | Lists pull requests filtered by status (active by default) |
| Pull requests | `get_pull_request` | Gets the details of a pull request |
| Pull requests | `create_pull_request` | Creates a pull request between two branches |
| Builds | `list_build_definitions` | Lists the build definitions of a project |
| Builds | `list_builds` | Lists recent builds, optionally filtered by definition |
| Builds | `queue_build` | Queues a build for a definition, optionally from a specific branch |

Tools that operate inside a project fall back to `ADOS_DEFAULT_PROJECT` when no project is given.

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

**Option A — from NuGet (once published):** nothing to download manually; the MCP client fetches and runs the package via `dnx AzureDevOpsServer.Mcp --yes` on first start.

**Option B — from source (works today):**

```bash
git clone https://github.com/eXoz00rd/AzureMCP.git
cd AzureMCP
dotnet build AzureDevOpsServer.Mcp.slnx
dotnet run --project tests/AzureDevOpsServer.Mcp.Tests
```

The repository is currently private — authenticate first (e.g. `gh auth login`).

### 4. Configure your MCP client

Use one of the configurations from [Usage](#usage) below — VS Code Copilot (`.vscode/mcp.json`), Visual Studio (`.mcp.json`), or any other MCP-capable client. For Claude Code:

```bash
claude mcp add azure-devops-server -e ADOS_COLLECTION_URL=https://devops.example.local/DefaultCollection -e ADOS_PAT=YOUR_PAT -- dnx AzureDevOpsServer.Mcp --yes
```

### 5. Verify

Ask the agent to call `server_info` or to "list projects on our DevOps server". The server refuses to start when `ADOS_COLLECTION_URL` or `ADOS_PAT` is missing and logs the exact reason to stderr, so a misconfigured client fails fast with a clear message.

## Usage

Once published to NuGet, the server will run directly from the package via `dnx`. Example MCP client configuration (Claude Code, VS Code, etc.):

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

   Until the package is published to NuGet.org, run it from source instead:

   ```json
   "command": "dotnet",
   "args": ["run", "--project", "D:/Projects/AzureMCP/src/AzureDevOpsServer.Mcp"]
   ```

2. Open Copilot Chat, switch to **Agent** mode, and start the server when prompted. VS Code asks for the PAT on first start and stores it securely — the token never lands in the config file.
3. Click the tools icon in the chat input to confirm the `azure-devops-server` tools are enabled, then ask Copilot for example to "list projects on our DevOps server".

### GitHub Copilot in Visual Studio

Visual Studio 2022 (17.14+) uses the same configuration format. Put the JSON above in a file named `.mcp.json` next to your solution (or `%USERPROFILE%\.mcp.json` to make it global), restart Visual Studio, and enable the server's tools in the Copilot Chat tool picker while in Agent mode.

### Configuration

| Variable | Required | Description |
|---|---|---|
| `ADOS_COLLECTION_URL` | yes | Full collection URL, e.g. `https://devops.example.local/DefaultCollection` |
| `ADOS_PAT` | yes | Personal Access Token used for all REST calls |
| `ADOS_DEFAULT_PROJECT` | no | Default project used when a tool call does not specify one |
| `ADOS_API_VERSION` | no | Override the REST API version (defaults to `7.0`) |

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
```

## Publishing a release

Releases are published to NuGet.org by the [release workflow](.github/workflows/release.yml):

1. Create an API key on [nuget.org](https://www.nuget.org/account/apikeys) and save it as the `NUGET_API_KEY` repository secret (**Settings → Secrets and variables → Actions**)
2. Tag the commit and push the tag:

   ```bash
   git tag v0.1.0-preview.2
   git push origin v0.1.0-preview.2
   ```

3. The workflow builds, tests, packs with the version taken from the tag (also synced into `.mcp/server.json`), and pushes the package to NuGet.org

## Roadmap

- Pull request threads, comments, and votes
- Build logs and timeline details
- Wiki page access
- Publishing to NuGet.org

## License

TBD
