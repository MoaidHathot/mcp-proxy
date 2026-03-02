# Remote HTTP/SSE Backend Servers

This example demonstrates how to connect to remote MCP servers using HTTP and SSE transports, and how to mix local and remote backends.

## Features Demonstrated

- **HTTP Transport**: Connect to modern MCP servers with streamable HTTP
- **SSE Transport**: Connect to servers using Server-Sent Events
- **Mixed Backends**: Combine local STDIO and remote servers
- **Custom Headers**: Add authentication and custom headers to requests
- **Environment Variables**: Securely inject secrets from environment

## Transport Types

### STDIO
For local MCP servers running as child processes.

```json
{
  "type": "stdio",
  "command": "node",
  "arguments": ["server.js"]
}
```

### HTTP (Streamable HTTP)
For modern MCP servers supporting the streamable HTTP transport. Auto-detects and falls back to SSE if needed.

```json
{
  "type": "http",
  "url": "https://mcp.example.com/api"
}
```

### SSE (Server-Sent Events)
For MCP servers using the legacy SSE-only transport.

```json
{
  "type": "sse",
  "url": "https://sse.example.com/mcp/sse"
}
```

## Architecture

```
┌─────────────┐                ┌─────────────┐
│             │                │             │
│   Client    │ ◄────────────► │  MCP Proxy  │
│             │                │             │
└─────────────┘                └──────┬──────┘
                                      │
                    ┌─────────────────┼─────────────────┐
                    │                 │                 │
              ┌─────┴─────┐     ┌─────┴─────┐     ┌─────┴─────┐
              │  STDIO    │     │   HTTP    │     │   SSE     │
              │  Local    │     │  Remote   │     │  Remote   │
              │ Process   │     │  Server   │     │  Server   │
              └───────────┘     └───────────┘     └───────────┘
```

## Configuration Details

### Adding Custom Headers

```json
{
  "remote-server": {
    "type": "http",
    "url": "https://api.example.com/mcp",
    "headers": {
      "Authorization": "Bearer ${API_TOKEN}",
      "X-Custom-Header": "value"
    }
  }
}
```

### Environment Variable Substitution

Two formats are supported:

#### Full Value Substitution
```json
{
  "headers": {
    "X-API-Key": "env:MY_API_KEY"
  }
}
```
The entire value is replaced with the environment variable.

#### Embedded Substitution
```json
{
  "headers": {
    "Authorization": "Bearer ${API_TOKEN}"
  }
}
```
The `${VAR}` pattern is replaced within the string.

## Public MCP Servers

This example includes Context7, a public MCP server:

```json
{
  "context7": {
    "type": "http",
    "url": "https://mcp.context7.com/mcp",
    "enabled": true
  }
}
```

Context7 provides up-to-date documentation for popular libraries and frameworks.

## Running the Example

### Prerequisites
Set required environment variables:

```bash
# For remote servers (if enabled)
export MCP_API_TOKEN="your-api-token"
export REMOTE_API_KEY="your-remote-api-key"
```

### Start the Proxy

```bash
mcpproxy -t stdio -c ./mcp-proxy.json
```

### Enable/Disable Servers

Edit `mcp-proxy.json` and set `"enabled": true` or `"enabled": false` for each server.

## Available Tools

### Local Filesystem (`local_*`)
- `local_read_file`
- `local_write_file`
- `local_list_directory`

### Context7 (`c7_*`)
- `c7_resolve-library-id` - Resolve a library name to its Context7 ID
- `c7_get-library-docs` - Get documentation for a library

## HTTP vs SSE: When to Use Which

| Transport | Use When |
|-----------|----------|
| HTTP | Server supports streamable HTTP (modern servers) |
| SSE | Server only supports SSE transport (legacy servers) |

The HTTP transport automatically falls back to SSE if the server doesn't support streamable HTTP, making it the safer default choice.

## Connecting to Your Own Remote Server

1. Deploy your MCP server with HTTP or SSE transport
2. Add it to the configuration:

```json
{
  "my-remote-server": {
    "type": "http",
    "title": "My Remote Server",
    "url": "https://my-server.example.com/mcp",
    "enabled": true,
    "headers": {
      "Authorization": "Bearer ${MY_SERVER_TOKEN}"
    }
  }
}
```

## Security Considerations

1. **Never hardcode secrets** - Use environment variables
2. **Use HTTPS** - Always use encrypted connections for remote servers
3. **Validate certificates** - Don't disable SSL verification in production
4. **Rotate tokens** - Regularly rotate API keys and tokens

## Next Steps

- See [05-http-api-key-auth](../05-http-api-key-auth) to secure your proxy with authentication
- See [07-azure-ad-auth](../07-azure-ad-auth) for enterprise Azure AD authentication
