# Multiple Servers with Prefixing

This example demonstrates one of the core features of MCP Proxy: aggregating multiple MCP servers into a single endpoint with tool prefixing to avoid name collisions.

## Features Demonstrated

- **Multiple STDIO Backends**: Three different MCP servers running simultaneously
- **Tool Prefixing**: Each server's tools are prefixed to avoid naming conflicts
- **Server Instructions**: Custom instructions sent to the client
- **Server Titles and Descriptions**: Human-readable metadata for each backend

## How It Works

The proxy starts multiple MCP servers as child processes and aggregates all their tools, resources, and prompts into a single unified endpoint. Tool prefixing ensures that tools with the same name from different servers don't conflict.

```
┌─────────────┐                ┌─────────────┐
│   Client    │                │             │──────► Filesystem Server (fs_*)
│             │ ◄────────────► │  MCP Proxy  │──────► Time Server (time_*)
│             │                │             │──────► Memory Server (mem_*)
└─────────────┘                └─────────────┘
```

## Configuration Breakdown

### Server Instructions

```json
{
  "proxy": {
    "serverInfo": {
      "instructions": "This proxy provides access to filesystem operations, time utilities, and memory storage."
    }
  }
}
```

Instructions are sent to the client during initialization, helping LLMs understand what capabilities are available.

### Tool Prefixing

```json
{
  "filesystem": {
    "tools": {
      "prefix": "fs",
      "prefixSeparator": "_"
    }
  }
}
```

This configuration transforms tool names:
- `read_file` → `fs_read_file`
- `write_file` → `fs_write_file`

When a client calls `fs_read_file`, the proxy:
1. Strips the prefix `fs_`
2. Routes the call to the `filesystem` server
3. Calls the original `read_file` tool

## Prerequisites

- Node.js 18+ installed
- `npx` available in PATH

## Running the Example

### STDIO Mode

```bash
mcpproxy -t stdio -c ./mcp-proxy.json
```

### HTTP/SSE Mode

```bash
mcpproxy -t sse -c ./mcp-proxy.json -p 5000
```

## Available Tools

After starting, the proxy exposes tools from all three servers:

### Filesystem Tools (prefix: `fs_`)
| Original Name | Proxied Name |
|--------------|--------------|
| `read_file` | `fs_read_file` |
| `write_file` | `fs_write_file` |
| `list_directory` | `fs_list_directory` |

### Time Tools (prefix: `time_`)
| Original Name | Proxied Name |
|--------------|--------------|
| `get_current_time` | `time_get_current_time` |
| `convert_timezone` | `time_convert_timezone` |

### Memory Tools (prefix: `mem_`)
| Original Name | Proxied Name |
|--------------|--------------|
| `create_entities` | `mem_create_entities` |
| `create_relations` | `mem_create_relations` |
| `search_nodes` | `mem_search_nodes` |
| `read_graph` | `mem_read_graph` |

## Why Use Prefixing?

1. **Avoid Collisions**: Multiple servers might have tools with the same name
2. **Clear Origin**: Makes it obvious which server a tool belongs to
3. **Organized Discovery**: Clients can understand tool groupings
4. **Selective Access**: Easier to implement filtering by prefix

## Alternative: No Prefixing

If your servers don't have conflicting tool names, you can disable prefixing:

```json
{
  "filesystem": {
    "tools": {
      "prefix": null
    }
  }
}
```

Or simply omit the `tools` configuration entirely.

## Custom Prefix Separators

You can customize the separator between prefix and tool name:

```json
{
  "tools": {
    "prefix": "fs",
    "prefixSeparator": "::"  // Results in fs::read_file
  }
}
```

Common separators: `_`, `-`, `::`, `.`

## Next Steps

- See [03-tool-filtering](../03-tool-filtering) to learn how to filter which tools are exposed
- See [04-remote-servers](../04-remote-servers) to learn how to connect to remote MCP servers
