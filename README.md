# Jira MCP Server

A small [Model Context Protocol](https://modelcontextprotocol.io) server, written in .NET 10, that lets AI assistants (GitHub Copilot, JetBrains AI Assistant / Junie, Claude, etc.) read and create work items in a Jira Cloud project.

## This is a local MCP server

This server runs **on your own machine**. It is not a hosted service and has no URL to connect to.

- It uses the **stdio** transport: your MCP client (Rider, VS Code, Claude Desktop, …) starts the executable as a child process and talks to it over standard input/output.
- The client launches it when needed and stops it when done; you don't run it yourself.
- Your Jira credentials stay in a local `.env` file and are only sent to your own Jira site.

## Tools

| Tool | What it does |
|---|---|
| `get_all_work_items` | Lists every work item in the configured project with its key, summary (title), status, issue type and assignee. Pages through all results. |
| `get_all_work_item_statuses` | Lists the statuses available in the project (e.g. To Do, In Progress, Done) with their IDs. |
| `get_all_work_item_types` | Lists the issue types available in the project (e.g. Task, Bug, Story) with their IDs. |
| `create_work_item` | Creates a work item with a title, description and issue type, then optionally moves it to a given status (defaults to Backlog when available). |

All tools work against the single project set in `Jira__DefaultProjectKey`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A Jira Cloud site and an [Atlassian API token](https://id.atlassian.com/manage-profile/security/api-tokens)

## Setup

1. Clone the repo and create your `.env` from the example:

   ```bash
   git clone https://github.com/EceE91/JiraMCPServer.git
   cd JiraMCPServer
   cp .env.example .env
   ```

2. Fill in `.env`:

   ```env
   Jira__BaseUrl=https://yourcompany.atlassian.net
   Jira__Email=you@example.com
   Jira__ApiToken=your-api-token-here
   Jira__DefaultProjectKey=ABC
   ```

   `.env` is git-ignored. The server looks for it in the executable's folder and its parent folders, so leaving it in the repo root works. Values can also be passed as environment variables by the MCP client, which take precedence over `.env`.

3. Build:

   ```bash
   dotnet build
   ```

   The executable ends up at `JiraMCPServer/bin/Debug/net10.0/JiraMCPServer.exe` (or `JiraMCPServer.dll` on any OS).

## Connecting a client

Every client needs the same thing: a command that starts the server. Replace the paths below with your own.

### GitHub Copilot in Rider / other JetBrains IDEs

Copilot Chat → **Agent** mode → tools icon → **Add More Tools…**, or edit
`%LOCALAPPDATA%\github-copilot\intellij\mcp.json` directly:

```json
{
  "servers": {
    "jira": {
      "type": "stdio",
      "command": "C:\\path\\to\\JiraMCPServer\\JiraMCPServer\\bin\\Debug\\net10.0\\JiraMCPServer.exe",
      "args": []
    }
  }
}
```

### JetBrains AI Assistant / Junie

**Settings → Tools → AI Assistant → Model Context Protocol (MCP)** (or **Tools → Junie → MCP Settings**) → **Add** → **As JSON**:

```json
{
  "mcpServers": {
    "jira": {
      "command": "C:\\path\\to\\JiraMCPServer\\JiraMCPServer\\bin\\Debug\\net10.0\\JiraMCPServer.exe",
      "args": []
    }
  }
}
```

### Claude Code

```bash
claude mcp add jira -- dotnet /path/to/JiraMCPServer/JiraMCPServer/bin/Debug/net10.0/JiraMCPServer.dll
```

### Testing without an IDE

[MCP Inspector](https://github.com/modelcontextprotocol/inspector) gives you a UI to call each tool directly:

```bash
npx @modelcontextprotocol/inspector dotnet JiraMCPServer/bin/Debug/net10.0/JiraMCPServer.dll
```

## Troubleshooting

- **"You must install or update .NET to run this application"**: the client can't find the .NET 10 runtime, usually because it was installed per-user (e.g. by Rider into `%USERPROFILE%\.dotnet`). Either install .NET 10 system-wide, add `"env": { "DOTNET_ROOT": "C:\\Users\\<you>\\.dotnet" }` to the server entry, or use that folder's `dotnet.exe` as the `command` with the `.dll` path in `args`.
- **"Missing configuration: Jira__…"** (in the client's MCP log): `.env` wasn't found or is incomplete.
- **401 / 403 from Jira**: wrong email or API token.
- **404 from Jira**: wrong `Jira__BaseUrl` or `Jira__DefaultProjectKey`.
- **Build fails with "file is locked"**: the MCP client is still running the old server. Stop it in the client, rebuild, then restart it.
- **Don't use `dotnet run` as the command**: build output goes to stdout and corrupts the MCP protocol stream. Point the client at the built `.exe` / `.dll` instead.

## Project layout

```
JiraMCPServer/
├── Program.cs          # Host setup, .env loading, stdio MCP transport
├── JiraTools.cs        # MCP tool definitions
├── JiraApiService.cs   # Jira Cloud REST API v3 calls
├── JiraOptions.cs      # "Jira" configuration section
└── appsettings.json
```
