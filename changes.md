# v1.14.0 Changelog

## Breaking Changes

- **`RoutingMode` and `BackendAuthType` moved to `McpProxy.Abstractions`** — These enums were previously in `McpProxy.Sdk.Configuration`. Code referencing them by fully-qualified name will need to update the namespace to `McpProxy.Abstractions`. The `using McpProxy.Sdk.Configuration` import may need `using McpProxy.Abstractions` added alongside it.

- **Duplicate server names now throw** — `AddStdioServer`, `AddHttpServer`, and `AddSseServer` now throw `ArgumentException` if a server with the same name was already added. Previously, duplicates were silently overwritten.

## Bug Fixes

- **Fixed: Per-server MCP endpoints now support full MCP Streamable HTTP protocol** — Both the CLI and SDK now call `MapMcp(route)` for each per-server route, creating proper MCP Streamable HTTP endpoints alongside the existing REST sub-routes. MCP protocol clients can now connect to `/mcp/server-a` and receive only that server's tools, resources, and prompts. Previously, only REST-style endpoints had per-server isolation; MCP protocol endpoints returned all tools from all backends.

- **Fixed: Subscribe/unsubscribe handlers now respect per-server routing** — Resource subscription and unsubscription handlers in `WithSdkProxyHandlers` and `WithProxyHandlers` now delegate to `SingleServerProxy` for per-server routes, matching the behavior of all other handlers. Added `SubscribeToResourceAsync` and `UnsubscribeFromResourceAsync` methods to `SingleServerProxy`.

- **Fixed: `WithConfigurationFile` is now functional** — `InitializeMcpProxyAsync` now loads and merges the specified JSON configuration file. Servers defined in the file are added via `TryAdd` — SDK-defined servers take priority over file-defined servers with the same name.

- **Fixed: SDK never registered `IHealthTracker` or debugging services** — The SDK's `RegisterSdkServices` now registers `NullHealthTracker`, `NullHookTracer`, and `NullRequestDumper` as defaults via `TryAddSingleton`. SDK consumers can override these with real implementations. `McpClientManager` now receives `IHealthTracker` from DI instead of always using the null implementation.

- **Fixed: TTL default mismatch** — README documentation for `proxy.caching.tools.ttlSeconds` previously stated the default was 60 seconds, but the SDK default was 300 seconds. README now correctly documents 300 seconds.

## New Features

- **`WithRouting` and `WithBackendAuth` added to builder interfaces** — `WithRouting(RoutingMode, string?)` is now on `IMcpProxyBuilder` and `WithBackendAuth(BackendAuthType)` is now on `IServerBuilder`. Both were previously only available as extension methods with runtime type checks.

- **Per-server virtual tools** — `IServerBuilder.AddVirtualTool(tool, handler)` adds virtual tools scoped to a specific server's per-server route. These tools appear only on that server's endpoint, alongside the server's backend tools.

- **Global virtual tools on per-server routes** — `IMcpProxyBuilder.WithGlobalVirtualToolsOnPerServerRoutes(true)` makes global virtual tools (added via `AddVirtualTool` on the proxy builder) appear on all per-server routes in addition to the unified endpoint. Default is `false` (existing behavior).

## Removed

- **Removed dead `ResourceSubscriptionManager`** — This class and its tests were orphaned code. Neither the CLI nor SDK used it — both implement subscription logic inline in their proxy servers. Removed `ResourceSubscriptionManager.cs` and `ResourceSubscriptionTests.cs`.

## Documentation

- **Updated SDK API Reference** — Added previously undocumented methods to the README tables: `WithRouting`, `WithGlobalHook`, `WithGlobalVirtualToolsOnPerServerRoutes`, `WithRoute`, `WithPromptPrefix`, `WithToolFilter`, `WithToolTransformer`, `WithHook`, `AddVirtualTool` (on `IServerBuilder`).
