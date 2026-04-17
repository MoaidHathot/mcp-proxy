# Public Client Auth - Microsoft 365 MCP Servers

This sample demonstrates how to use `InteractiveBrowser` authentication to proxy multiple Microsoft 365 MCP servers through a single MCP Proxy instance. The proxy authenticates as the current user using a pre-authorized public client ID (e.g., VS Code's app registration) and exposes each backend on its own HTTP endpoint.

No app registration is needed. One browser sign-in covers all backends.

## Architecture

```
                                                    ┌──────────────────────┐
                                              ┌────>│ Calendar MCP Server  │
                                              │     └──────────────────────┘
┌─────────────────┐     ┌────────────────┐    │     ┌──────────────────────┐
│   MCP Client    │────>│   MCP Proxy    │────┼────>│ M365 Copilot MCP     │
│                 │     │                │    │     └──────────────────────┘
│ (OpenCode,      │     │ /calendar      │    │     ┌──────────────────────┐
│  Claude, etc.)  │     │ /copilot       │    ├────>│ Mail MCP Server      │
└─────────────────┘     │ /mail          │    │     └──────────────────────┘
                        │ /me            │    │     ┌──────────────────────┐
                        └────────────────┘    └────>│ Me MCP Server        │
                          │                         └──────────────────────┘
                          │
                    InteractiveBrowser
                    auth (shared token
                    cache, one sign-in)
```

## Prerequisites

### 1. Discover the Public Client ID and Audience

You need three values that are specific to your Azure AD environment:

| Value | Description | How to discover |
|-------|-------------|-----------------|
| `PUBLIC_CLIENT_ID` | The pre-authorized public client app ID | Use [sample 15](../15-teams-integration) with `--log-token` to capture the `appid` claim from VS Code's token |
| `TENANT_ID` | Your Azure AD tenant ID | Same `--log-token` output (`tid` claim), or from the Azure Portal |
| `AZURE_AUDIENCE` | The M365 MCP service app ID | Same `--log-token` output (`aud` claim) |

To discover these values, run sample 15 in forward-auth mode with `--log-token`:

```bash
cd samples/15-teams-integration
dotnet run -- --tenant-id=your-tenant-id --log-token
```

Then connect VS Code to `http://localhost:5101/mcp`. The proxy logs the token claims:

```
═══════════════════════════════════════════════════════════════════════════
TOKEN CLAIMS (from VS Code's first authenticated request)
═══════════════════════════════════════════════════════════════════════════
  appid (Client ID):   aebc6443-...    <-- PUBLIC_CLIENT_ID
  aud   (Audience):    ea9ffc3e-...    <-- AZURE_AUDIENCE
  tid   (Tenant ID):   72f988bf-...    <-- TENANT_ID
═══════════════════════════════════════════════════════════════════════════
```

### 2. Set Environment Variables

```bash
# PowerShell
$env:TENANT_ID = "your-tenant-id"
$env:PUBLIC_CLIENT_ID = "your-public-client-id"
$env:AZURE_AUDIENCE = "your-audience-app-id"

# Bash
export TENANT_ID="your-tenant-id"
export PUBLIC_CLIENT_ID="your-public-client-id"
export AZURE_AUDIENCE="your-audience-app-id"
```

## Running

### HTTP Mode (per-server routing)

Each backend is exposed on its own endpoint:

```bash
cd samples/16-public-client-auth
mcpproxy -t http -c mcp-proxy.json -p 5200
```

Endpoints:

| URL | Backend |
|-----|---------|
| `http://localhost:5200/calendar` | Microsoft 365 Calendar |
| `http://localhost:5200/copilot` | Microsoft 365 Copilot |
| `http://localhost:5200/mail` | Microsoft 365 Mail |
| `http://localhost:5200/me` | Microsoft 365 Me / Profile |
| `http://localhost:5200/mcp/` | Discovery (lists all servers) |

### Stdio Mode

All tools from all backends are aggregated into a single stdio stream:

```bash
cd samples/16-public-client-auth
mcpproxy -t stdio -c mcp-proxy.json
```

Use `--server` / `-s` to select specific backend(s):

```bash
# Single server
mcpproxy -t stdio -c mcp-proxy.json -s calendar

# Multiple servers
mcpproxy -t stdio -c mcp-proxy.json -s calendar -s mail
```

## Client Configuration

### Stdio per-server (recommended for OpenCode / VS Code)

Use `-s` to run one proxy instance per backend. Each client entry launches its own
`mcpproxy` process with only one backend active:

**VS Code** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "m365-calendar": {
      "type": "stdio",
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "path/to/mcp-proxy.json", "-s", "calendar"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "PUBLIC_CLIENT_ID": "your-public-client-id",
        "AZURE_AUDIENCE": "your-audience-app-id"
      }
    },
    "m365-mail": {
      "type": "stdio",
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "path/to/mcp-proxy.json", "-s", "mail"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "PUBLIC_CLIENT_ID": "your-public-client-id",
        "AZURE_AUDIENCE": "your-audience-app-id"
      }
    },
    "m365-copilot": {
      "type": "stdio",
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "path/to/mcp-proxy.json", "-s", "copilot"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "PUBLIC_CLIENT_ID": "your-public-client-id",
        "AZURE_AUDIENCE": "your-audience-app-id"
      }
    },
    "m365-me": {
      "type": "stdio",
      "command": "mcpproxy",
      "args": ["-t", "stdio", "-c", "path/to/mcp-proxy.json", "-s", "me"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "PUBLIC_CLIENT_ID": "your-public-client-id",
        "AZURE_AUDIENCE": "your-audience-app-id"
      }
    }
  }
}
```

**OpenCode** (`opencode.json`):

```json
{
  "mcp": {
    "m365-calendar": {
      "type": "local",
      "command": ["mcpproxy", "-t", "stdio", "-c", "path/to/mcp-proxy.json", "-s", "calendar"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "PUBLIC_CLIENT_ID": "your-public-client-id",
        "AZURE_AUDIENCE": "your-audience-app-id"
      },
      "enabled": true
    },
    "m365-mail": {
      "type": "local",
      "command": ["mcpproxy", "-t", "stdio", "-c", "path/to/mcp-proxy.json", "-s", "mail"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "PUBLIC_CLIENT_ID": "your-public-client-id",
        "AZURE_AUDIENCE": "your-audience-app-id"
      },
      "enabled": true
    },
    "m365-copilot": {
      "type": "local",
      "command": ["mcpproxy", "-t", "stdio", "-c", "path/to/mcp-proxy.json", "-s", "copilot"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "PUBLIC_CLIENT_ID": "your-public-client-id",
        "AZURE_AUDIENCE": "your-audience-app-id"
      },
      "enabled": true
    },
    "m365-me": {
      "type": "local",
      "command": ["mcpproxy", "-t", "stdio", "-c", "path/to/mcp-proxy.json", "-s", "me"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "PUBLIC_CLIENT_ID": "your-public-client-id",
        "AZURE_AUDIENCE": "your-audience-app-id"
      },
      "enabled": true
    }
  }
}
```

### HTTP per-server routing (requires separate proxy startup)

Connect to individual endpoints as remote MCP servers. The proxy must be started
separately (see [HTTP Mode](#http-mode-per-server-routing) above).

**OpenCode** (`opencode.json`):

```json
{
  "mcp": {
    "m365-calendar": {
      "type": "remote",
      "url": "http://localhost:5200/calendar",
      "enabled": true
    },
    "m365-mail": {
      "type": "remote",
      "url": "http://localhost:5200/mail",
      "enabled": true
    },
    "m365-copilot": {
      "type": "remote",
      "url": "http://localhost:5200/copilot",
      "enabled": true
    },
    "m365-me": {
      "type": "remote",
      "url": "http://localhost:5200/me",
      "enabled": true
    }
  }
}
```

**VS Code** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "m365-calendar": {
      "type": "http",
      "url": "http://localhost:5200/calendar"
    },
    "m365-mail": {
      "type": "http",
      "url": "http://localhost:5200/mail"
    },
    "m365-copilot": {
      "type": "http",
      "url": "http://localhost:5200/copilot"
    },
    "m365-me": {
      "type": "http",
      "url": "http://localhost:5200/me"
    }
  }
}
```

## How Token Sharing Works

All four backends are configured with the same `credentialGroup: "m365"` in their auth
config. This tells the proxy to share a single `InteractiveBrowserCredential` instance
across all backends in the group, resulting in **one browser sign-in** regardless of how
many backends are configured.

```json
"auth": {
  "type": "InteractiveBrowser",
  "credentialGroup": "m365",
  "azureAd": { "..." }
}
```

Additionally, all four backends share the same:
- Public client ID (`PUBLIC_CLIENT_ID`)
- Tenant (`TENANT_ID`)
- Audience/scopes (`AZURE_AUDIENCE/.default`)
- Persistent token cache (default name: `mcp-proxy`)

This means:
1. **First backend** to connect triggers a browser sign-in (one prompt only)
2. **Remaining backends** reuse the same credential instance — no additional prompts
3. The token is cached in the OS credential store (Windows Credential Manager, macOS Keychain, etc.)
4. **Across restarts**, the cached refresh token is used silently (~90 day lifetime)
5. After expiry, the next request triggers a new browser sign-in automatically

If `credentialGroup` is omitted, each backend gets its own credential instance and may
trigger separate browser prompts.

## Adding More Servers

To add another Microsoft 365 MCP server, copy any server block and change the `url` and `route`.
Include the same `credentialGroup` to share the credential:

```json
"sharepoint": {
  "type": "http",
  "title": "Microsoft 365 SharePoint",
  "description": "SharePoint document and site management tools",
  "url": "https://agent365.svc.cloud.microsoft/agents/tenants/${TENANT_ID}/servers/mcp_SharePointServer",
  "route": "/sharepoint",
  "enabled": true,
  "auth": {
    "type": "InteractiveBrowser",
    "credentialGroup": "m365",
    "azureAd": {
      "clientId": "${PUBLIC_CLIENT_ID}",
      "tenantId": "${TENANT_ID}",
      "scopes": ["${AZURE_AUDIENCE}/.default"]
    }
  }
}
```

Servers with the same `credentialGroup` share a single credential. Servers with a
different group (or no group) get their own independent credentials.

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `TENANT_ID` | Yes | Azure AD tenant ID |
| `PUBLIC_CLIENT_ID` | Yes | Pre-authorized public client app ID (discover via sample 15's `--log-token`) |
| `AZURE_AUDIENCE` | Yes | Resource audience / app ID of the M365 MCP service (discover via `--log-token`) |

## See Also

- [Sample 15: Teams Integration](../15-teams-integration/) - Full Teams integration with caching, hooks, and `--log-token` discovery
- [Sample 09: Per-Server Routing](../09-per-server-routing/) - Per-server routing patterns
- [Sample 07: Azure AD Auth](../07-azure-ad-auth/) - Azure AD authentication for the proxy itself
