# Teams Integration Sample

This sample demonstrates how to use MCP-Proxy's SDK features (hooks, interceptors, and virtual tools) to create a rich Microsoft Teams integration layer.

## Authentication Modes

This sample supports two authentication modes:

| Mode | Transport | Auth Handling | Best For |
|------|-----------|---------------|----------|
| **forward-auth** (default) | HTTP/SSE | VS Code authenticates with Azure AD, proxy forwards auth header | Interactive use, user-specific permissions |
| **proxy-auth** | stdio | Proxy authenticates with Azure AD using app credentials | Automated agents, scripts, simplified client setup |

### Forward-Auth Mode (Default)

```
VS Code ──(HTTP + OAuth)──> Proxy ──(forwarded auth)──> Teams MCP Server
```

- VS Code handles Azure AD authentication (browser popup)
- Proxy forwards the `Authorization` header to Teams MCP Server
- Each user authenticates with their own credentials
- User-specific permissions and data access

### Proxy-Auth Mode

```
Client ──(stdio, no auth)──> Proxy ──(Azure AD app auth)──> Teams MCP Server
```

- Proxy authenticates using Azure AD Client Credentials flow
- Clients connect via stdio without any authentication
- Single app identity for all requests
- Simpler client configuration, suitable for automation

## Features

### Automatic Caching
- **File-backed JSON persistence** - Cache survives restarts
- **TTL-based freshness** - 4-hour default, configurable
- **Tiered lookup** - Recent contacts → Full cache → Built-in (self-chat)
- **Automatic population** - Cache is updated from MCP responses

### Cache Short-Circuiting
- **Interceptor-based** - Returns cached data without MCP calls
- **Transparent to LLM** - Results include `fromCache: true` flag
- **Respects TTL** - Only short-circuits when cache is fresh

### Security
- **Credential scanning** - Blocks messages containing API keys, tokens, passwords
- **Configurable** - Block or warn-only mode
- **Multiple patterns** - AWS keys, JWT tokens, connection strings, etc.

### Automatic Pagination
- **Enforced limits** - ListChats/ListTeams limited to 20 items
- **Automatic injection** - Adds `top` parameter if not specified
- **Capping** - Reduces oversized requests to safe limits

### Virtual Tools
- **teams_resolve** - Unified resolver for people, chats, teams, channels
- **teams_lookup_*** - Direct ID-based lookups
- **teams_cache_*** - Cache management
- **teams_parse_url** - Extract IDs from Teams URLs
- **teams_validate_message** - Pre-send credential check

## Running the Sample

### Prerequisites

1. Your Azure AD tenant ID
2. For **forward-auth mode**: VS Code with the MCP extension
3. For **proxy-auth mode**: An Azure AD app registration with client credentials

### Forward-Auth Mode (Default)

This mode requires VS Code to handle OAuth authentication.

```bash
cd samples/15-teams-integration

# Using environment variables
$env:TENANT_ID = "your-tenant-id"
dotnet run

# Or using command line arguments
dotnet run -- --tenant-id=your-tenant-id

# Optional: customize the port (default: 5100)
dotnet run -- --tenant-id=your-tenant-id --port=5200
```

**VS Code Configuration** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "teams-proxy": {
      "type": "http",
      "url": "http://localhost:5100/mcp"
    }
  }
}
```

### Proxy-Auth Mode

This mode uses Azure AD Client Credentials flow - the proxy authenticates with an app identity.

#### Azure AD App Setup

1. Register an app in Azure AD ([Azure Portal](https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade))
2. Create a client secret (Certificates & secrets → New client secret)
3. Grant API permissions:
   - Microsoft Graph → Application permissions → relevant permissions for Teams
   - Admin consent may be required
4. Note the Application (client) ID and tenant ID

#### Running

```bash
cd samples/15-teams-integration

# Using environment variables (recommended)
$env:TENANT_ID = "your-tenant-id"
$env:AUTH_MODE = "proxy-auth"
$env:AZURE_CLIENT_ID = "your-app-client-id"
$env:AZURE_CLIENT_SECRET = "your-client-secret"
dotnet run

# Or using command line arguments
dotnet run -- --auth-mode=proxy-auth --tenant-id=your-tenant-id --client-id=your-app-id --client-secret=your-secret

# Optional: customize scopes (default: https://graph.microsoft.com/.default)
$env:AZURE_SCOPES = "api://your-api/.default"
dotnet run
```

**VS Code Configuration** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "teams-proxy": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "path/to/samples/15-teams-integration"],
      "env": {
        "TENANT_ID": "your-tenant-id",
        "AUTH_MODE": "proxy-auth",
        "AZURE_CLIENT_ID": "your-app-client-id",
        "AZURE_CLIENT_SECRET": "your-client-secret"
      }
    }
  }
}
```

## Environment Variables

| Variable | Required | Mode | Description |
|----------|----------|------|-------------|
| `TENANT_ID` | Yes | Both | Azure AD tenant ID |
| `AUTH_MODE` | No | Both | `forward-auth` (default) or `proxy-auth` |
| `PORT` | No | forward-auth | HTTP port (default: 5100) |
| `AZURE_CLIENT_ID` | Yes | proxy-auth | App registration client ID |
| `AZURE_CLIENT_SECRET` | Yes | proxy-auth | App registration client secret |
| `AZURE_SCOPES` | No | proxy-auth | Comma-separated scopes (default: `https://graph.microsoft.com/.default`) |

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `CacheFilePath` | `%LOCALAPPDATA%/mcp-proxy/teams-cache.json` | Cache file location |
| `CacheTtl` | 4 hours | Time-to-live for cache entries |
| `MaxRecentContacts` | 50 | Size of recent contacts list |
| `DefaultPaginationLimit` | 20 | Max items per list request |
| `EnableCredentialScanning` | true | Scan outbound messages |
| `BlockCredentials` | true | Block or just warn |
| `EnableMessagePrefix` | false | Add prefix to messages |
| `MessagePrefix` | "[AI]" | Prefix text |
| `EnableCachePopulation` | true | Auto-populate from responses |
| `EnableCacheShortCircuit` | true | Return cached data |
| `EnableAutoPagination` | true | Enforce pagination limits |
| `RegisterVirtualTools` | true | Add virtual tools |

## Virtual Tools Reference

### teams_resolve
Unified resolver for all Teams entities.

```json
{
  "query": "John Smith",
  "type": "person"  // optional: any, person, chat, team, channel
}
```

### teams_parse_url
Extract IDs from Teams URLs.

```json
{
  "url": "https://teams.microsoft.com/l/chat/19:abc...@thread.v2"
}
```

### teams_validate_message
Pre-check message for credentials.

```json
{
  "message": "Here's the API key: sk-abc123..."
}
```

## Architecture

### Forward-Auth Mode

```
┌─────────────────────────────────────────────────────────────┐
│                      VS Code / LLM                           │
│                                                              │
│  1. Fetches /.well-known/oauth-authorization-server         │
│  2. Receives OAuth metadata (proxied from Teams Server)     │
│  3. Authenticates with Azure AD (browser popup)             │
│  4. Sends requests with Authorization header                │
└───────────────────────────┬─────────────────────────────────┘
                            │ HTTP with Authorization header
┌───────────────────────────▼─────────────────────────────────┐
│              MCP Proxy (localhost:5100/mcp)                  │
│  ┌─────────────────────────────────────────────────────┐    │
│  │         OAuthMetadataProxyMiddleware                 │    │
│  │    (proxies /.well-known/* from Teams Server)        │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │         ForwardAuthorizationHandler                  │    │
│  │    (forwards incoming Authorization header)          │    │
│  └─────────────────────────────────────────────────────┘    │
│                        ... hooks, cache, etc ...             │
└───────────────────────────┬─────────────────────────────────┘
                            │ SSE with forwarded Authorization
┌───────────────────────────▼─────────────────────────────────┐
│                    Teams MCP Server                          │
└─────────────────────────────────────────────────────────────┘
```

### Proxy-Auth Mode

```
┌─────────────────────────────────────────────────────────────┐
│                      VS Code / LLM                           │
│                                                              │
│  Connects via stdio - no authentication required             │
└───────────────────────────┬─────────────────────────────────┘
                            │ stdio (no auth)
┌───────────────────────────▼─────────────────────────────────┐
│                   MCP Proxy (stdio)                          │
│  ┌─────────────────────────────────────────────────────┐    │
│  │         AzureAdCredentialProvider                    │    │
│  │    (acquires tokens using client credentials)        │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │         AzureAdAuthorizationHandler                  │    │
│  │    (adds Bearer token to outbound requests)          │    │
│  └─────────────────────────────────────────────────────┘    │
│                        ... hooks, cache, etc ...             │
└───────────────────────────┬─────────────────────────────────┘
                            │ SSE with Bearer token
┌───────────────────────────▼─────────────────────────────────┐
│                    Teams MCP Server                          │
└─────────────────────────────────────────────────────────────┘
```

## Project Structure

```
15-teams-integration/
├── Cache/
│   ├── Models/
│   │   └── CacheModels.cs      # Cache entity records
│   ├── ITeamsCacheService.cs   # Cache service interface
│   └── TeamsCacheService.cs    # Thread-safe implementation
├── Hooks/
│   ├── TeamsPaginationHook.cs      # Auto-pagination
│   ├── TeamsCredentialScanHook.cs  # Security scanning
│   ├── TeamsMessagePrefixHook.cs   # Message prefixing
│   └── TeamsCachePopulateHook.cs   # Auto-cache population
├── Interceptors/
│   └── TeamsCacheInterceptor.cs    # Cache short-circuiting
├── Utilities/
│   ├── CredentialScanner.cs    # Regex-based credential detection
│   └── TeamsUrlParser.cs       # Teams URL parsing
├── VirtualTools/
│   └── TeamsVirtualTools.cs    # All virtual tool definitions
├── TeamsIntegrationOptions.cs  # Configuration options
├── TeamsIntegrationExtensions.cs # SDK extension method
├── Program.cs                  # Application entry point
├── mcp-proxy.json             # Sample configuration
├── SKILL.md                   # LLM guidance document
└── README.md                  # This file
```

## See Also

- [SKILL.md](./SKILL.md) - LLM guidance for using the Teams integration
- [Sample 13: SDK Virtual Tools](../13-sdk-virtual-tools/) - Virtual tools example
- [Sample 06: Hooks](../06-hooks/) - Hooks configuration example
