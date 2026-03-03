---
layout: default
title: Client Integrations
description: Setup guides for popular MCP clients
---

## Client Integrations

This page provides detailed setup guides for connecting MCP Proxy to popular MCP clients.

<div class="toc">
<h4>On this page</h4>
<ul>
<li><a href="#claude-desktop">Claude Desktop</a></li>
<li><a href="#opencode">OpenCode</a></li>
<li><a href="#github-copilot">GitHub Copilot</a></li>
<li><a href="#custom-http-clients">Custom HTTP Clients</a></li>
</ul>
</div>

## Claude Desktop

Claude Desktop uses a JSON configuration file to define MCP servers.

### Configuration File Location

| Platform | Path |
|----------|------|
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| Linux | `~/.config/Claude/claude_desktop_config.json` |

### Using Installed Tool

After installing MCP Proxy globally:

```bash
dotnet tool install -g McpProxy
```

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "/path/to/mcp-proxy.json"]
    }
  }
}
```

### Using dnx (No Installation)

If you don't want to install globally:

```json
{
  "mcpServers": {
    "mcp-proxy": {
      "command": "dnx",
      "args": ["McpProxy", "--", "-t", "stdio", "-c", "/path/to/mcp-proxy.json"]
    }
  }
}
```

### With Environment Variables

Pass environment variables to the proxy:

```json
{
  "mcpServers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "/path/to/mcp-proxy.json"],
      "env": {
        "GITHUB_TOKEN": "your-token-here",
        "API_KEY": "your-api-key"
      }
    }
  }
}
```

### Troubleshooting

**Proxy not starting:**
1. Verify the path to your config file is correct
2. Check that `mcpproxy` is in your PATH (run `mcpproxy --help` in terminal)
3. Check Claude Desktop's MCP logs

**Tools not appearing:**
1. Verify your `mcp-proxy.json` configuration is valid JSON
2. Check that backend servers are configured correctly
3. Enable verbose logging: `["-t", "stdio", "-c", "/path/to/config.json", "-v"]`

## OpenCode

OpenCode supports MCP servers through its configuration system.

### Configuration File

OpenCode reads MCP configuration from `opencode.json` or your project's configuration file.

### Using Installed Tool

```json
{
  "mcp": {
    "mcp-proxy": {
      "type": "local",
      "command": ["mcpproxy", "-t", "stdio", "-c", "/path/to/mcp-proxy.json"],
      "enabled": true
    }
  }
}
```

### Using dnx

```json
{
  "mcp": {
    "mcp-proxy": {
      "type": "local",
      "command": ["dnx", "McpProxy", "--", "-t", "stdio", "-c", "/path/to/mcp-proxy.json"],
      "enabled": true
    }
  }
}
```

### With Verbose Logging

```json
{
  "mcp": {
    "mcp-proxy": {
      "type": "local",
      "command": ["mcpproxy", "-t", "stdio", "-c", "/path/to/mcp-proxy.json", "-v"],
      "enabled": true
    }
  }
}
```

### Project-Specific Configuration

You can use relative paths for project-specific configs:

```json
{
  "mcp": {
    "mcp-proxy": {
      "type": "local",
      "command": ["mcpproxy", "-t", "stdio", "-c", "./.mcp-proxy.json"],
      "enabled": true
    }
  }
}
```

## GitHub Copilot

GitHub Copilot in VS Code supports MCP servers through settings or workspace configuration.

### VS Code Settings

Add to your VS Code `settings.json`:

```json
{
  "github.copilot.chat.mcp.servers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "/path/to/mcp-proxy.json"]
    }
  }
}
```

### Workspace Configuration

Create `.vscode/mcp.json` in your project:

```json
{
  "servers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "${workspaceFolder}/.mcp-proxy.json"]
    }
  }
}
```

### Using dnx

```json
{
  "servers": {
    "mcp-proxy": {
      "command": "dnx",
      "args": ["McpProxy", "--", "-t", "stdio", "-c", "/path/to/mcp-proxy.json"]
    }
  }
}
```

### Multiple Configurations

For different projects, use workspace-relative paths:

```json
{
  "servers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "${workspaceFolder}/mcp-proxy.json"]
    }
  }
}
```

## Custom HTTP Clients

For custom integrations or HTTP-based clients, run MCP Proxy in SSE mode.

### Starting the Server

```bash
# Default port (5000)
mcpproxy -t sse -c ./mcp-proxy.json

# Custom port
mcpproxy -t sse -c ./mcp-proxy.json -p 8080

# Or via environment variable
ASPNETCORE_URLS=http://localhost:8080 mcpproxy -t sse -c ./mcp-proxy.json
```

### Endpoints

With unified routing (default):

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/mcp/sse` | GET | SSE connection endpoint |
| `/mcp/message` | POST | JSON-RPC message endpoint |

With per-server routing:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/mcp/{server}/sse` | GET | Server-specific SSE endpoint |
| `/mcp/{server}/message` | POST | Server-specific message endpoint |

### Example: cURL

```bash
# Connect via SSE
curl -N http://localhost:5000/mcp/sse

# Send a message (in another terminal)
curl -X POST http://localhost:5000/mcp/message \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### Example: JavaScript/TypeScript

```typescript
import { EventSource } from 'eventsource';

const sse = new EventSource('http://localhost:5000/mcp/sse');

sse.onmessage = (event) => {
  const message = JSON.parse(event.data);
  console.log('Received:', message);
};

// Send a request
async function listTools() {
  const response = await fetch('http://localhost:5000/mcp/message', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      jsonrpc: '2.0',
      id: 1,
      method: 'tools/list'
    })
  });
  return response.json();
}
```

### Authentication

For production deployments, enable authentication:

```json
{
  "proxy": {
    "authentication": {
      "enabled": true,
      "type": "apiKey",
      "apiKey": {
        "header": "X-API-Key",
        "value": "env:MCP_PROXY_API_KEY"
      }
    }
  }
}
```

Then include the header in requests:

```bash
curl -X POST http://localhost:5000/mcp/message \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### Docker Deployment

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN dotnet tool install -g McpProxy
ENV PATH="$PATH:/root/.dotnet/tools"

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=build /root/.dotnet/tools /tools
ENV PATH="$PATH:/tools"

COPY mcp-proxy.json /app/
WORKDIR /app

EXPOSE 5000
CMD ["mcpproxy", "-t", "sse", "-c", "/app/mcp-proxy.json"]
```

```bash
docker build -t mcp-proxy .
docker run -p 5000:5000 -e ASPNETCORE_URLS=http://+:5000 mcp-proxy
```

## Common Issues

### "Command not found"

Ensure the .NET tools directory is in your PATH:

```bash
# Add to ~/.bashrc or ~/.zshrc
export PATH="$PATH:$HOME/.dotnet/tools"
```

### Connection Refused

1. Check the proxy is running: `ps aux | grep mcpproxy`
2. Verify the port is correct
3. Check firewall settings

### Tools Not Appearing

1. Verify backend server configurations
2. Check for filtering rules that might exclude tools
3. Enable verbose logging to see what's being loaded

### Performance Issues

1. Enable caching (enabled by default)
2. Increase cache TTL for stable backends
3. Consider using per-server routing for high-traffic deployments

## Sample Projects

For complete working examples of client integrations, see these sample projects:

| Sample | Description |
|--------|-------------|
| [01-minimal](../samples/01-minimal/) | Basic stdio-based setup |
| [02-multi-server](../samples/02-multi-server/) | Multiple backend servers |
| [03-sse-server](../samples/03-sse-server/) | HTTP/SSE server configuration |

See the [samples README](../samples/README.md) for the complete list of 13 samples covering JSON configuration and SDK/programmatic approaches.
