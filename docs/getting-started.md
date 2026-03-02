---
layout: default
title: Getting Started
description: Install and configure MCP Proxy
---

## Getting Started

This guide will help you install MCP Proxy and get it running with your first configuration.

<div class="toc">
<h4>On this page</h4>
<ul>
<li><a href="#installation">Installation</a></li>
<li><a href="#your-first-configuration">Your First Configuration</a></li>
<li><a href="#running-the-proxy">Running the Proxy</a></li>
<li><a href="#connecting-a-client">Connecting a Client</a></li>
<li><a href="#next-steps">Next Steps</a></li>
</ul>
</div>

## Installation

There are three ways to install and run MCP Proxy:

### Option 1: .NET Global Tool (Recommended)

If you have .NET 10 or later installed:

```bash
dotnet tool install -g McpProxy
```

After installation, the `mcpproxy` command will be available globally:

```bash
mcpproxy --help
```

### Option 2: Run with dnx (No Installation)

You can run MCP Proxy directly without installing it using `dnx`:

```bash
dnx McpProxy -- stdio ./mcp-proxy.json
```

This downloads and runs the tool on demand.

### Option 3: Build from Source

Clone the repository and build:

```bash
git clone https://github.com/MoaidHathot/mcp-proxy.git
cd mcp-proxy
dotnet build src/McpProxy
```

Run with:

```bash
dotnet run --project src/McpProxy -- stdio ./mcp-proxy.json
```

## Your First Configuration

Create a configuration file named `mcp-proxy.json`:

```json
{
  "mcp": {
    "filesystem": {
      "type": "stdio",
      "title": "Filesystem Server",
      "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "/path/to/workspace"]
    }
  }
}
```

This configuration tells MCP Proxy to:
1. Connect to a single backend MCP server called "filesystem"
2. Start it as a local STDIO process using `npx`
3. Give it access to your workspace directory

### Adding More Servers

You can add multiple servers:

```json
{
  "mcp": {
    "filesystem": {
      "type": "stdio",
      "title": "Filesystem Server",
      "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "/workspace"]
    },
    "context7": {
      "type": "sse",
      "title": "Context7 Documentation",
      "url": "https://mcp.context7.com/sse"
    },
    "github": {
      "type": "http",
      "title": "GitHub MCP",
      "url": "https://your-github-mcp-server.com",
      "headers": {
        "Authorization": "Bearer ${GITHUB_TOKEN}"
      }
    }
  }
}
```

### Environment Variables

MCP Proxy supports environment variable substitution in two formats:

- `"env:VAR_NAME"` - Use this for entire values
- `${VAR_NAME}` - Use this for embedding within strings

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "environment": {
        "API_KEY": "env:MY_API_KEY"
      },
      "headers": {
        "Authorization": "Bearer ${API_TOKEN}"
      }
    }
  }
}
```

## Running the Proxy

### STDIO Mode (for local clients)

Use STDIO mode when connecting to clients like Claude Desktop or OpenCode that spawn the proxy as a subprocess:

```bash
mcpproxy stdio ./mcp-proxy.json
```

### SSE Mode (for HTTP clients)

Use SSE mode to run the proxy as an HTTP server:

```bash
mcpproxy sse ./mcp-proxy.json
```

By default, the server runs on port 5000. You can change this:

```bash
mcpproxy sse ./mcp-proxy.json --urls "http://localhost:8080"
```

Or using an environment variable:

```bash
ASPNETCORE_URLS=http://localhost:8080 mcpproxy sse ./mcp-proxy.json
```

### Configuration Path

If you don't specify a config file path, MCP Proxy looks for:

1. The `MCP_PROXY_CONFIG_PATH` environment variable
2. `mcp-proxy.json` in the current directory

## Connecting a Client

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["stdio", "/path/to/mcp-proxy.json"]
    }
  }
}
```

### OpenCode

Add to your OpenCode configuration:

```json
{
  "mcp": {
    "mcp-proxy": {
      "type": "local",
      "command": ["mcpproxy", "stdio", "/path/to/mcp-proxy.json"],
      "enabled": true
    }
  }
}
```

### GitHub Copilot

Add to VS Code settings or `.vscode/mcp.json`:

```json
{
  "servers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["stdio", "/path/to/mcp-proxy.json"]
    }
  }
}
```

## Verifying the Setup

Once connected, your client should be able to see all tools from all configured backend servers. The tools will be listed with their original names (or prefixed names if you've configured prefixing).

You can enable verbose logging to troubleshoot issues:

```bash
mcpproxy stdio ./mcp-proxy.json --verbose
```

## Next Steps

- [Configuration Reference]({{ '/configuration/' | relative_url }}) - Learn about all configuration options
- [Features]({{ '/features/' | relative_url }}) - Explore filtering, prefixing, and hooks
- [Client Integrations]({{ '/integrations/' | relative_url }}) - Detailed setup for each client
