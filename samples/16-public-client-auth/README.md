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

## Client Configuration

### OpenCode (`opencode.json`)

Connect to individual endpoints as remote MCP servers:

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

### VS Code (`.vscode/mcp.json`)

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

All four backends share the same:
- Public client ID (`PUBLIC_CLIENT_ID`)
- Tenant (`TENANT_ID`)
- Audience/scopes (`AZURE_AUDIENCE/.default`)
- Persistent token cache (default name: `mcp-proxy`)

This means:
1. **First request** to any backend triggers a browser sign-in
2. The token is cached in the OS credential store (Windows Credential Manager, macOS Keychain, etc.)
3. **All subsequent requests** to any backend reuse the cached token silently
4. The cached refresh token is valid for approximately 90 days
5. After expiry, the next request triggers a new browser sign-in automatically

## Adding More Servers

To add another Microsoft 365 MCP server, copy any server block and change the `url` and `route`:

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
    "azureAd": {
      "clientId": "${PUBLIC_CLIENT_ID}",
      "tenantId": "${TENANT_ID}",
      "scopes": ["${AZURE_AUDIENCE}/.default"]
    }
  }
}
```

All servers sharing the same `clientId`, `tenantId`, and `scopes` will share the cached token automatically.

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
