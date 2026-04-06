# MCP Proxy - Next Phase Implementation Guide

## Current State (Completed)

### Project Structure
The solution is organized as a multi-project structure:

```
McpProxy.slnx
├── src/
│   ├── McpProxy.Abstractions/     # Interfaces and contracts
│   ├── McpProxy.Sdk/             # Core library implementation
│   └── McpProxy/                  # CLI tool (NuGet global tool)
└── tests/
    ├── McpProxy.Tests.Unit/       # Unit tests (178 tests passing)
    └── McpProxy.Tests.E2E/        # E2E tests (15 tests passing)
```

### Implemented Features

#### 1. Core Proxy Functionality
- **McpProxyServer** (`src/McpProxy.Sdk/Proxy/McpProxyServer.cs`): Aggregates multiple MCP backends
  - `ListToolsAsync`: Lists tools from all backends with filtering and transformation
  - `CallToolAsync`: Routes tool calls to the correct backend with hook support
  - `ListResourcesAsync`: Lists resources from all backends
  - `ReadResourceAsync`: Reads resources from the correct backend
  - `ListPromptsAsync`: Lists prompts from all backends
  - `GetPromptAsync`: Gets prompts from the correct backend

- **McpClientManager** (`src/McpProxy.Sdk/Proxy/McpClientManager.cs`): Manages connections to backend MCP servers
  - Supports STDIO, HTTP, and SSE transports
  - Connection lifecycle management

#### 2. Configuration System
- **ProxyConfiguration** (`src/McpProxy.Sdk/Configuration/ProxyConfiguration.cs`): Main config model
- **ServerConfiguration** (`src/McpProxy.Sdk/Configuration/ServerConfiguration.cs`): Per-server config
  - Transport type (Stdio, Http, Sse)
  - Command/Arguments for STDIO
  - URL for HTTP/SSE
  - Headers for HTTP requests
  - Environment variables
  - Tools filtering configuration
  - Hooks configuration
  - Route path for per-server routing
  - Enabled flag

- **ConfigurationLoader** (`src/McpProxy.Sdk/Configuration/ConfigurationLoader.cs`):
  - JSON configuration loading with comments and trailing commas
  - Environment variable substitution: `"env:VAR_NAME"` and `${VAR_NAME}` syntax
  - Validation of required properties

#### 3. Tool Filtering System
- **IToolFilter** interface with implementations:
  - `NoFilter`: Include all tools
  - `AllowListFilter`: Include only matching tools (wildcard support)
  - `DenyListFilter`: Exclude matching tools (wildcard support)
  - `RegexFilter`: Regex-based include/exclude patterns
- **FilterFactory**: Creates filters from configuration
- Case-insensitive matching option

#### 4. Tool Transformation
- **IToolTransformer** interface for modifying tool definitions
- **ToolPrefixer**: Adds server-specific prefixes to tool names
  - Configurable prefix and separator
  - Reverse lookup (remove prefix) for routing

#### 5. Hook System
- **IPreInvokeHook**: Execute before tool calls (can modify request)
- **IPostInvokeHook**: Execute after tool calls (can modify result)
- **IToolHook**: Combined pre and post hooks
- **HookPipeline** (`src/McpProxy.Sdk/Hooks/HookPipeline.cs`):
  - Priority-based ordering
  - Context sharing between hooks
- **Built-in hooks** (`src/McpProxy.Sdk/Hooks/BuiltInHooks.cs`):
  - `LoggingHook`: Logs tool invocations
  - `InputTransformHook`: Transforms input arguments
  - `OutputTransformHook`: Transforms output results

#### 6. Authentication (Basic Structure)
- **IAuthenticationHandler** interface
- **ApiKeyAuthHandler**: API key validation
- **BearerTokenAuthHandler**: JWT token validation (structure only)
- **AuthenticationMiddleware**: ASP.NET Core middleware for HTTP endpoint

#### 7. CLI Tool
- **Program.cs** (`src/McpProxy/Program.cs`):
  - `--transport` / `-t`: Server transport type (stdio, http, sse)
  - `--config` / `-c`: Path to configuration file
  - `--port` / `-p`: Port for HTTP/SSE server
  - `--verbose` / `-v`: Enable verbose logging
- Registers all MCP handlers (Tools, Resources, Prompts)
- Supports both STDIO and HTTP transports

#### 8. NuGet Package Configuration
- Package ID: `McpProxy`
- Tool command: `mcpproxy`
- Author: Moaid Hathot
- MIT License

#### 9. Advanced MCP Protocol Support
- **ProxyClientHandlers** (`src/McpProxy.Sdk/Proxy/ProxyClientHandlers.cs`): Forwards requests from backend servers to connected clients
  - Sampling: Backend servers can request LLM completions via the proxy
  - Elicitation: Backend servers can request structured user input
  - Roots: Backend servers can request file system root information
- **McpClientManager** updated to wire handlers when creating backend connections
- Automatic capability detection and graceful degradation

### SDK Versions and Important API Details

**MCP SDK v1.0.0** (Stable Release):
- Use `TextContentBlock` (not `TextContent`)
- Use `BlobContentBlock` (not `BlobContent`)
- Use `TextResourceContents` / `BlobResourceContents` for resource contents
- `McpClientPrompt` does NOT have an `Arguments` property
- `Role` is an enum: `Role.Assistant`, `Role.User`
- Methods like `ListToolsAsync`, `CallToolAsync` take `cancellationToken` as named parameter
- `Tool.Name` is now a `required` property
- `McpClientHandlers` is now sealed
- Use `McpServerPrimitiveCollection.TryGetPrimitive()` for name-based lookups (removed `Tool.McpServerTool`, etc.)
- Binary data uses `ReadOnlyMemory<byte>` instead of string
- Use `WithRequestFilters()` and `WithMessageFilters()` instead of removed `Add*Filter` extension methods

**System.CommandLine v2.0.0-beta5**:
- Use `new Option<T>("--name", "-alias") { Description = ..., DefaultValueFactory = ... }`
- Use `rootCommand.Parse(args).InvokeAsync()` for async execution
- Use `rootCommand.Options.Add()` to add options

---

## Completed Phases

### Phase 3: Advanced MCP Protocol Support ✅

#### 3.1 Sampling Support ✅
Backend MCP servers can now request LLM completions from the proxy's connected client.

**Implementation**:
- `ProxyClientHandlers` (`src/McpProxy.Sdk/Proxy/ProxyClientHandlers.cs`): Handles forwarding sampling/elicitation/roots requests from backend servers to connected clients
- `McpClientManager` updated to accept `ProxyClientHandlers` and configure handlers when creating backend connections
- Backend clients advertise `SamplingCapability`, `ElicitationCapability`, and `RootsCapability`
- Handlers set up to forward requests via `McpServer.SampleAsync()`, `McpServer.ElicitAsync()`, `McpServer.RequestRootsAsync()`

#### 3.2 Elicitation Support ✅
Backend MCP servers can now request structured input from users via the proxy's connected client.

**Implementation**:
- `HandleElicitationAsync` in `ProxyClientHandlers` forwards elicitation requests
- Graceful degradation when client doesn't support elicitation (returns "decline" action)

#### 3.3 Roots Support ✅
Backend MCP servers can now request file system roots from the proxy's connected client.

**Implementation**:
- `HandleRootsAsync` in `ProxyClientHandlers` forwards roots requests
- Returns empty roots list when client doesn't support roots

### Phase 5: JSON-Based Hook Configuration ✅

Hooks can now be configured via JSON in addition to programmatic configuration.

**Implementation**:
- `HookFactory` (`src/McpProxy.Sdk/Hooks/HookFactory.cs`): Factory for creating hook instances from configuration
  - Supports built-in hooks: `logging`, `inputTransform`, `outputTransform`
  - `ConfigurePipeline()` method adds hooks to a pipeline from config
  - Extensible via `RegisterHookType()` for custom hooks
  - Case-insensitive hook type matching
- `Program.cs` updated to:
  - Register `HookFactory` as a service
  - Call `ConfigureHookPipelines()` to wire hooks from config to each server's pipeline
- Unit tests: `HookFactoryTests.cs` (22 new tests)

**Example JSON config**:
```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "node",
      "arguments": ["server.js"],
      "hooks": {
        "preInvoke": [
          { "type": "logging", "config": { "logLevel": "debug", "logArguments": true } }
        ],
        "postInvoke": [
          { "type": "outputTransform", "config": { "redactPatterns": ["password", "secret"] } }
        ]
      }
    }
  }
}
```

### Phase 7: Multi-Endpoint Routing ✅

Support for exposing servers on different HTTP endpoints.

**Implementation**:
- `SingleServerProxy` (`src/McpProxy.Sdk/Proxy/SingleServerProxy.cs`): Proxy for single backend server
  - Simplified API that doesn't require `RequestContext<T>` (accepts params directly)
  - Supports all MCP operations: ListTools, CallTool, ListResources, ReadResource, ListPrompts, GetPrompt
  - Filtering and prefixing support
  - Hook pipeline integration
- `Program.cs` updated with `MapSingleServerEndpoint()` to create HTTP endpoints for each server:
  - `POST {route}/tools/list`
  - `POST {route}/tools/call`
  - `POST {route}/resources/list`
  - `POST {route}/resources/read`
  - `POST {route}/prompts/list`
  - `POST {route}/prompts/get`
- `RoutingMode` enum: `Unified` (all servers on one endpoint) or `PerServer` (each server on its own route)
- Unit tests: `SingleServerProxyTests.cs` (14 new tests)

**Example JSON config**:
```json
{
  "proxy": {
    "routing": {
      "mode": "perServer",
      "basePath": "/mcp"
    }
  },
  "mcp": {
    "github-server": {
      "type": "http",
      "url": "http://localhost:3001",
      "route": "/github"
    },
    "filesystem-server": {
      "type": "stdio",
      "command": "node",
      "arguments": ["fs-server.js"],
      "route": "/filesystem"
    }
  }
}
```

### Phase 8: E2E Tests ✅

Created E2E tests that validate the full proxy behavior with mock MCP clients.

**Implementation**:
- `ProxyTestBase` (`tests/McpProxy.Tests.E2E/Fixtures/ProxyTestBase.cs`): Test base class with mock setup helpers
  - `CreateMockClient()`: Creates mock `IMcpClientWrapper` with configurable tools/resources/prompts
  - `CreateTool()`, `CreateResource()`, `CreatePrompt()`: Helper methods for creating protocol objects
  - `RegisterClient()`: Registers mock clients with the client manager
  - `CreateProxyServer()`: Creates proxy server with given configuration

- **IMcpClientWrapper interface**: Wrapper around `McpClient` enabling testability
  - MCP SDK's `McpClientTool`, `McpClientResource`, `McpClientPrompt` are sealed classes
  - Interface returns protocol types (`Tool`, `Resource`, `Prompt`) instead of sealed client types
  - `McpClientWrapper` implementation converts client types to protocol types

**Test files**:
- `ToolAggregationTests.cs` (5 tests): Tests tool listing aggregation from multiple backends
- `ToolRoutingTests.cs` (4 tests): Tests tool call routing to correct backend
- `FilteringTests.cs` (6 tests): Tests tool filtering (allowlist/denylist) and prefixing

**Test scenarios covered**:
- Tool listing from single/multiple backends
- Tool aggregation preserves metadata
- Tool calls route to correct backend
- Unknown tools return errors
- Prefixed tools can be called with prefix
- DenyList filtering excludes matching tools
- AllowList filtering includes only matching tools
- Custom prefix separators work correctly
- Filters applied before prefixes

---

### Phase 9: Documentation and Polish ✅

1. **README.md updates** ✅:
   - Installation instructions (global tool, dnx, build from source)
   - Quick start guide
   - Complete configuration reference (proxy settings, server config, filtering, prefixing, hooks)
   - Example configurations for Claude Desktop, OpenCode, GitHub Copilot
   - Programmatic usage examples
   - Advanced MCP protocol features (Sampling, Elicitation, Roots)

2. **Sample configuration file** ✅:
   - Created `mcp-proxy.sample.json` with comprehensive examples

3. **NuGet package** ✅:
   - Package metadata verified (ID, description, tags, license, README)
   - Icon placeholder in .csproj (ready when icon is added)

4. **Code cleanup** ✅:
   - No TODO comments in codebase
   - All public APIs have XML documentation (100% coverage)
   - Nullability annotations enabled project-wide

---

## Completed Enhancements (SDK 1.0.0 Capabilities)

### Phase 10: Advanced SDK 1.0.0 Features ✅

#### 10.1 Resource/Prompt Filtering ✅
Added filtering interfaces for resources and prompts, similar to existing tool filtering.

**Implementation**:
- `IResourceFilter` interface with implementations: `ResourceNoFilter`, `ResourceAllowListFilter`, `ResourceDenyListFilter`, `ResourceRegexFilter`
- `IPromptFilter` interface with implementations: `PromptNoFilter`, `PromptAllowListFilter`, `PromptDenyListFilter`, `PromptRegexFilter`
- `ResourceFilterFactory` and `PromptFilterFactory` for creating filters from configuration
- Updated `McpClientInfo` to hold resource and prompt filters
- Updated `McpProxyServer.ListResourcesAsync()` and `ListPromptsAsync()` to apply filters
- Unit tests: `ResourceFiltersTests.cs` and `PromptFiltersTests.cs`

#### 10.2 Tool List Caching with TTL ✅
Cache tool lists from backends to reduce repeated calls, with configurable TTL and notification-based invalidation.

**Implementation**:
- `ToolCache` class (`src/McpProxy.Sdk/Proxy/ToolCache.cs`): Thread-safe cache with TTL support
  - Configurable default TTL (default: 5 minutes)
  - Per-server TTL override support
  - `GetOrFetchAsync()` method for automatic cache population
  - Manual invalidation via `Invalidate()` and `InvalidateAll()`
  - Automatic invalidation on `tools/list_changed` notifications
- `CacheConfiguration` in `ProxyConfiguration` for JSON-based configuration
- Updated `McpProxyServer.ListToolsAsync()` and `FindToolAsync()` to use cache
- Unit tests: `ToolCacheTests.cs` (12 tests)

**Example JSON config**:
```json
{
  "proxy": {
    "cache": {
      "enabled": true,
      "defaultTtlSeconds": 300,
      "perServerTtl": {
        "slow-server": 600
      }
    }
  }
}
```

#### 10.3 Progress Notifications Forwarding ✅
Forward progress notifications from backends to connected clients.

**Implementation**:
- `NotificationForwarder` class (`src/McpProxy.Sdk/Proxy/NotificationForwarder.cs`): Handles forwarding of all notification types
  - `ForwardProgressAsync()`: Forwards `notifications/progress` from backends
  - `ForwardResourceUpdatedAsync()`: Forwards `notifications/resources/updated`
  - `ForwardToolListChangedAsync()`: Forwards `tools/list_changed` (also invalidates cache)
  - `ForwardResourceListChangedAsync()`: Forwards `resources/list_changed`
  - `ForwardPromptListChangedAsync()`: Forwards `prompts/list_changed`
- Updated `McpClientManager` to wire up notification handlers when creating backend connections
- Progress notifications include progress token mapping between client and backend
- Unit tests: `NotificationForwarderTests.cs` (15 tests)

#### 10.4 List Changed Notifications ✅
Forward list changed notifications from backends to clients, enabling dynamic capability updates.

**Implementation**:
- All list changed notification types supported:
  - `tools/list_changed`: Notifies client when backend's tool list changes (also invalidates tool cache)
  - `resources/list_changed`: Notifies client when backend's resource list changes
  - `prompts/list_changed`: Notifies client when backend's prompt list changes
- Notifications aggregated from all backends and forwarded to client
- Unit tests included in `NotificationForwarderTests.cs`

#### 10.5 Resource Subscriptions ✅
Support for resource subscription management and notification forwarding.

**Implementation**:
- `ResourceSubscriptionManager` class (`src/McpProxy.Sdk/Proxy/ResourceSubscriptionManager.cs`):
  - Tracks active subscriptions per client session
  - Maps resource URIs to their backend servers
  - Thread-safe subscription tracking with `ConcurrentDictionary`
- Added `SubscribeToResourceAsync()` and `UnsubscribeFromResourceAsync()` to `IMcpClientWrapper` interface
- Added subscription handlers to `McpProxyServer`:
  - `SubscribeToResourceAsync()` / `SubscribeToResourceCoreAsync()`: Subscribes to resource updates
  - `UnsubscribeFromResourceAsync()` / `UnsubscribeFromResourceCoreAsync()`: Unsubscribes from resource updates
- Wired handlers in `Program.cs` via `WithSubscribeToResourcesHandler()` and `WithUnsubscribeFromResourcesHandler()`
- `notifications/resources/updated` already forwarded via `NotificationForwarder`
- Unit tests: `ResourceSubscriptionTests.cs` (17 tests)

---

## Completed Enhancements (Phase 11)

### 11.1 Capability Extensions ✅
Support for configuring client and server experimental capabilities through JSON configuration.

**Implementation**:
- `CapabilityConfiguration` class with `ClientCapabilitySettings` and `ServerCapabilitySettings`
- Client capabilities: `Sampling`, `Elicitation`, `Roots` (boolean toggles) + `Experimental` dictionary
- Server capabilities: `Experimental` dictionary for custom features advertised to clients
- `McpClientManager` updated to configure client capabilities based on settings
- `Program.cs` updated with `ConfigureServerOptions()` to set server experimental capabilities
- Unit tests: 8 new tests in `ConfigurationLoaderTests.cs`

**Example JSON config**:
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

### 11.2 Resource/Prompt Transformers ✅
Added `IResourceTransformer` and `IPromptTransformer` interfaces for transforming resource URIs and prompt names.

**Implementation**:
- `ResourcePrefixer` class: Adds server-specific prefixes to resource URIs (default separator: `://`)
- `PromptPrefixer` class: Adds server-specific prefixes to prompt names (default separator: `_`)
- `ResourceTransformerFactory` and `PromptTransformerFactory` for creating transformers from config
- Updated `McpProxyServer` to apply resource and prompt transformers during listing and routing
- Updated `ResourceInfo` and `PromptInfo` to include `OriginalUri`/`OriginalName` for reverse lookup
- Unit tests: `ResourcePrefixerTests.cs` (14 tests), `PromptPrefixerTests.cs` (14 tests)

**Example JSON config**:
```json
{
  "mcp": {
    "github-server": {
      "type": "http",
      "url": "http://localhost:3001",
      "resources": {
        "prefix": "github",
        "prefixSeparator": "://"
      },
      "prompts": {
        "prefix": "github",
        "prefixSeparator": "_"
      }
    }
  }
}
```

### 11.3 Metrics and Observability (OpenTelemetry) ✅
Added OpenTelemetry integration for distributed tracing and metrics.

**Implementation**:
- `ProxyMetrics` class (`src/McpProxy.Sdk/Telemetry/ProxyMetrics.cs`):
  - Counters: `mcpproxy.tool_calls.total`, `mcpproxy.tool_calls.successful`, `mcpproxy.tool_calls.failed`
  - Counters: `mcpproxy.resource_reads.total`, `mcpproxy.prompt_gets.total`
  - Histograms: `mcpproxy.tool_call.duration`, `mcpproxy.resource_read.duration`, `mcpproxy.prompt_get.duration`
  - UpDownCounter: `mcpproxy.backend_connections.active`
  - All metrics include `server` and operation-specific tags
- `ProxyActivitySource` class (`src/McpProxy.Sdk/Telemetry/ProxyActivitySource.cs`):
  - Activities: `mcpproxy.tool_call`, `mcpproxy.resource_read`, `mcpproxy.prompt_get`
  - Activities: `mcpproxy.list_tools`, `mcpproxy.list_resources`, `mcpproxy.list_prompts`
  - Tags: `mcp.server`, `mcp.tool`, `mcp.resource`, `mcp.prompt`, `mcp.operation`
  - Error recording with `error.type`, `error.message`, exception events
- `TelemetryServiceExtensions` class for wiring up OpenTelemetry services
- `TelemetryConfiguration` class with `MetricsConfiguration` and `TracingConfiguration`
- Support for console exporter (debugging) and OTLP exporter (production)
- Unit tests: `ProxyMetricsTests.cs` (11 tests), `ProxyActivitySourceTests.cs` (11 tests)

**Example JSON config**:
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

---

## Future Enhancements

1. **Rate Limiting**:
   - Add per-client and per-backend rate limiting
   - Configurable limits via JSON

2. **Circuit Breaker**:
   - Add circuit breaker pattern for backend health management
   - Automatic backend failover and recovery

---

## Architecture Notes

### Request Flow
```
Client Request
     │
     ▼
┌─────────────────┐
│  MCP Server     │  (McpProxy CLI)
│  (STDIO/HTTP)   │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ McpProxyServer  │  Routing + Aggregation
└────────┬────────┘
         │
    ┌────┴────┐
    │         │
    ▼         ▼
┌───────┐ ┌───────┐
│Backend│ │Backend│  (via McpClientManager)
│   1   │ │   2   │
└───────┘ └───────┘
```

### Hook Pipeline Flow
```
Tool Call Request
     │
     ▼
┌─────────────────────┐
│ Pre-Invoke Hook 1   │ (priority 1)
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│ Pre-Invoke Hook 2   │ (priority 2)
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│ Backend Tool Call   │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│ Post-Invoke Hook 1  │ (priority 1)
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│ Post-Invoke Hook 2  │ (priority 2)
└─────────┬───────────┘
          │
          ▼
   Tool Call Response
```

### Key Extension Points
1. **IToolFilter / IResourceFilter / IPromptFilter**: Custom filtering logic
2. **IToolTransformer**: Custom tool transformation
3. **IPreInvokeHook / IPostInvokeHook**: Custom request/response handling
4. **IAuthenticationHandler**: Custom authentication schemes

---

## File Reference

### Core Files
| File | Purpose |
|------|---------|
| `src/McpProxy.Sdk/Proxy/McpProxyServer.cs` | Main proxy logic |
| `src/McpProxy.Sdk/Proxy/McpClientManager.cs` | Backend connection management |
| `src/McpProxy.Sdk/Proxy/McpClientInfo.cs` | Client info container |
| `src/McpProxy.Sdk/Proxy/IMcpClientWrapper.cs` | Interface for testable MCP client |
| `src/McpProxy.Sdk/Proxy/McpClientWrapper.cs` | MCP client wrapper implementation |
| `src/McpProxy.Sdk/Proxy/SingleServerProxy.cs` | Single-server proxy for PerServer routing |
| `src/McpProxy.Sdk/Proxy/ProxyClientHandlers.cs` | Forwarding handlers for sampling/elicitation/roots |
| `src/McpProxy.Sdk/Proxy/ToolCache.cs` | Tool list caching with TTL |
| `src/McpProxy.Sdk/Proxy/NotificationForwarder.cs` | Notification forwarding (progress, list changed) |
| `src/McpProxy.Sdk/Proxy/ResourceSubscriptionManager.cs` | Resource subscription tracking |
| `src/McpProxy.Sdk/Configuration/ConfigurationLoader.cs` | JSON config loading |
| `src/McpProxy.Sdk/Configuration/ProxyConfiguration.cs` | Config model |
| `src/McpProxy.Sdk/Hooks/HookPipeline.cs` | Hook execution pipeline |
| `src/McpProxy.Sdk/Hooks/HookFactory.cs` | Hook factory for JSON config |
| `src/McpProxy.Sdk/Filtering/ToolFilters.cs` | Tool filter implementations |
| `src/McpProxy.Sdk/Filtering/ResourceFilters.cs` | Resource filter implementations |
| `src/McpProxy.Sdk/Filtering/PromptFilters.cs` | Prompt filter implementations |
| `src/McpProxy/Program.cs` | CLI entry point |

### Test Files
| File | Purpose |
|------|---------|
| `tests/McpProxy.Tests.Unit/Filtering/ToolFiltersTests.cs` | Tool filter tests |
| `tests/McpProxy.Tests.Unit/Filtering/ResourceFiltersTests.cs` | Resource filter tests |
| `tests/McpProxy.Tests.Unit/Filtering/PromptFiltersTests.cs` | Prompt filter tests |
| `tests/McpProxy.Tests.Unit/Hooks/HookPipelineTests.cs` | Hook pipeline tests |
| `tests/McpProxy.Tests.Unit/Hooks/HookFactoryTests.cs` | Hook factory tests |
| `tests/McpProxy.Tests.Unit/Configuration/ConfigurationLoaderTests.cs` | Config tests |
| `tests/McpProxy.Tests.Unit/Proxy/ProxyClientHandlersTests.cs` | Client handlers tests |
| `tests/McpProxy.Tests.Unit/Proxy/SingleServerProxyTests.cs` | SingleServerProxy tests |
| `tests/McpProxy.Tests.Unit/Proxy/ToolCacheTests.cs` | Tool cache tests |
| `tests/McpProxy.Tests.Unit/Proxy/NotificationForwarderTests.cs` | Notification forwarder tests |
| `tests/McpProxy.Tests.Unit/Proxy/ResourceSubscriptionTests.cs` | Resource subscription tests |
| `tests/McpProxy.Tests.E2E/Fixtures/ProxyTestBase.cs` | E2E test base class |
| `tests/McpProxy.Tests.E2E/ToolAggregationTests.cs` | Tool aggregation E2E tests |
| `tests/McpProxy.Tests.E2E/ToolRoutingTests.cs` | Tool routing E2E tests |
| `tests/McpProxy.Tests.E2E/FilteringTests.cs` | Filtering E2E tests |

### Configuration Files
| File | Purpose |
|------|---------|
| `Directory.Packages.props` | Central package versions |
| `nuget.config` | NuGet sources |
| `McpProxy.slnx` | Solution file |
