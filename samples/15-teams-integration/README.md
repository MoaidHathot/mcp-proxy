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

```bash
cd samples/15-teams-integration
dotnet run
```

The proxy will start and display available features and tools.

## See Also

- [SKILL.md](./SKILL.md) - LLM guidance for using the Teams integration
- [Sample 13: SDK Virtual Tools](../13-sdk-virtual-tools/) - Virtual tools example
- [Sample 06: Hooks](../06-hooks/) - Hooks configuration example
