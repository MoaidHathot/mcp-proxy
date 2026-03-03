# MCP Proxy Samples

This directory contains example configurations demonstrating various features of MCP Proxy. Each sample includes a complete configuration file and detailed documentation.

## Sample Overview

| # | Sample | Complexity | Features |
|---|--------|------------|----------|
| 01 | [Basic Single Server](./01-basic-single-server) | Beginner | Single STDIO backend, minimal config |
| 02 | [Multiple Servers](./02-basic-multiple-servers) | Beginner | Server aggregation, tool prefixing |
| 03 | [Tool Filtering](./03-tool-filtering) | Intermediate | Allowlist, denylist, regex filtering |
| 04 | [Remote Servers](./04-remote-servers) | Intermediate | HTTP/SSE backends, environment variables |
| 05 | [API Key Auth](./05-http-api-key-auth) | Intermediate | HTTP mode, API key authentication |
| 06 | [Hooks](./06-hooks) | Advanced | Logging, rate limiting, audit, content filter |
| 07 | [Azure AD Auth](./07-azure-ad-auth) | Advanced | OAuth2/OIDC, role-based access |
| 08 | [Telemetry](./08-telemetry) | Advanced | OpenTelemetry, metrics, tracing |
| 09 | [Per-Server Routing](./09-per-server-routing) | Advanced | Individual server endpoints |
| 10 | [Enterprise Complete](./10-enterprise-complete) | Expert | All features combined |
| 11 | [SDK Basic](./11-sdk-basic) | Intermediate | Programmatic configuration, fluent API |
| 12 | [SDK Hooks & Interceptors](./12-sdk-hooks-interceptors) | Advanced | Code-based hooks, tool interceptors |
| 13 | [SDK Virtual Tools](./13-sdk-virtual-tools) | Advanced | Virtual tools, tool modification |

## Quick Start

### Prerequisites

- .NET 10 SDK
- Node.js 18+ (for npm MCP servers)
- MCP Proxy installed (`dotnet tool install -g McpProxy`)

### Running a Sample

#### JSON Configuration Samples (01-10)

```bash
# Navigate to a sample directory
cd samples/01-basic-single-server

# Run with STDIO transport (for Claude Desktop, OpenCode)
mcpproxy -t stdio -c ./mcp-proxy.json

# Or run with HTTP/SSE transport
mcpproxy -t sse -c ./mcp-proxy.json -p 5000
```

#### SDK Samples (11-13)

```bash
# Navigate to an SDK sample directory
cd samples/11-sdk-basic

# Build and run
dotnet build
dotnet run
```

## Learning Path

### Beginners
Start with these samples to understand the basics:

1. **[01-basic-single-server](./01-basic-single-server)**: Minimal configuration
2. **[02-basic-multiple-servers](./02-basic-multiple-servers)**: Core aggregation feature

### Intermediate Users
Learn about security, remote servers, and SDK usage:

3. **[03-tool-filtering](./03-tool-filtering)**: Control which tools are exposed
4. **[04-remote-servers](./04-remote-servers)**: Connect to HTTP/SSE backends
5. **[05-http-api-key-auth](./05-http-api-key-auth)**: Secure your proxy
6. **[11-sdk-basic](./11-sdk-basic)**: Programmatic configuration with fluent API

### Advanced Users
Enterprise features, observability, and SDK capabilities:

7. **[06-hooks](./06-hooks)**: Runtime processing hooks (JSON-based)
8. **[07-azure-ad-auth](./07-azure-ad-auth)**: Enterprise authentication
9. **[08-telemetry](./08-telemetry)**: Monitoring and tracing
10. **[12-sdk-hooks-interceptors](./12-sdk-hooks-interceptors)**: Code-based hooks and interceptors
11. **[13-sdk-virtual-tools](./13-sdk-virtual-tools)**: Virtual tools and tool modification

### Production Deployment
Complete examples for production:

12. **[09-per-server-routing](./09-per-server-routing)**: API gateway pattern
13. **[10-enterprise-complete](./10-enterprise-complete)**: Full production setup

## Configuration Approaches

MCP Proxy supports two configuration approaches:

### JSON Configuration (Samples 01-10)

Use JSON files for static, declarative configuration:

```json
{
  "mcp": {
    "filesystem": {
      "type": "stdio",
      "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "/workspace"]
    }
  }
}
```

**Best for:** Static deployments, CI/CD pipelines, simple setups

### SDK/Programmatic Configuration (Samples 11-13)

Use C# code for dynamic, programmatic configuration:

```csharp
builder.Services.AddMcpProxy(proxy =>
{
    proxy.AddStdioServer("filesystem", "npx", "-y", "@anthropic/mcp-server-filesystem", "/workspace")
        .WithToolPrefix("fs")
        .DenyTools("delete_*")
        .OnPreInvoke(ctx => { /* custom logic */ })
        .Build();

    proxy.AddTool("custom_tool", "My tool", (req, ct) =>
        ValueTask.FromResult(new CallToolResult { /* ... */ }));
});
```

**Best for:** Dynamic server discovery, custom hooks, virtual tools, integration with existing apps

## Feature Matrix

| Feature | 01 | 02 | 03 | 04 | 05 | 06 | 07 | 08 | 09 | 10 | 11 | 12 | 13 |
|---------|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| STDIO Backend | X | X | X | X | X | X | X | X | X | X | X | X | X |
| HTTP Backend | | | | X | | | X | | | X | X | | |
| SSE Backend | | | | X | | | | | | | X | X | X |
| Tool Prefixing | | X | X | X | | | | | | X | X | X | X |
| Tool Filtering | | | X | | | | | | | X | X | X | X |
| API Key Auth | | | | | X | | | | X | | | | |
| Azure AD Auth | | | | | | | X | | | X | | | |
| JSON Hooks | | | | | | X | X | X | | X | | | |
| SDK Hooks | | | | | | | | | | | | X | |
| Rate Limiting | | | | | | X | | | | X | | | |
| Audit Logging | | | | | | X | | | | X | | | |
| Content Filter | | | | | | X | | | | X | | X | |
| Telemetry | | | | | | | | X | | X | | | |
| Per-Server Routing | | | | | | | | | X | | | | |
| Virtual Tools | | | | | | | | | | | | | X |
| Tool Interceptors | | | | | | | | | | | | X | X |
| Fluent Builder | | | | | | | | | | | X | X | X |

## Client Integration

### Claude Desktop

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

### OpenCode

Add to `opencode.json`:
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

### VS Code (GitHub Copilot)

Add to `settings.json`:
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

## Troubleshooting

### Common Issues

| Issue | Solution |
|-------|----------|
| "Command not found" | Install MCP Proxy: `dotnet tool install -g McpProxy` |
| "npx not found" | Install Node.js and ensure npx is in PATH |
| Connection timeout | Check backend server URLs and network access |
| Auth failures | Verify API keys/tokens and environment variables |

### Debug Mode

Run with verbose logging:
```bash
mcpproxy -t stdio -c ./mcp-proxy.json -v
```

## Contributing

Feel free to submit additional samples or improvements to existing ones!

## License

These samples are provided under the same license as the main MCP Proxy project.
