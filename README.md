# MCP Proxy

A proxy server for the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) that aggregates multiple MCP servers into a single endpoint. This allows clients to connect to one proxy and access tools from multiple backend MCP servers.

## Features

- **Aggregates multiple MCP servers** - Connect to multiple backend MCP servers and expose all their tools through a single endpoint
- **Dual transport support** - Run the proxy with either STDIO or SSE (Server-Sent Events) transport
- **Supports STDIO and HTTP backends** - Connect to backend MCP servers using STDIO (local processes) or HTTP/SSE (remote servers)

## Installation

### Install as a .NET Global Tool (Recommended)

```bash
dotnet tool install -g McpProxy
```

After installation, you can run the proxy using:

```bash
mcpproxy stdio ./mcp-proxy.json
mcpproxy sse ./mcp-proxy.json
```

### Run with dnx (No Installation Required)

You can run McpProxy directly without installing it using `dnx`:

```bash
dnx McpProxy -- stdio ./mcp-proxy.json
dnx McpProxy -- sse ./mcp-proxy.json
```

### Build from Source

```bash
git clone https://github.com/MoaidHathot/mcp-proxy.git
cd mcp-proxy
dotnet build src/McpProxy
dotnet run --project src/McpProxy -- stdio ./mcp-proxy.json
```

## Usage

```bash
mcpproxy <transport> [config-path]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `transport` | Server transport type: `stdio` or `sse` |
| `config-path` | Path to configuration file (optional) |

### Environment Variables

| Variable | Description |
|----------|-------------|
| `MCP_PROXY_CONFIG_PATH` | Path to the configuration file (used if `config-path` argument is not provided) |

### Examples

```bash
# Run with STDIO transport (for use with Claude Desktop, etc.)
mcpproxy stdio

# Run with SSE transport (for HTTP clients)
mcpproxy sse

# Run with STDIO and specific config file
mcpproxy stdio ./mcp-proxy.json

# Run with SSE and specific config file
mcpproxy sse /path/to/config.json
```

## Configuration

Create a `mcp-proxy.json` file to configure the backend MCP servers. See [`mcp-proxy.sample.json`](mcp-proxy.sample.json) for a complete example with both local (Azure MCP) and remote (Context7) servers.

```json
{
  "Mcp": { 
    "sample-mcp-server": {
      "type": "stdio",
      "title": "Azure MCP Server",
      "description": "The official Azure MCP Server",
      "command": "dnx",
      "arguments": ["Azure.Mcp@2.0.0-beta.10", "--yes", "--", "server", "start"]
    },
    "microsoft-learn": {
      "type": "http",
      "title": "Microsoft Learn MCP",
      "description": "Microsoft Learn MCP Server",
      "url": "https://learn.microsoft.com/api/mcp"
    }
  }
}
```

### Configuration Options

#### STDIO Backend

| Property | Type | Description |
|----------|------|-------------|
| `type` | string | Must be `"stdio"` |
| `title` | string | Display name for the server |
| `description` | string | Description of the server |
| `command` | string | Command to execute |
| `arguments` | string[] | Command-line arguments |

#### HTTP/SSE Backend

| Property | Type | Description |
|----------|------|-------------|
| `type` | string | `"sse"` for SSE-only servers (like Context7), `"http"` for Streamable HTTP with auto-detection |
| `title` | string | Display name for the server |
| `description` | string | Description of the server |
| `url` | string | URL of the MCP server endpoint |

> **Note**: Use `"sse"` for servers that only support Server-Sent Events (most current MCP servers). Use `"http"` for servers that support the newer Streamable HTTP transport (auto-detects and falls back to SSE).

## Using with OpenCode

Add the following to your OpenCode configuration file (`opencode.json`):

### Using the installed dotnet tool

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

### Using dnx (no installation required)

```json
{
  "mcp": {
    "mcp-proxy": {
      "type": "local",
      "command": ["dnx", "McpProxy", "--", "stdio", "/path/to/mcp-proxy.json"],
      "enabled": true
    }
  }
}
```

## Using with GitHub Copilot CLI

Add the following to your VS Code settings (`settings.json`) or workspace settings:

### Using the installed dotnet tool

```json
{
  "github.copilot.chat.mcp.servers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["stdio", "/path/to/mcp-proxy.json"]
    }
  }
}
```

### Using dnx (no installation required)

```json
{
  "github.copilot.chat.mcp.servers": {
    "mcp-proxy": {
      "command": "dnx",
      "args": ["McpProxy", "--", "stdio", "/path/to/mcp-proxy.json"]
    }
  }
}
```

Alternatively, create a `.vscode/mcp.json` file in your project:

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

## Using with Claude Desktop

Add the following to your Claude Desktop configuration (`claude_desktop_config.json`):

### Using the installed dotnet tool

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

### Using dnx (no installation required)

```json
{
  "mcpServers": {
    "mcp-proxy": {
      "command": "dnx",
      "args": ["McpProxy", "--", "stdio", "/path/to/mcp-proxy.json"]
    }
  }
}
```

## Using with HTTP/SSE Clients

Start the proxy in SSE mode:

```bash
mcpproxy sse ./mcp-proxy.json
```

The server will start on the default ASP.NET Core port (typically `http://localhost:5000`). You can configure the port using standard ASP.NET Core configuration:

```bash
mcpproxy sse ./mcp-proxy.json --urls "http://localhost:8080"
```

Or via environment variable:

```bash
ASPNETCORE_URLS=http://localhost:8080 mcpproxy sse ./mcp-proxy.json
```

## License

MIT
