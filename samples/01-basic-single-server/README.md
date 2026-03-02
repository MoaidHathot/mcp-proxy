# Basic Single Server Example

This is the simplest possible MCP Proxy configuration. It demonstrates how to proxy a single MCP server through the proxy.

## Features Demonstrated

- **Single STDIO Backend**: Connects to one local MCP server
- **Minimal Configuration**: Shows the bare minimum needed to get started
- **Server Info**: Custom server name and version

## How It Works

The proxy starts a single MCP server (the Anthropic Filesystem server) as a child process and communicates with it via STDIO. All tools, resources, and prompts from this server are exposed through the proxy.

```
┌─────────────┐     STDIO      ┌─────────────┐     STDIO      ┌──────────────┐
│   Client    │ ◄────────────► │  MCP Proxy  │ ◄────────────► │  Filesystem  │
│ (e.g. Claude)│                │             │                │    Server    │
└─────────────┘                └─────────────┘                └──────────────┘
```

## Configuration Breakdown

```json
{
  "proxy": {
    "serverInfo": {
      "name": "Basic Single Server Example",  // Proxy server name
      "version": "1.0.0"                       // Proxy version
    }
  },
  "mcp": {
    "filesystem": {                            // Server identifier
      "type": "stdio",                         // STDIO transport
      "command": "npx",                        // Command to run
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "."],
      "enabled": true
    }
  }
}
```

## Prerequisites

- Node.js 18+ installed
- `npx` available in PATH

## Running the Example

### With STDIO Transport (for Claude Desktop, OpenCode, etc.)

```bash
mcpproxy -t stdio -c ./mcp-proxy.json
```

### With HTTP/SSE Transport

```bash
mcpproxy -t sse -c ./mcp-proxy.json -p 5000
```

Then connect to `http://localhost:5000/mcp/sse`

## Claude Desktop Configuration

Add to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "mcp-proxy": {
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "/path/to/samples/01-basic-single-server/mcp-proxy.json"]
    }
  }
}
```

## OpenCode Configuration

Add to your `opencode.json`:

```json
{
  "mcp": {
    "mcp-proxy": {
      "type": "local",
      "command": ["mcpproxy", "-t", "stdio", "-c", "/path/to/samples/01-basic-single-server/mcp-proxy.json"],
      "enabled": true
    }
  }
}
```

## Available Tools

After starting, the proxy exposes all tools from the Filesystem server:

- `read_file` - Read contents of a file
- `write_file` - Write contents to a file
- `list_directory` - List directory contents
- `create_directory` - Create a new directory
- `move_file` - Move or rename a file
- `search_files` - Search for files matching a pattern
- `get_file_info` - Get metadata about a file

## Next Steps

- See [02-basic-multiple-servers](../02-basic-multiple-servers) to learn how to aggregate multiple servers
- See [03-tool-filtering](../03-tool-filtering) to learn how to filter which tools are exposed
