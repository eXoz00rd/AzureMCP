# AzureMCP

MCP (Model Context Protocol) server for **Azure DevOps Server (on-premises)**, built with **.NET 10 / C#** on top of the official [ModelContextProtocol C# SDK](https://www.nuget.org/packages/ModelContextProtocol) and distributed as a NuGet package.

> **Status:** early development — the tool surface and package layout are being defined.

## Why

Most existing MCP integrations for Azure DevOps target Azure DevOps Services (cloud). This project focuses on **on-premises Azure DevOps Server** installations (2019 / 2020 / 2022+): collection URLs, REST API versions supported by on-prem servers, and Personal Access Token (PAT) authentication.

## Goals

- First-class support for on-premises Azure DevOps Server REST APIs
- Authentication with a **PAT only** — no credentials in code, no secrets in the repository
- Built on the official ModelContextProtocol NuGet SDK with .NET 10
- Shipped as a **NuGet package** runnable via `dnx`
- Secure by default: PAT is read from environment variables at runtime and never logged

## Planned tools

| Area | Capabilities |
|---|---|
| Work items | WIQL queries, get / create / update, comments |
| Repositories | list repositories and branches, commits, file content |
| Pull requests | list, details, create, comment, vote |
| Builds | definitions, queue builds, results, logs |
| Projects & teams | list projects, teams, members |

## Requirements

- .NET 10 SDK
- A reachable Azure DevOps Server (on-premises) collection URL
- A PAT created in that collection, with the minimal scopes required for the tools you use

## Usage (target design)

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

### Configuration

| Variable | Required | Description |
|---|---|---|
| `ADOS_COLLECTION_URL` | yes | Full collection URL, e.g. `https://devops.example.local/DefaultCollection` |
| `ADOS_PAT` | yes | Personal Access Token used for all REST calls |
| `ADOS_DEFAULT_PROJECT` | no | Default project used when a tool call does not specify one |
| `ADOS_API_VERSION` | no | Override the REST API version if your server requires it |

## Security

- The PAT is read **only** from environment variables — never from command-line arguments, committed configuration files, or source code
- No secrets are ever stored in this repository
- Use a PAT with the minimal scopes needed and a short expiration date
- Use HTTPS for the collection URL

## Building from source

```bash
git clone https://github.com/eXoz00rd/AzureMCP.git
cd AzureMCP
dotnet build
dotnet test
```

## License

TBD
