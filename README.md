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

## Roadmap

- Pull request threads, comments, and votes
- Build logs and timeline details
- Wiki page access
- Publishing to NuGet.org

## License

TBD
