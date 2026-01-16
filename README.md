# MCP Proxy

A proxy server for the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) that aggregates multiple MCP servers into a single endpoint. This allows clients to connect to one proxy and access tools from multiple backend MCP servers.

## Features

- **Aggregates multiple MCP servers** - Connect to multiple backend MCP servers and expose all their tools through a single endpoint
- **Dual transport support** - Run the proxy with either STDIO or SSE (Server-Sent Events) transport
- **Supports STDIO and HTTP backends** - Connect to backend MCP servers using STDIO (local processes) or HTTP/SSE (remote servers)

## Installation

```bash
dotnet build src/McpProxy
```

## Usage

```bash
McpProxy <transport> [config-path]
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
McpProxy stdio

# Run with SSE transport (for HTTP clients)
McpProxy sse

# Run with STDIO and specific config file
McpProxy stdio ./mcp-proxy.json

# Run with SSE and specific config file
McpProxy sse /path/to/config.json
```

## Configuration

Create a `mcp-proxy.json` file to configure the backend MCP servers. See [`mcp-proxy.sample.json`](mcp-proxy.sample.json) for a complete example with both local (Azure MCP) and remote (Context7) servers.

```json
{
  "Mcp": {
    "azure-mcp": {
      "type": "stdio",
      "title": "Azure MCP Server",
      "description": "Official Microsoft Azure MCP Server",
      "command": "azmcp",
      "arguments": ["server", "start"]
    },
    "context7": {
      "type": "http",
      "title": "Context7 Documentation",
      "description": "Up-to-date code documentation for libraries",
      "url": "https://mcp.context7.com/mcp"
    }
  }
}
```

### Prerequisites for Sample Configuration

- **Azure MCP Server**: Install as a .NET global tool:
  ```bash
  dotnet tool install -g azure.mcp
  ```
- **Context7**: No installation required (remote HTTP server)

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

## Using with Claude Desktop

Add the following to your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "mcp-proxy": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/mcp-proxy/src/McpProxy", "--", "stdio", "/path/to/mcp-proxy.json"]
    }
  }
}
```

Or if you've published the executable:

```json
{
  "mcpServers": {
    "mcp-proxy": {
      "command": "/path/to/McpProxy",
      "args": ["stdio", "/path/to/mcp-proxy.json"]
    }
  }
}
```

## Using with HTTP/SSE Clients

Start the proxy in SSE mode:

```bash
McpProxy sse ./mcp-proxy.json
```

The server will start on the default ASP.NET Core port (typically `http://localhost:5000`). You can configure the port using standard ASP.NET Core configuration:

```bash
McpProxy sse ./mcp-proxy.json --urls "http://localhost:8080"
```

Or via environment variable:

```bash
ASPNETCORE_URLS=http://localhost:8080 McpProxy sse ./mcp-proxy.json
```

## License

MIT
