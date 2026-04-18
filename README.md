# MCP Proxy

An extensible proxy and gateway for the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/). Aggregates multiple MCP servers into a single endpoint, bridges transports (expose local stdio servers as remote HTTP endpoints and vice versa), handles authentication to remote backends (including Azure AD interactive browser and forward-authorization flows), and provides per-server routing, tool filtering, hooks, and a programmatic SDK.

**[View Full Documentation](https://moaidhathot.github.io/mcp-proxy/)**

## Features

- **Aggregates multiple MCP servers** - Connect to multiple backend MCP servers and expose all their tools, resources, and prompts through a single endpoint
- **Dual transport support** - Run the proxy with either STDIO or HTTP/SSE transport
- **Transport bridging** - Expose local STDIO servers as remote HTTP endpoints, or consume remote HTTP servers through a local STDIO interface
- **Supports STDIO and HTTP backends** - Connect to backend MCP servers using STDIO (local processes) or HTTP/SSE (remote servers)
- **Backend authentication** - Authenticate to remote backends using Azure AD client credentials, on-behalf-of, managed identity, forward-authorization, default Azure credential, or interactive browser (public client) flows
- **Interactive browser auth** - Authenticate as the current user via browser sign-in using a pre-authorized public client ID (e.g., for Microsoft 365 MCP servers). Tokens are cached in the OS credential store for silent re-authentication
- **Credential sharing** - Share a single credential instance across multiple backends via `credentialGroup`, avoiding duplicate browser prompts
- **Deferred connection** - Defer backend connections until the first client request via `deferConnection`, avoiding interactive auth at startup
- **Server selection** - Select specific servers from a config file via `--server` / `-s` to run a subset of backends
- **Tool filtering** - AllowList, DenyList, or regex-based filtering per server
- **Tool prefixing** - Add server-specific prefixes to avoid name collisions
- **Hook system** - Pre-invoke and post-invoke hooks for logging, input/output transformation
- **Advanced MCP support** - Sampling, elicitation, and roots forwarding to clients
- **Per-server routing** - Expose each server on its own HTTP endpoint or aggregate all under one
- **Proxy authentication** - API key, Bearer token, and Azure AD (Microsoft Entra ID) authentication for inbound HTTP requests
- **Config path expansion** - Environment variable expansion (`${VAR}`, `%VAR%`, `~`) in the `--config` path

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
| `--config` | `-c` | Path to mcp-proxy.json configuration file (supports `${VAR}`, `%VAR%`, `~` expansion) | Auto-detect |
| `--port` | `-p` | Port for HTTP/SSE server | `5000` |
| `--server` | `-s` | Select specific server(s) from the configuration. Can be specified multiple times | All servers |
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

# Run only specific servers from the config
mcpproxy -t stdio -c ./mcp-proxy.json -s calendar -s mail

# Use environment variable expansion in config path
mcpproxy -t stdio -c '${XDG_CONFIG_HOME}/my-proxy/config.json'
mcpproxy -t stdio -c '~/my-proxy/config.json'
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
| `proxy.caching.tools.ttlSeconds` | int | `300` | Cache time-to-live in seconds |

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

> **Note**: Use `"sse"` for servers that only support Server-Sent Events (most current MCP servers). Use `"http"` for servers that support the newer Streamable HTTP transport.

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

### Backend Authentication

Configure how the proxy authenticates to remote backend MCP servers. This is separate from proxy-level (inbound) authentication.

```json
{
  "mcp": {
    "my-backend": {
      "type": "http",
      "url": "https://api.example.com/mcp",
      "auth": {
        "type": "InteractiveBrowser",
        "credentialGroup": "shared",
        "deferConnection": true,
        "azureAd": {
          "clientId": "${PUBLIC_CLIENT_ID}",
          "tenantId": "${TENANT_ID}",
          "scopes": ["${AUDIENCE}/.default"]
        }
      }
    }
  }
}
```

#### Auth Types

| Type | Description |
|------|-------------|
| `None` | No authentication (default) |
| `AzureAdClientCredentials` | App-to-app authentication using client ID and secret |
| `AzureAdOnBehalfOf` | User-delegated access via on-behalf-of flow |
| `AzureAdManagedIdentity` | Authentication using Azure managed identity |
| `ForwardAuthorization` | Forward the incoming Authorization header to the backend (HTTP mode only) |
| `AzureDefaultCredential` | Auto-discover credentials from the environment (Azure CLI, env vars, managed identity, etc.) |
| `InteractiveBrowser` | Authenticate as the current user via browser sign-in using a pre-authorized public client ID. Tokens are cached in the OS credential store for silent re-authentication |

#### Auth Configuration Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `type` | string | `"None"` | Auth type (see table above) |
| `credentialGroup` | string | `null` | Backends with the same group share a single credential instance, avoiding duplicate browser prompts. Applies to `InteractiveBrowser` |
| `deferConnection` | bool | `false` | When `true`, defer backend connection until the first client request. Avoids interactive auth at startup |
| `azureAd.clientId` | string | `null` | Client/application ID |
| `azureAd.tenantId` | string | `null` | Azure AD tenant ID |
| `azureAd.clientSecret` | string | `null` | Client secret (supports `env:VAR_NAME`) |
| `azureAd.scopes` | string[] | `[".default"]` | OAuth scopes to request |
| `azureAd.tokenCacheName` | string | `"mcp-proxy"` | Persistent token cache name in OS credential store |
| `azureAd.redirectUri` | string | `null` | Redirect URI for interactive browser flow |

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

## SDK / Programmatic Usage

MCP Proxy can be consumed as an SDK in your .NET applications, providing a fluent API for programmatic configuration instead of (or in addition to) JSON configuration files.

### Quick Start

```csharp
using McpProxy.Sdk.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Configure MCP Proxy using the SDK
builder.Services.AddMcpProxy(proxy =>
{
    // Configure proxy metadata
    proxy
        .WithServerInfo("My MCP Proxy", "1.0.0", "Custom proxy server")
        .WithToolCaching(enabled: true, ttlSeconds: 120);

    // Add backend servers
    proxy.AddStdioServer("filesystem", "npx", "-y", "@anthropic/mcp-server-filesystem", "/tmp")
        .WithTitle("Filesystem Server")
        .WithToolPrefix("fs")
        .DenyTools("delete_*", "remove_*")
        .Build();

    proxy.AddSseServer("context7", "https://mcp.context7.com/sse")
        .WithTitle("Context7")
        .Build();

    proxy.AddHttpServer("github", "https://api.github.com/mcp")
        .WithHeaders(new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {Environment.GetEnvironmentVariable("GITHUB_TOKEN")}"
        })
        .WithToolPrefix("gh")
        .AllowTools("get_*", "list_*", "search_*")
        .Build();
});

var app = builder.Build();
await app.Services.InitializeMcpProxyAsync();
await app.RunAsync();
```

### Per-Server Routing (SDK)

To expose each backend on its own HTTP endpoint with isolated tools, use `MapPerServerMcpEndpoints()`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpProxy(proxy =>
{
    proxy.WithServerInfo("My Proxy", "1.0.0");

    // Configure per-server routing
    proxy.WithRouting(RoutingMode.PerServer, "/mcp");

    proxy.AddHttpServer("calendar", "https://example.com/calendar-mcp")
        .WithTitle("Calendar")
        .Build();

    proxy.AddHttpServer("mail", "https://example.com/mail-mcp")
        .WithTitle("Mail")
        .Build();
});

builder.Services.AddMcpServer().WithHttpTransport().WithSdkProxyHandlers();

var app = builder.Build();
await app.InitializeMcpProxyAsync();

// Map unified endpoint (aggregates all tools)
app.MapMcp("/mcp");

// Map per-server endpoints (isolated tools per backend)
app.MapPerServerMcpEndpoints();
// Creates: /mcp/calendar/tools/list, /mcp/mail/tools/list, etc.

await app.RunAsync();
```

Each per-server endpoint only returns tools, resources, and prompts from its own backend.
The unified `/mcp` endpoint still aggregates everything.

### When to Use SDK vs JSON Configuration

| Use Case | Recommended Approach |
|----------|---------------------|
| Static configuration | JSON file |
| Dynamic server discovery | SDK |
| Custom authentication logic | SDK |
| Runtime tool modification | SDK |
| Code-based hooks and interceptors | SDK |
| CI/CD deployments | JSON file |
| Integration with existing apps | SDK |

### SDK API Reference

#### IMcpProxyBuilder Methods

| Method | Description |
|--------|-------------|
| `WithServerInfo(name, version, instructions)` | Set proxy server metadata |
| `WithRouting(mode, basePath)` | Configure routing mode (Unified or PerServer) |
| `WithToolCaching(enabled, ttlSeconds)` | Configure tool list caching (default: 300s) |
| `AddStdioServer(name, command, args...)` | Add a local STDIO backend |
| `AddHttpServer(name, url)` | Add a remote HTTP backend |
| `AddSseServer(name, url)` | Add a remote SSE backend |
| `WithGlobalPreInvokeHook(hook)` | Add a pre-invoke hook for all servers |
| `WithGlobalPostInvokeHook(hook)` | Add a post-invoke hook for all servers |
| `WithGlobalHook(hook)` | Add a combined pre/post-invoke hook for all servers |
| `AddVirtualTool(tool, handler)` | Add a proxy-handled virtual tool (unified endpoint) |
| `WithGlobalVirtualToolsOnPerServerRoutes(enabled)` | Show global virtual tools on per-server routes |
| `WithToolInterceptor(interceptor)` | Intercept and modify tool lists |
| `WithToolCallInterceptor(interceptor)` | Intercept tool calls |
| `WithConfigurationFile(path)` | Merge with JSON configuration (SDK config takes priority) |

#### WebApplication Extension Methods

| Method | Description |
|--------|-------------|
| `InitializeMcpProxyAsync()` | Initialize backend connections and hook pipelines |
| `MapPerServerMcpEndpoints()` | Map per-server HTTP endpoints with isolated tools per backend |
| `UseOAuthMetadataProxy()` | Add OAuth metadata proxy middleware |
| `UseForwardAuthAuthentication()` | Add forward-authorization authentication middleware |

#### IServerBuilder Methods

| Method | Description |
|--------|-------------|
| `WithTitle(title)` | Set display name |
| `WithDescription(description)` | Set description |
| `WithEnvironment(dict)` | Set environment variables (STDIO) |
| `WithHeaders(dict)` | Set HTTP headers (HTTP/SSE) |
| `WithRoute(route)` | Set custom route path (PerServer mode) |
| `WithToolPrefix(prefix, separator)` | Add prefix to tool names |
| `WithResourcePrefix(prefix, separator)` | Add prefix to resource URIs |
| `WithPromptPrefix(prefix, separator)` | Add prefix to prompt names |
| `WithBackendAuth(type)` | Configure backend authentication type |
| `WithBackendAuth(type, configure)` | Configure Azure AD backend authentication |
| `AllowTools(patterns...)` | Only include matching tools |
| `DenyTools(patterns...)` | Exclude matching tools |
| `WithToolFilter(filter)` | Add a custom tool filter |
| `WithToolTransformer(transformer)` | Add a custom tool transformer |
| `WithPreInvokeHook(hook)` | Add server-specific pre-invoke hook |
| `WithPostInvokeHook(hook)` | Add server-specific post-invoke hook |
| `WithHook(hook)` | Add a combined pre/post-invoke hook |
| `AddVirtualTool(tool, handler)` | Add a virtual tool scoped to this server |
| `Enabled(bool)` | Enable/disable the server |
| `Build()` | Return to proxy builder |

### Hooks and Interceptors

The SDK provides lambda-based hooks for easy inline configuration:

```csharp
proxy
    // Global logging hook
    .OnPreInvoke(ctx =>
    {
        Console.WriteLine($"Calling: {ctx.Data.Name} on {ctx.ServerName}");
        ctx.Items["startTime"] = Stopwatch.StartNew();
        return ValueTask.CompletedTask;
    })
    // Global timing hook
    .OnPostInvoke((ctx, result) =>
    {
        var sw = (Stopwatch)ctx.Items["startTime"];
        Console.WriteLine($"Completed in {sw.ElapsedMilliseconds}ms");
        return ValueTask.FromResult(result);
    })
    // Tool list interceptor
    .InterceptTools(tools => tools
        .Where(t => !t.Tool.Name.Contains("deprecated")))
    // Tool call interceptor
    .InterceptToolCalls((context, ct) =>
    {
        if (context.ToolName == "proxy_status")
        {
            return ValueTask.FromResult<CallToolResult?>(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Healthy" }]
            });
        }
        return ValueTask.FromResult<CallToolResult?>(null);
    });
```

### Virtual Tools

Create tools that are handled directly by the proxy:

```csharp
proxy.AddTool(
    name: "proxy_info",
    description: "Get proxy information",
    handler: (request, ct) => ValueTask.FromResult(new CallToolResult
    {
        Content = [new TextContentBlock { Text = "MCP Proxy v1.0.0" }]
    }));

// With full input schema
proxy.AddVirtualTool(
    new Tool
    {
        Name = "calculate",
        Description = "Evaluate math expressions",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["expression"] = new JsonObject { ["type"] = "string" }
            }
        }
    },
    handler: (request, ct) =>
    {
        var expr = request.Arguments?["expression"]?.ToString();
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"Result: {Evaluate(expr)}" }]
        });
    });
```

### Tool Modification

Modify, rename, or remove tools from the aggregated list:

```csharp
proxy
    // Rename tools
    .RenameTool("filesystem_read_file", "read")
    // Remove by pattern
    .RemoveToolsByPattern("internal_*", "*_debug")
    // Remove by predicate
    .RemoveTools((tool, server) => tool.Name.StartsWith("admin_"))
    // Modify tools
    .ModifyTools(
        predicate: (tool, _) => true,
        modifier: tool => new Tool
        {
            Name = tool.Name,
            Description = $"[Proxied] {tool.Description}",
            InputSchema = tool.InputSchema
        });
```

### Combining SDK with JSON Configuration

SDK settings take precedence over JSON configuration:

```csharp
proxy
    .WithConfigurationFile("mcp-proxy.json")  // Load base config
    .AddStdioServer("custom", "my-server")    // Add SDK-only server
    .Build();
```

### SDK Samples

See the `/samples` directory for complete examples:

- **[11-sdk-basic](samples/11-sdk-basic)** - Basic SDK usage and DI integration
- **[12-sdk-hooks-interceptors](samples/12-sdk-hooks-interceptors)** - Hooks, interceptors, logging
- **[13-sdk-virtual-tools](samples/13-sdk-virtual-tools)** - Virtual tools and tool modification
- **[15-teams-integration](samples/15-teams-integration)** - Teams MCP integration with caching, forward-auth, user-auth, and `--log-token` discovery
- **[16-public-client-auth](samples/16-public-client-auth)** - Multi-backend interactive browser auth with credential sharing (Microsoft 365 Calendar, Mail, Copilot, Me)

### Low-Level API

For advanced scenarios, you can use the underlying classes directly:

```csharp
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Proxy;
using Microsoft.Extensions.Logging;

// Load configuration
var config = await ConfigurationLoader.LoadAsync("mcp-proxy.json");

// Create loggers
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var clientManagerLogger = loggerFactory.CreateLogger<McpClientManager>();
var proxyServerLogger = loggerFactory.CreateLogger<McpProxyServer>();

// Create and initialize client manager
var clientManager = new McpClientManager(clientManagerLogger);
await clientManager.InitializeAsync(config, cancellationToken);

// Create proxy server
var proxyServer = new McpProxyServer(proxyServerLogger, clientManager, config);

// Use the proxy server
var tools = await proxyServer.ListToolsCoreAsync(cancellationToken);
var result = await proxyServer.CallToolCoreAsync(callToolParams, cancellationToken);
```

## License

MIT
