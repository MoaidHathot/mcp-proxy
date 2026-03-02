---
layout: default
title: Configuration Reference
description: Complete configuration options for MCP Proxy
---

## Configuration Reference

MCP Proxy uses a JSON configuration file with support for comments and trailing commas. This page documents all available configuration options.

<div class="toc">
<h4>On this page</h4>
<ul>
<li><a href="#configuration-structure">Configuration Structure</a></li>
<li><a href="#proxy-settings">Proxy Settings</a></li>
<li><a href="#server-configuration">Server Configuration</a></li>
<li><a href="#environment-variables">Environment Variables</a></li>
<li><a href="#complete-example">Complete Example</a></li>
</ul>
</div>

## Configuration Structure

The configuration file has two main sections:

```json
{
  "proxy": {
    // Proxy-level settings
  },
  "mcp": {
    // Backend MCP server definitions
  }
}
```

## Proxy Settings

### Server Info

Configure how the proxy identifies itself to clients:

```json
{
  "proxy": {
    "serverInfo": {
      "name": "My MCP Proxy",
      "version": "1.0.0",
      "instructions": "This proxy aggregates multiple MCP servers."
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `name` | string | `"MCP Proxy"` | Server name exposed to clients |
| `version` | string | `"1.0.0"` | Server version |
| `instructions` | string | `null` | Instructions sent to the client |

### Routing

Configure how servers are exposed via HTTP:

```json
{
  "proxy": {
    "routing": {
      "mode": "unified",
      "basePath": "/mcp"
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `mode` | string | `"unified"` | `"unified"` (all servers on one endpoint) or `"perServer"` (each server on its own route) |
| `basePath` | string | `"/mcp"` | Base path for MCP endpoints |

### Authentication

Configure authentication for HTTP endpoints:

```json
{
  "proxy": {
    "authentication": {
      "enabled": true,
      "type": "apiKey",
      "apiKey": {
        "header": "X-API-Key",
        "queryParameter": "api_key",
        "value": "env:MCP_PROXY_API_KEY"
      }
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Enable authentication |
| `type` | string | `"none"` | `"none"`, `"apiKey"`, `"bearer"`, or `"azureAd"` |

**API Key Authentication:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `apiKey.header` | string | `"X-API-Key"` | Header name for API key |
| `apiKey.queryParameter` | string | `null` | Query parameter fallback |
| `apiKey.value` | string | - | Expected API key (supports `env:VAR`) |

**Bearer Token Authentication:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `bearer.authority` | string | - | JWT authority URL |
| `bearer.audience` | string | - | Expected audience |
| `bearer.validateIssuer` | bool | `true` | Whether to validate the token issuer |
| `bearer.validateAudience` | bool | `true` | Whether to validate the token audience |
| `bearer.validIssuers` | string[] | `null` | List of valid issuers (alternative to using authority) |

**Azure AD (Microsoft Entra ID) Authentication:**

Azure AD authentication provides enterprise-grade authentication using Microsoft Entra ID (formerly Azure Active Directory). It automatically fetches and caches JWKS keys from Azure AD for token validation.

```json
{
  "proxy": {
    "authentication": {
      "enabled": true,
      "type": "azureAd",
      "azureAd": {
        "tenantId": "your-tenant-id",
        "clientId": "your-application-client-id",
        "audience": "api://your-application-client-id",
        "requiredScopes": ["access_as_user"],
        "requiredRoles": ["MCP.User"]
      }
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `azureAd.instance` | string | `"https://login.microsoftonline.com/"` | Azure AD instance URL |
| `azureAd.tenantId` | string | - | Azure AD tenant ID or domain (use `"common"` for multi-tenant) |
| `azureAd.clientId` | string | - | Application (client) ID registered in Azure AD |
| `azureAd.audience` | string | `clientId` | Expected audience for token validation |
| `azureAd.validIssuers` | string[] | `null` | Valid issuers (defaults to Azure AD issuers for tenant) |
| `azureAd.validateIssuer` | bool | `true` | Whether to validate the token issuer |
| `azureAd.validateAudience` | bool | `true` | Whether to validate the token audience |
| `azureAd.requiredScopes` | string[] | `null` | Required OAuth scopes (token must contain at least one) |
| `azureAd.requiredRoles` | string[] | `null` | Required app roles (token must contain at least one) |

**OAuth 2.0 Metadata Discovery:**

When authentication is enabled, the proxy exposes OAuth 2.0 metadata discovery endpoints following RFC 8414:
- `/.well-known/oauth-authorization-server` - OAuth 2.0 Authorization Server Metadata
- `/.well-known/openid-configuration` - OpenID Connect Discovery

MCP clients can use these endpoints to discover how to authenticate with the proxy.

### Logging

Configure request/response logging:

```json
{
  "proxy": {
    "logging": {
      "logRequests": true,
      "logResponses": false,
      "sensitiveDataMask": true
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `logRequests` | bool | `true` | Log incoming requests |
| `logResponses` | bool | `false` | Log outgoing responses |
| `sensitiveDataMask` | bool | `true` | Mask sensitive data in logs |

### Caching

Configure tool list caching:

```json
{
  "proxy": {
    "caching": {
      "tools": {
        "enabled": true,
        "ttlSeconds": 60
      }
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `caching.tools.enabled` | bool | `true` | Enable tool list caching |
| `caching.tools.ttlSeconds` | int | `60` | Cache TTL in seconds |

### Capabilities

Configure MCP capabilities:

```json
{
  "proxy": {
    "capabilities": {
      "client": {
        "sampling": true,
        "elicitation": true,
        "roots": false,
        "experimental": {
          "customFeature": { "enabled": true }
        }
      },
      "server": {
        "experimental": {
          "proxyFeature": { "supported": true }
        }
      }
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `client.sampling` | bool | `true` | Enable sampling capability |
| `client.elicitation` | bool | `true` | Enable elicitation capability |
| `client.roots` | bool | `true` | Enable roots capability |
| `client.experimental` | object | `{}` | Experimental client capabilities |
| `server.experimental` | object | `{}` | Experimental server capabilities |

### Telemetry

Configure OpenTelemetry integration:

```json
{
  "proxy": {
    "telemetry": {
      "enabled": true,
      "serviceName": "my-mcp-proxy",
      "serviceVersion": "1.0.0",
      "metrics": {
        "enabled": true,
        "consoleExporter": false,
        "otlpEndpoint": "http://localhost:4317"
      },
      "tracing": {
        "enabled": true,
        "consoleExporter": false,
        "otlpEndpoint": "http://localhost:4317"
      }
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Enable telemetry |
| `serviceName` | string | `"McpProxy"` | Service name for telemetry |
| `serviceVersion` | string | `"1.0.0"` | Service version |
| `metrics.enabled` | bool | `true` | Enable metrics |
| `metrics.consoleExporter` | bool | `false` | Export to console (debugging) |
| `metrics.otlpEndpoint` | string | `null` | OTLP endpoint URL |
| `tracing.enabled` | bool | `true` | Enable tracing |
| `tracing.consoleExporter` | bool | `false` | Export to console (debugging) |
| `tracing.otlpEndpoint` | string | `null` | OTLP endpoint URL |

## Server Configuration

Each entry in the `mcp` object defines a backend MCP server.

### STDIO Backend

Run a local process as an MCP server:

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "title": "My Server",
      "description": "A local MCP server",
      "command": "node",
      "arguments": ["server.js", "--port", "3000"],
      "environment": {
        "API_KEY": "env:MY_API_KEY",
        "DEBUG": "true"
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
| `environment` | object | No | Environment variables |
| `enabled` | bool | No | Enable/disable server (default: `true`) |

### HTTP Backend

Connect to a remote HTTP MCP server:

```json
{
  "mcp": {
    "remote-server": {
      "type": "http",
      "title": "Remote Server",
      "url": "https://example.com/mcp",
      "headers": {
        "Authorization": "Bearer ${API_TOKEN}",
        "X-Custom-Header": "value"
      }
    }
  }
}
```

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `type` | string | Yes | `"http"` for Streamable HTTP (auto-detects SSE) |
| `title` | string | No | Display name |
| `description` | string | No | Server description |
| `url` | string | Yes | Server URL |
| `headers` | object | No | Custom headers |
| `enabled` | bool | No | Enable/disable server (default: `true`) |

### SSE Backend

Connect to an SSE-only MCP server:

```json
{
  "mcp": {
    "sse-server": {
      "type": "sse",
      "title": "SSE Server",
      "url": "https://example.com/sse"
    }
  }
}
```

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `type` | string | Yes | `"sse"` for SSE-only servers |
| `title` | string | No | Display name |
| `url` | string | Yes | SSE endpoint URL |
| `headers` | object | No | Custom headers |
| `enabled` | bool | No | Enable/disable server |

> **Note**: Use `"sse"` for servers that only support Server-Sent Events. Use `"http"` for servers that support the newer Streamable HTTP transport (it auto-detects and falls back to SSE).

### Backend Authentication

Configure Azure AD authentication for connecting to backend MCP servers that require OAuth tokens:

```json
{
  "mcp": {
    "azure-api": {
      "type": "http",
      "title": "Azure API Server",
      "url": "https://my-mcp-server.azurewebsites.net/mcp",
      "auth": {
        "type": "AzureAdClientCredentials",
        "azureAd": {
          "tenantId": "your-tenant-id",
          "clientId": "your-client-id",
          "clientSecret": "env:AZURE_CLIENT_SECRET",
          "scopes": ["api://backend-api/.default"]
        }
      }
    }
  }
}
```

**Authentication Types:**

| Type | Description |
|------|-------------|
| `None` | No authentication (default) |
| `AzureAdClientCredentials` | App-to-app authentication using client credentials flow |
| `AzureAdOnBehalfOf` | User delegation using on-behalf-of flow |
| `AzureAdManagedIdentity` | Azure managed identity (system or user-assigned) |

**Azure AD Configuration Options:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `azureAd.instance` | string | `"https://login.microsoftonline.com/"` | Azure AD instance URL |
| `azureAd.tenantId` | string | - | Azure AD tenant ID |
| `azureAd.clientId` | string | - | Application (client) ID (required for client credentials) |
| `azureAd.clientSecret` | string | - | Client secret (supports `env:VAR`) |
| `azureAd.certificatePath` | string | - | Path to certificate file (alternative to secret) |
| `azureAd.certificateThumbprint` | string | - | Certificate thumbprint in store (alternative to secret) |
| `azureAd.scopes` | string[] | `[".default"]` | OAuth scopes to request |
| `azureAd.managedIdentityClientId` | string | - | Client ID for user-assigned managed identity |

**Client Credentials Example:**

```json
{
  "auth": {
    "type": "AzureAdClientCredentials",
    "azureAd": {
      "tenantId": "contoso.onmicrosoft.com",
      "clientId": "00000000-0000-0000-0000-000000000000",
      "clientSecret": "env:BACKEND_CLIENT_SECRET",
      "scopes": ["api://backend-server/.default"]
    }
  }
}
```

**On-Behalf-Of Flow Example:**

The OBO flow allows the proxy to call backend servers on behalf of the authenticated user:

```json
{
  "auth": {
    "type": "AzureAdOnBehalfOf",
    "azureAd": {
      "tenantId": "contoso.onmicrosoft.com",
      "clientId": "00000000-0000-0000-0000-000000000000",
      "clientSecret": "env:BACKEND_CLIENT_SECRET",
      "scopes": ["api://backend-server/access_as_user"]
    }
  }
}
```

> **Note**: On-Behalf-Of requires that the inbound request to the proxy is authenticated with Azure AD.

**Managed Identity Example:**

```json
{
  "auth": {
    "type": "AzureAdManagedIdentity",
    "azureAd": {
      "scopes": ["api://backend-server/.default"]
    }
  }
}
```

For user-assigned managed identity:

```json
{
  "auth": {
    "type": "AzureAdManagedIdentity",
    "azureAd": {
      "managedIdentityClientId": "00000000-0000-0000-0000-000000000000",
      "scopes": ["api://backend-server/.default"]
    }
  }
}
```

**Certificate-Based Authentication:**

```json
{
  "auth": {
    "type": "AzureAdClientCredentials",
    "azureAd": {
      "tenantId": "contoso.onmicrosoft.com",
      "clientId": "00000000-0000-0000-0000-000000000000",
      "certificatePath": "/path/to/certificate.pfx",
      "scopes": ["api://backend-server/.default"]
    }
  }
}
```

Or load from certificate store:

```json
{
  "auth": {
    "type": "AzureAdClientCredentials",
    "azureAd": {
      "tenantId": "contoso.onmicrosoft.com",
      "clientId": "00000000-0000-0000-0000-000000000000",
      "certificateThumbprint": "ABC123DEF456...",
      "scopes": ["api://backend-server/.default"]
    }
  }
}
```

### Tool Configuration

Configure filtering and prefixing for tools:

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "tools": {
        "prefix": "myserver",
        "prefixSeparator": "_",
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

See [Features - Filtering]({{ '/features/#filtering' | relative_url }}) for more details.

### Resource Configuration

Configure filtering and prefixing for resources:

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "resources": {
        "prefix": "myserver",
        "prefixSeparator": "://",
        "filter": {
          "mode": "denylist",
          "patterns": ["internal:*"]
        }
      }
    }
  }
}
```

### Prompt Configuration

Configure filtering and prefixing for prompts:

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "prompts": {
        "prefix": "myserver",
        "prefixSeparator": "_",
        "filter": {
          "mode": "none"
        }
      }
    }
  }
}
```

### Hooks

Configure pre and post-invoke hooks:

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "hooks": {
        "preInvoke": [
          { "type": "logging", "config": { "logLevel": "debug" } }
        ],
        "postInvoke": [
          { "type": "outputTransform", "config": { "redactPatterns": ["password"] } }
        ]
      }
    }
  }
}
```

See [Features - Hooks]({{ '/features/#hooks' | relative_url }}) for more details.

### Per-Server Routing

Expose servers on different HTTP endpoints:

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
      "command": "fs-server",
      "route": "/fs"
    }
  }
}
```

With `perServer` routing:
- GitHub tools: `/mcp/github/tools/list`, `/mcp/github/tools/call`
- Filesystem tools: `/mcp/fs/tools/list`, `/mcp/fs/tools/call`

## Environment Variables

### Substitution Syntax

MCP Proxy supports two forms of environment variable substitution:

**1. Full value replacement** (`env:VAR_NAME`):

```json
{
  "environment": {
    "API_KEY": "env:MY_API_KEY"
  }
}
```

**2. Embedded substitution** (`${VAR_NAME}`):

```json
{
  "headers": {
    "Authorization": "Bearer ${API_TOKEN}"
  }
}
```

### Configuration Path

The configuration file path can be set via:

1. Command-line argument: `mcpproxy -t stdio -c ./mcp-proxy.json`
2. Environment variable: `MCP_PROXY_CONFIG_PATH`

## Complete Example

```json
{
  // Proxy settings
  "proxy": {
    "serverInfo": {
      "name": "My MCP Proxy",
      "version": "1.0.0",
      "instructions": "Aggregated MCP endpoint"
    },
    "routing": {
      "mode": "unified",
      "basePath": "/mcp"
    },
    "authentication": {
      "enabled": false
    },
    "caching": {
      "tools": {
        "enabled": true,
        "ttlSeconds": 60
      }
    },
    "telemetry": {
      "enabled": true,
      "serviceName": "my-mcp-proxy",
      "metrics": {
        "enabled": true,
        "otlpEndpoint": "http://localhost:4317"
      },
      "tracing": {
        "enabled": true,
        "otlpEndpoint": "http://localhost:4317"
      }
    }
  },
  
  // Backend servers
  "mcp": {
    // Local filesystem server with prefix and filtering
    "filesystem": {
      "type": "stdio",
      "title": "Filesystem",
      "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "/workspace"],
      "tools": {
        "prefix": "fs",
        "filter": {
          "mode": "denylist",
          "patterns": ["delete_*", "remove_*"]
        }
      },
      "hooks": {
        "preInvoke": [
          { "type": "logging", "config": { "logLevel": "debug" } }
        ]
      }
    },
    
    // Remote Context7 documentation
    "context7": {
      "type": "sse",
      "title": "Context7",
      "url": "https://mcp.context7.com/sse",
      "tools": {
        "prefix": "c7"
      }
    },
    
    // GitHub MCP with authentication
    "github": {
      "type": "http",
      "title": "GitHub",
      "url": "https://github-mcp.example.com",
      "headers": {
        "Authorization": "Bearer ${GITHUB_TOKEN}"
      },
      "tools": {
        "prefix": "gh"
      }
    },
    
    // Disabled server (not loaded)
    "experimental": {
      "type": "stdio",
      "command": "experimental-server",
      "enabled": false
    }
  }
}
```
