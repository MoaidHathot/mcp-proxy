# Teams Integration Sample

This sample demonstrates how to use MCP-Proxy's SDK features (hooks, interceptors, and virtual tools) to create a rich Microsoft Teams integration layer.

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

## Usage

### SDK-Style (Recommended)

```csharp
builder.Services.AddMcpProxy(proxy =>
{
    proxy.WithServerInfo("My App", "1.0.0");

    // Add your Teams MCP server
    proxy.AddStdioServer("teams", "npx", "-y", "@example/mcp-server-msgraph")
        .WithToolPrefix("teams")
        .Build();
});

// After building the host
var app = builder.Build();
var proxyBuilder = app.Services.GetRequiredService<IMcpProxyBuilder>();

proxyBuilder.WithTeamsIntegration(app.Services, options =>
{
    options.CacheTtl = TimeSpan.FromHours(4);
    options.EnableCredentialScanning = true;
    options.EnableCacheShortCircuit = true;
    options.EnableAutoPagination = true;
});
```

### Configuration Options

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

Returns:
```json
{
  "success": true,
  "type": "Chat",
  "chatId": "19:abc...@thread.v2"
}
```

### teams_validate_message
Pre-check message for credentials.

```json
{
  "message": "Here's the API key: sk-abc123..."
}
```

Returns:
```json
{
  "safe": false,
  "hasCredentials": true,
  "detectedTypes": ["API key"],
  "recommendation": "Do not send this message. Remove sensitive data first."
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                         LLM/Agent                           │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│                      MCP Proxy                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                   Virtual Tools                      │    │
│  │  teams_resolve, teams_parse_url, teams_cache_*      │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                Cache Interceptor                     │    │
│  │        (Short-circuits when cache is fresh)          │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                   Pre-Invoke Hooks                   │    │
│  │  TeamsPaginationHook → TeamsCredentialScanHook →    │    │
│  │  TeamsMessagePrefixHook                              │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                  Post-Invoke Hooks                   │    │
│  │         TeamsCachePopulateHook                       │    │
│  └─────────────────────────────────────────────────────┘    │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│                    Teams MCP Server                          │
│              (Microsoft Graph API backend)                   │
└─────────────────────────────────────────────────────────────┘
```

## Running the Sample

### Prerequisites

1. Your Azure AD tenant ID
2. VS Code with the MCP extension (for authentication)

### Step 1: Start the Proxy

```bash
cd samples/15-teams-integration

# Using environment variable
$env:TENANT_ID = "your-tenant-id"
dotnet run

# Or using command line argument
dotnet run -- --tenant-id=your-tenant-id

# Optional: customize the port (default: 5100)
dotnet run -- --tenant-id=your-tenant-id --port=5200
```

The proxy will start at `http://localhost:5100/mcp` and display:
- Tenant ID
- Proxy URL
- Available features and virtual tools

### Step 2: Configure VS Code

Create or update your VS Code `mcp.json` (typically at `.vscode/mcp.json` in your workspace):

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

**Authentication Flow:**
1. VS Code connects to the proxy at `http://localhost:5100/mcp`
2. VS Code fetches `/.well-known/oauth-authorization-server` from the proxy
3. The proxy forwards this request to the Teams MCP Server and returns the OAuth metadata
4. VS Code uses the metadata to authenticate with Azure AD (browser popup)
5. VS Code sends requests with the `Authorization: Bearer <token>` header
6. The proxy forwards the Authorization header to the Teams MCP Server
7. The Teams MCP Server authenticates using your token

The proxy acts as a **transparent OAuth pass-through** - it proxies the OAuth discovery
metadata from the backend Teams server, allowing VS Code to authenticate directly with
Azure AD without any manual token management.

### Architecture

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
│  │         OAuthMetadataProxyEndpoints                  │    │
│  │    (proxies /.well-known/* from Teams Server)        │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │         ForwardAuthorizationHandler                  │    │
│  │    (captures incoming Authorization header)          │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                   Virtual Tools                      │    │
│  │  teams_resolve, teams_parse_url, teams_cache_*      │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                Cache Interceptor                     │    │
│  │        (Short-circuits when cache is fresh)          │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                   Pre-Invoke Hooks                   │    │
│  │  TeamsPaginationHook → TeamsCredentialScanHook →    │    │
│  │  TeamsMessagePrefixHook                              │    │
│  └─────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                  Post-Invoke Hooks                   │    │
│  │         TeamsCachePopulateHook                       │    │
│  └─────────────────────────────────────────────────────┘    │
└───────────────────────────┬─────────────────────────────────┘
                            │ SSE with forwarded Authorization
┌───────────────────────────▼─────────────────────────────────┐
│                    Teams MCP Server                          │
│    https://agent365.svc.cloud.microsoft/agents/tenants/     │
│              {tenant_id}/servers/mcp_TeamsServer            │
└─────────────────────────────────────────────────────────────┘
```

## See Also

- [SKILL.md](./SKILL.md) - LLM guidance for using the Teams integration
- [Sample 13: SDK Virtual Tools](../13-sdk-virtual-tools/) - Virtual tools example
- [Sample 06: Hooks](../06-hooks/) - Hooks configuration example
