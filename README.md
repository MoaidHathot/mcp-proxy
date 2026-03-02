# MCP Proxy

A proxy server for the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) that aggregates multiple MCP servers into a single endpoint. This allows clients to connect to one proxy and access tools, resources, and prompts from multiple backend MCP servers.

**[View Full Documentation](https://moaidhathot.github.io/mcp-proxy/)**

## Features

- **Aggregates multiple MCP servers** - Connect to multiple backend MCP servers and expose all their tools, resources, and prompts through a single endpoint
- **Dual transport support** - Run the proxy with either STDIO or HTTP/SSE transport
- **Supports STDIO and HTTP backends** - Connect to backend MCP servers using STDIO (local processes) or HTTP/SSE (remote servers)
- **Tool filtering** - AllowList, DenyList, or regex-based filtering per server
- **Tool prefixing** - Add server-specific prefixes to avoid name collisions
- **Hook system** - Pre-invoke and post-invoke hooks for logging, input/output transformation
- **Advanced MCP support** - Sampling, elicitation, and roots forwarding to clients
- **Per-server routing** - Expose each server on its own HTTP endpoint or aggregate all under one
- **Authentication** - API key, Bearer token, and Azure AD (Microsoft Entra ID) authentication for HTTP endpoints

## Installation

### Install as a .NET Global Tool (Recommended)

```bash
dotnet tool install -g McpProxy
```

After installation, you can run the proxy using:

```bash
mcpproxy -t stdio -c ./mcp-proxy.json
mcpproxy -t sse -c ./mcp-proxy.json
```

### Run with dnx (No Installation Required)

You can run McpProxy directly without installing it using `dnx`:

```bash
dnx McpProxy -- -t stdio -c ./mcp-proxy.json
dnx McpProxy -- -t sse -c ./mcp-proxy.json
```

### Build from Source

```bash
git clone https://github.com/MoaidHathot/mcp-proxy.git
cd mcp-proxy
dotnet build src/McpProxy
dotnet run --project src/McpProxy -- -t stdio -c ./mcp-proxy.json
```

## Quick Start

1. Create a configuration file `mcp-proxy.json`:

```json
{
  "mcp": {
    "filesystem": {
      "type": "stdio",
      "title": "Filesystem MCP",
      "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "/path/to/directory"]
    },
    "context7": {
      "type": "sse",
      "title": "Context7 MCP",
      "url": "https://mcp.context7.com/sse"
    }
  }
}
```

2. Run the proxy:

```bash
mcpproxy -t stdio -c ./mcp-proxy.json
```

3. Connect your MCP client (Claude Desktop, OpenCode, GitHub Copilot, etc.) to the proxy.

## Usage

```bash
mcpproxy [options]
```

### Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--transport` | `-t` | Server transport type: `stdio`, `http`, or `sse` | `stdio` |
| `--config` | `-c` | Path to mcp-proxy.json configuration file | Auto-detect |
| `--port` | `-p` | Port for HTTP/SSE server | `5000` |
| `--verbose` | `-v` | Enable verbose logging | `false` |

### Environment Variables

| Variable | Description |
|----------|-------------|
| `MCP_PROXY_CONFIG_PATH` | Path to the configuration file (used if `--config` is not provided) |

### Examples

```bash
# Run with STDIO transport (for use with Claude Desktop, etc.)
mcpproxy -t stdio

# Run with SSE transport (for HTTP clients)
mcpproxy -t sse

# Run with STDIO and specific config file
mcpproxy -t stdio -c ./mcp-proxy.json

# Run with SSE, custom port, and verbose logging
mcpproxy -t sse -c /path/to/config.json -p 8080 -v
```

## Configuration Reference

The configuration file is JSON with support for comments and trailing commas. Environment variables can be referenced using `"env:VAR_NAME"` or `${VAR_NAME}` syntax.

### Root Structure

```json
{
  "proxy": {
    "serverInfo": { ... },
    "routing": { ... },
    "authentication": { ... },
    "logging": { ... },
    "caching": { ... }
  },
  "mcp": {
    "server-name": { ... }
  }
}
```

### Proxy Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `proxy.serverInfo.name` | string | `"MCP Proxy"` | Server name exposed to clients |
| `proxy.serverInfo.version` | string | `"1.0.0"` | Server version |
| `proxy.serverInfo.instructions` | string | `null` | Instructions for the client |
| `proxy.routing.mode` | string | `"unified"` | `"unified"` or `"perServer"` |
| `proxy.routing.basePath` | string | `"/mcp"` | Base path for MCP endpoints |
| `proxy.caching.tools.enabled` | bool | `true` | Enable tool list caching |
| `proxy.caching.tools.ttlSeconds` | int | `60` | Cache time-to-live in seconds |

### Authentication Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `proxy.authentication.enabled` | bool | `false` | Enable authentication |
| `proxy.authentication.type` | string | `"none"` | `"none"`, `"apiKey"`, `"bearer"`, or `"azureAd"` |
| `proxy.authentication.apiKey.header` | string | `"X-API-Key"` | Header name for API key |
| `proxy.authentication.apiKey.queryParameter` | string | `null` | Query parameter fallback |
| `proxy.authentication.apiKey.value` | string | `null` | Expected API key (supports `env:VAR`) |
| `proxy.authentication.bearer.authority` | string | `null` | JWT authority URL |
| `proxy.authentication.bearer.audience` | string | `null` | Expected audience |
| `proxy.authentication.bearer.validateIssuer` | bool | `true` | Whether to validate the issuer |
| `proxy.authentication.bearer.validateAudience` | bool | `true` | Whether to validate the audience |
| `proxy.authentication.bearer.validIssuers` | string[] | `null` | List of valid issuers |
| `proxy.authentication.azureAd.tenantId` | string | `null` | Azure AD tenant ID or domain |
| `proxy.authentication.azureAd.clientId` | string | `null` | Application (client) ID |
| `proxy.authentication.azureAd.audience` | string | `clientId` | Expected audience |
| `proxy.authentication.azureAd.requiredScopes` | string[] | `null` | Required OAuth scopes |
| `proxy.authentication.azureAd.requiredRoles` | string[] | `null` | Required app roles |

### Logging Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `proxy.logging.logRequests` | bool | `true` | Log incoming requests |
| `proxy.logging.logResponses` | bool | `false` | Log outgoing responses |
| `proxy.logging.sensitiveDataMask` | bool | `true` | Mask sensitive data in logs |

### Server Configuration

Each entry in the `mcp` object configures a backend MCP server.

#### STDIO Backend

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "title": "My Local Server",
      "description": "A local MCP server",
      "command": "node",
      "arguments": ["server.js"],
      "environment": {
        "API_KEY": "env:MY_API_KEY"
      },
      "enabled": true
    }
  }
}
```

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `type` | string | Yes | Must be `"stdio"` |
| `title` | string | No | Display name |
| `description` | string | No | Server description |
| `command` | string | Yes | Command to execute |
| `arguments` | string[] | No | Command-line arguments |
| `environment` | object | No | Environment variables (supports `env:VAR`) |
| `enabled` | bool | No | Whether server is enabled (default: `true`) |

#### HTTP/SSE Backend

```json
{
  "mcp": {
    "remote-server": {
      "type": "sse",
      "title": "Remote MCP Server",
      "url": "https://example.com/mcp/sse",
      "headers": {
        "Authorization": "Bearer env:API_TOKEN"
      }
    }
  }
}
```

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `type` | string | Yes | `"sse"` for SSE-only, `"http"` for Streamable HTTP with auto-detection |
| `title` | string | No | Display name |
| `description` | string | No | Server description |
| `url` | string | Yes | URL of the MCP server endpoint |
| `headers` | object | No | Custom headers (supports `env:VAR`) |
| `enabled` | bool | No | Whether server is enabled (default: `true`) |

> **Note**: Use `"sse"` for servers that only support Server-Sent Events (most current MCP servers). Use `"http"` for servers that support the newer Streamable HTTP transport (auto-detects and falls back to SSE).

### Tool Filtering

Filter which tools are exposed from each server.

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "node",
      "arguments": ["server.js"],
      "tools": {
        "filter": {
          "mode": "allowlist",
          "patterns": ["read_*", "write_*"],
          "caseInsensitive": true
        }
      }
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `tools.filter.mode` | string | `"none"` | `"none"`, `"allowlist"`, `"denylist"`, or `"regex"` |
| `tools.filter.patterns` | string[] | `[]` | Patterns to match (wildcards `*` and `?` supported) |
| `tools.filter.caseInsensitive` | bool | `true` | Case-insensitive matching |

**Filter Modes:**
- `none`: Include all tools (default)
- `allowlist`: Only include tools matching any pattern
- `denylist`: Exclude tools matching any pattern
- `regex`: First pattern is include regex, optional second is exclude regex

### Tool Prefixing

Add a prefix to all tool names from a server to avoid collisions.

```json
{
  "mcp": {
    "github": {
      "type": "http",
      "url": "https://github-mcp.example.com",
      "tools": {
        "prefix": "gh",
        "prefixSeparator": "_"
      }
    }
  }
}
```

With this config, a tool named `create_issue` would be exposed as `gh_create_issue`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `tools.prefix` | string | `null` | Prefix to add to tool names |
| `tools.prefixSeparator` | string | `"_"` | Separator between prefix and name |

### Hook System

Hooks allow you to execute custom logic before and after tool calls.

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "node",
      "arguments": ["server.js"],
      "hooks": {
        "preInvoke": [
          {
            "type": "logging",
            "config": {
              "logLevel": "debug",
              "logArguments": true
            }
          }
        ],
        "postInvoke": [
          {
            "type": "outputTransform",
            "config": {
              "redactPatterns": ["password", "secret", "token"]
            }
          }
        ]
      }
    }
  }
}
```

**Built-in Hooks:**

| Hook Type | Phase | Description |
|-----------|-------|-------------|
| `logging` | Pre/Post | Logs tool invocations |
| `inputTransform` | Pre | Transforms input arguments |
| `outputTransform` | Post | Transforms output results |

**Logging Hook Config:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `logLevel` | string | `"information"` | Log level: `debug`, `information`, `warning`, `error` |
| `logArguments` | bool | `false` | Include arguments in logs |
| `logResult` | bool | `false` | Include result in logs |

**Output Transform Hook Config:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `redactPatterns` | string[] | `[]` | Patterns to redact from output |
| `redactedValue` | string | `"[REDACTED]"` | Replacement text for redacted content |

### Per-Server Routing

Expose each server on its own HTTP endpoint instead of aggregating all under one.

```json
{
  "proxy": {
    "routing": {
      "mode": "perServer",
      "basePath": "/mcp"
    }
  },
  "mcp": {
    "github": {
      "type": "http",
      "url": "https://github-mcp.example.com",
      "route": "/github"
    },
    "filesystem": {
      "type": "stdio",
      "command": "node",
      "arguments": ["fs-server.js"],
      "route": "/fs"
    }
  }
}
```

With this config:
- GitHub server tools available at `/mcp/github/tools/list`, `/mcp/github/tools/call`, etc.
- Filesystem server tools available at `/mcp/fs/tools/list`, `/mcp/fs/tools/call`, etc.

## Advanced MCP Protocol Support

MCP Proxy supports advanced MCP protocol features by forwarding requests between backend servers and connected clients.

### Sampling

Backend MCP servers can request LLM completions from the client through the proxy. When a backend server calls sampling, the proxy forwards the request to the connected client and returns the response.

### Elicitation

Backend MCP servers can request structured user input through the proxy. The proxy forwards elicitation requests to the connected client, which can prompt the user for input.

### Roots

Backend MCP servers can request file system root information from the client through the proxy.

> **Note**: These features require the connected client to support the respective capabilities. The proxy handles graceful degradation when clients don't support these features.

## Client Configuration Examples

### OpenCode

Add to your OpenCode configuration file (`opencode.json`):

#### Using the installed dotnet tool

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

#### Using dnx (no installation required)

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

### GitHub Copilot CLI

Add to your VS Code settings (`settings.json`) or workspace settings:

#### Using the installed dotnet tool

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

#### Using dnx (no installation required)

```json
{
  "github.copilot.chat.mcp.servers": {
    "mcp-proxy": {
      "command": "dnx",
      "args": ["McpProxy", "--", "-t", "stdio", "-c", "/path/to/mcp-proxy.json"]
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
      "args": ["-t", "stdio", "-c", "/path/to/mcp-proxy.json"]
    }
  }
}
```

### Claude Desktop

Add to your Claude Desktop configuration (`claude_desktop_config.json`):

#### Using the installed dotnet tool

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

#### Using dnx (no installation required)

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

### HTTP/SSE Clients

Start the proxy in SSE mode:

```bash
mcpproxy -t sse -c ./mcp-proxy.json
```

The server will start on the default ASP.NET Core port (typically `http://localhost:5000`). You can configure the port using the `--port` option:

```bash
mcpproxy -t sse -c ./mcp-proxy.json -p 8080
```

Or via environment variable:

```bash
ASPNETCORE_URLS=http://localhost:8080 mcpproxy -t sse -c ./mcp-proxy.json
```

## Complete Configuration Example

```json
{
  // Proxy-level settings
  "proxy": {
    "serverInfo": {
      "name": "My MCP Proxy",
      "version": "1.0.0",
      "instructions": "This proxy aggregates multiple MCP servers."
    },
    "routing": {
      "mode": "unified",
      "basePath": "/mcp"
    },
    "authentication": {
      "enabled": false
    },
    "logging": {
      "logRequests": true,
      "logResponses": false,
      "sensitiveDataMask": true
    }
  },
  
  // Backend MCP servers
  "mcp": {
    // Local STDIO server with filtering and hooks
    "filesystem": {
      "type": "stdio",
      "title": "Filesystem Server",
      "description": "Provides file system access",
      "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "/workspace"],
      "tools": {
        "prefix": "fs",
        "prefixSeparator": "_",
        "filter": {
          "mode": "denylist",
          "patterns": ["delete_*", "remove_*"],
          "caseInsensitive": true
        }
      },
      "hooks": {
        "preInvoke": [
          { "type": "logging", "config": { "logLevel": "debug" } }
        ]
      }
    },
    
    // Remote SSE server with authentication
    "context7": {
      "type": "sse",
      "title": "Context7",
      "description": "Context7 documentation search",
      "url": "https://mcp.context7.com/sse"
    },
    
    // Remote HTTP server with custom headers
    "github": {
      "type": "http",
      "title": "GitHub MCP",
      "url": "https://github-mcp.example.com",
      "headers": {
        "Authorization": "Bearer ${GITHUB_TOKEN}"
      },
      "tools": {
        "prefix": "gh"
      }
    }
  }
}
```

## Programmatic Usage

MCP Proxy can also be used as a library in your .NET applications:

```csharp
using McpProxy.Core.Configuration;
using McpProxy.Core.Proxy;
using Microsoft.Extensions.Logging;

// Load configuration
var config = await ConfigurationLoader.LoadAsync("mcp-proxy.json");

// Create loggers (using your logging factory)
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var clientManagerLogger = loggerFactory.CreateLogger<McpClientManager>();
var proxyServerLogger = loggerFactory.CreateLogger<McpProxyServer>();

// Create client manager
var clientManager = new McpClientManager(clientManagerLogger);

// Initialize connections to backend servers
await clientManager.InitializeAsync(config, cancellationToken);

// Create proxy server
var proxyServer = new McpProxyServer(
    proxyServerLogger,
    clientManager,
    config
);

// Use the proxy server to handle MCP requests
// These methods take RequestContext<T> from the MCP server infrastructure
var tools = await proxyServer.ListToolsCoreAsync(cancellationToken);
var result = await proxyServer.CallToolCoreAsync(callToolParams, cancellationToken);
```

## License

MIT
