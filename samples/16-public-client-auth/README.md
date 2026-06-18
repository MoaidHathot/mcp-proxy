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
                        │ /teams         │    ├────>│ Me MCP Server        │
                        └────────────────┘    │     └──────────────────────┘
                          │                   │     ┌──────────────────────┐
                          │                   └────>│ Teams (sample 15)    │
                          │                         │ - cache              │
                    InteractiveBrowser              │ - hooks              │
                    auth (shared token              │ - virtual tools      │
                    cache, one sign-in)             │ - credential scan    │
                                                    └──────────────────────┘
                                                       (spawned via `dnx`,
                                                        stdio child process)
```

The four HTTP backends (Calendar / Copilot / Mail / Me) use `InteractiveBrowser` directly. The Teams backend is provided by [sample 15](../15-teams-integration) running as a stdio child process via `dnx`. Sample 15 itself uses `InteractiveBrowser` outbound to the Teams MCP Server and adds caching, hooks, virtual tools, and credential scanning. All five backends share a single persistent token cache, so one browser sign-in covers them all.

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
| `http://localhost:5200/mcp/calendar` | Microsoft 365 Calendar |
| `http://localhost:5200/mcp/copilot` | Microsoft 365 Copilot |
| `http://localhost:5200/mcp/mail` | Microsoft 365 Mail |
| `http://localhost:5200/mcp/me` | Microsoft 365 Me / Profile |
| `http://localhost:5200/mcp/teams` | Microsoft 365 Teams (sample 15, with cache/hooks/virtual tools) |
| `http://localhost:5200/mcp/` | Discovery (lists all servers) |

Endpoints follow the pattern `{basePath}/{serverName}`. The `basePath` defaults to `/mcp`
and can be changed in the config. Individual servers can override their route with the
`route` property.

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
      "url": "http://localhost:5200/mcp/calendar",
      "enabled": true
    },
    "m365-mail": {
      "type": "remote",
      "url": "http://localhost:5200/mcp/mail",
      "enabled": true
    },
    "m365-copilot": {
      "type": "remote",
      "url": "http://localhost:5200/mcp/copilot",
      "enabled": true
    },
    "m365-me": {
      "type": "remote",
      "url": "http://localhost:5200/mcp/me",
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
      "url": "http://localhost:5200/mcp/calendar"
    },
    "m365-mail": {
      "type": "http",
      "url": "http://localhost:5200/mcp/mail"
    },
    "m365-copilot": {
      "type": "http",
      "url": "http://localhost:5200/mcp/copilot"
    },
    "m365-me": {
      "type": "http",
      "url": "http://localhost:5200/mcp/me"
    }
  }
}
```

## Config Path

The `--config` / `-c` option supports environment variable expansion in the path.
This is useful when the config file is stored in a shared location:

```bash
# Unix-style ${VAR}
mcpproxy -t http -c '${XDG_CONFIG_HOME}/orchestra/m365.proxy.json'

# Home directory shorthand
mcpproxy -t http -c '~/orchestra/m365.proxy.json'

# Windows-style %VAR%
mcpproxy -t http -c '%USERPROFILE%/orchestra/m365.proxy.json'
```

Note: use single quotes in PowerShell to prevent the shell from expanding `${VAR}`
before the proxy sees it.

## How Credential Sharing and Deferred Connection Work

### Credential Sharing (`credentialGroup`)

All four backends are configured with the same `credentialGroup: "m365"` in their auth
config. This tells the proxy to share a single `InteractiveBrowserCredential` instance
across all backends in the group, resulting in **one browser sign-in** regardless of how
many backends are configured.

If `credentialGroup` is omitted, each backend gets its own credential instance and may
trigger separate browser prompts.

### Deferred Connection (`deferConnection`)

When `deferConnection` is set to `true`, the proxy does not connect to the backend at
startup. Instead, it connects lazily on the first client request to that backend's
endpoint. This avoids triggering interactive browser authentication before the user
actually needs a specific backend.

When `false` (the default), all backends connect eagerly during proxy startup.

### Combined configuration

```json
"auth": {
  "type": "InteractiveBrowser",
  "credentialGroup": "m365",
  "deferConnection": true,
  "azureAd": { "..." }
}
```

With both options enabled:

1. **Proxy starts** instantly — no backend connections, no browser prompts
2. **First request** to e.g. `/mcp/calendar/tools/call` triggers the `calendar` backend connection
3. Browser sign-in opens (if no cached token) — one prompt only
4. **Subsequent requests** to other backends (e.g. `/mcp/mail`) connect lazily but reuse
   the shared credential — no additional browser prompts
5. The token is cached in the OS credential store (Windows Credential Manager, macOS Keychain, etc.)
6. **Across restarts**, the cached refresh token is used silently (~90 day lifetime)
7. After expiry, the next access to any backend automatically opens a fresh browser sign-in

### Credential expiry and re-authentication

Because all four backends share `credentialGroup: "m365"`, they share one token. When the
refresh token finally expires, the **next** access to any of them automatically opens a
browser sign-in (`InteractiveBrowserCredential` does this on the first request after the
cached token can no longer be refreshed silently). One sign-in re-authenticates the whole group.

If sign-in cannot complete (browser closed/timed out), the proxy **surfaces the error**
instead of silently returning zero tools:

- `tools/list` for the expired group returns an explicit authentication error rather than
  an empty list.
- Calling a tool whose backend is blocked on auth returns the real sign-in error, not
  "tool not found".
- A healthy credential group is unaffected — only the expired group surfaces an error.

This is the default (`proxy.backendErrors.onAuthFailure: "surface"`). Set it to `"skip"`
to restore the older behavior of silently omitting the affected backend's tools. Because
this sample co-locates the proxy with the user (stdio or local HTTP), the browser opens on
the user's machine.

### Token sharing across backends

All four backends share the same:
- Public client ID (`PUBLIC_CLIENT_ID`)
- Tenant (`TENANT_ID`)
- Audience/scopes (`AZURE_AUDIENCE/.default`)
- Persistent token cache (default name: `mcp-proxy`)

## Teams Backend via Sample 15

This sample pre-wires a 5th backend named `teams` that is **not** an HTTP backend like the other four — it is a stdio child process spawned from [sample 15](../15-teams-integration). Sample 15 itself uses `InteractiveBrowser` outbound to the Teams MCP Server and adds caching, hooks, virtual tools, and credential scanning. From this sample's point of view it is just another stdio backend; the cache/hooks/virtual tools are entirely an implementation detail of sample 15.

### Prerequisites

The `teams-mcp` tool must be installable via `dnx`. Either:

- **From nuget.org** (once published) — `dnx` resolves `McpProxy.Samples.Teams.Mcp@1.18.0` automatically.
- **Local build** — run `./pack.ps1` from the repo root to produce `artifacts/McpProxy.Samples.Teams.Mcp.<version>.nupkg`, then add the local feed when launching:

  ```bash
  dnx --add-source ./artifacts McpProxy.Samples.Teams.Mcp@1.18.0 --yes -- --help
  ```

  (or invoke `mcpproxy` with `DOTNET_ADD_NUGET_SOURCE` configured so the child `dnx` resolves locally — both are supported by `dnx`).

### How it shares the browser sign-in

Sample 16 already passes `--token-cache-name=mcp-proxy` and `AZURE_SCOPES=${AZURE_AUDIENCE}/.default` to sample 15. Together with the matching `${PUBLIC_CLIENT_ID}` and `${TENANT_ID}`, this aligns:

- The MSAL persistent cache name (so the cached refresh token is reused)
- The Azure AD app / tenant / audience (so a token minted for one backend is reusable for the others, or at minimum a new access token can be acquired silently from the shared refresh token)

The first `teams_*` invocation triggers a browser sign-in. After that, both sample 16's HTTP backends and sample 15's outbound Teams calls reuse the cached token, and across restarts the refresh token in the OS credential store yields silent sign-in for ~90 days.

### Disabling the Teams backend

If you do not need Teams, set `enabled: false` on the `teams` entry in `mcp-proxy.json` — or remove it entirely. The other four backends keep working unchanged.

## Adding More Servers

To add another Microsoft 365 MCP server, copy any server block and change the `url`.
Include the same `credentialGroup` to share the credential:

```json
"sharepoint": {
  "type": "http",
  "title": "Microsoft 365 SharePoint",
  "description": "SharePoint document and site management tools",
  "url": "https://agent365.svc.cloud.microsoft/agents/tenants/${TENANT_ID}/servers/mcp_SharePointServer",
  "enabled": true,
  "auth": {
    "type": "InteractiveBrowser",
    "credentialGroup": "m365",
    "deferConnection": true,
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

The `teams` backend (provided by sample 15 via `dnx`) inherits these three environment variables automatically and converts them into its own CLI flags via the `mcp-proxy.json` `arguments` array. See [sample 15's README](../15-teams-integration/README.md#use-as-a-backend-of-another-mcp-proxy) for the full list of flags sample 15 accepts when used as a backend.

## Auth Configuration Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `type` | string | - | Auth type. Use `InteractiveBrowser` for user-delegated browser auth |
| `credentialGroup` | string | `null` | Group name for credential sharing. Backends with the same group share one credential instance |
| `deferConnection` | bool | `false` | When `true`, defer backend connection until the first client request (lazy auth) |
| `azureAd.clientId` | string | - | Public client app ID (required for `InteractiveBrowser`) |
| `azureAd.tenantId` | string | - | Azure AD tenant ID |
| `azureAd.scopes` | string[] | `[".default"]` | OAuth scopes to request |
| `azureAd.tokenCacheName` | string | `"mcp-proxy"` | Name for the persistent token cache in the OS credential store |
| `azureAd.redirectUri` | string | `"http://localhost"` | Redirect URI for the OAuth flow |

## See Also

- [Sample 15: Teams Integration](../15-teams-integration/) - Full Teams integration with caching, hooks, and `--log-token` discovery
- [Sample 09: Per-Server Routing](../09-per-server-routing/) - Per-server routing patterns
- [Sample 07: Azure AD Auth](../07-azure-ad-auth/) - Azure AD authentication for the proxy itself
