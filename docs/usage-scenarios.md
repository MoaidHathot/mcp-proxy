---
layout: default
title: Usage Scenarios
description: Visual guide to MCP Proxy usage patterns with diagrams
---

# Usage Scenarios

A visual guide to the different ways you can use MCP Proxy, from simple single-server setups to enterprise-scale architectures.

---

## 1. Local MCP Exposed as Remote

Turn any local STDIO-based MCP server into a remotely accessible HTTP endpoint. Useful when you want multiple clients or remote machines to reach a local tool.

```mermaid
graph LR
    subgraph Remote Clients
        C1[Client A<br/>Claude Desktop]
        C2[Client B<br/>VS Code]
    end

    subgraph Host Machine
        P[MCP Proxy<br/>HTTP :5000/mcp]
        S[Local MCP Server<br/>STDIO]
    end

    C1 -- "HTTP" --> P
    C2 -- "HTTP" --> P
    P -- "stdin/stdout" --> S

    style P fill:#4a9eff,color:#fff
    style S fill:#6c757d,color:#fff
```

```json
{
  "proxy": { "routing": { "mode": "unified", "basePath": "/mcp" } },
  "mcp": {
    "filesystem": {
      "type": "stdio",
      "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "."]
    }
  }
}
```

Run with: `mcp-proxy --transport http --port 5000`

---

## 2. Remote MCP Exposed as Local

Wrap a remote HTTP/SSE-based MCP server behind a local STDIO interface. Useful when your client only supports STDIO but the server is remote.

```mermaid
graph LR
    C[AI Client<br/>STDIO only] -- "stdin/stdout" --> P[MCP Proxy<br/>STDIO mode]
    P -- "HTTPS" --> R[Remote MCP Server<br/>mcp.example.com]

    style P fill:#4a9eff,color:#fff
    style R fill:#28a745,color:#fff
```

```json
{
  "mcp": {
    "remote-api": {
      "type": "http",
      "url": "https://mcp.example.com/api",
      "headers": { "Authorization": "Bearer ${API_TOKEN}" }
    }
  }
}
```

Run with: `mcp-proxy --transport stdio`

---

## 3. Combining Multiple MCPs into One

Aggregate several backend MCP servers (local and remote) into a single unified endpoint. Tool prefixes prevent name collisions.

```mermaid
graph LR
    C[AI Client] -- "single endpoint" --> P[MCP Proxy<br/>/mcp]

    P -- "stdio" --> FS[Filesystem MCP<br/>fs_*]
    P -- "stdio" --> MEM[Memory MCP<br/>mem_*]
    P -- "HTTPS" --> C7[Context7 MCP<br/>c7_*]

    style P fill:#4a9eff,color:#fff
    style FS fill:#6c757d,color:#fff
    style MEM fill:#6c757d,color:#fff
    style C7 fill:#28a745,color:#fff
```

The client sees all tools from all three servers under one connection: `fs_read_file`, `mem_create_node`, `c7_resolve_library_id`, etc.

```json
{
  "mcp": {
    "filesystem": {
      "type": "stdio", "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "."],
      "tools": { "prefix": "fs" }
    },
    "memory": {
      "type": "stdio", "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-memory"],
      "tools": { "prefix": "mem" }
    },
    "context7": {
      "type": "http",
      "url": "https://mcp.context7.com/mcp",
      "tools": { "prefix": "c7" }
    }
  }
}
```

---

## 4. Per-Server Routing (Each MCP on Its Own URL)

Expose each backend on a dedicated HTTP path instead of aggregating everything. Each path behaves as an independent MCP endpoint.

```mermaid
graph LR
    C1[Client A] -- "/api/filesystem" --> P
    C2[Client B] -- "/api/time" --> P
    C3[Client C] -- "/api/memory" --> P

    P[MCP Proxy<br/>PerServer routing]

    P -- "stdio" --> FS[Filesystem MCP]
    P -- "stdio" --> TM[Time MCP]
    P -- "stdio" --> MEM[Memory MCP]

    style P fill:#4a9eff,color:#fff
    style FS fill:#6c757d,color:#fff
    style TM fill:#6c757d,color:#fff
    style MEM fill:#6c757d,color:#fff
```

Clients connect to only the servers they need. Each endpoint supports full MCP operations (`tools/list`, `tools/call`, etc.).

```json
{
  "proxy": {
    "routing": { "mode": "perServer", "basePath": "/api" }
  },
  "mcp": {
    "filesystem": { "type": "stdio", "command": "npx", "arguments": ["-y", "@anthropic/mcp-server-filesystem", "."] },
    "time":       { "type": "stdio", "command": "npx", "arguments": ["-y", "@anthropic/mcp-server-time"] },
    "memory":     { "type": "stdio", "command": "npx", "arguments": ["-y", "@anthropic/mcp-server-memory"] }
  }
}
```

---

## 5. Tool Filtering (Include / Exclude)

Control which tools are exposed per backend using allowlist, denylist, or regex filters. Expose the same server multiple times with different filters to create role-based views.

```mermaid
graph TD
    subgraph "Same Filesystem Server, Two Views"
        FS_FULL[Filesystem MCP Server<br/>20 tools total]
    end

    P[MCP Proxy] -- "allowlist: read_*, list_*, get_*" --> RO[Read-Only View<br/>fs_read_file, fs_list_directory, ...]
    P -- "denylist: delete_*, remove_*" --> SAFE[Safe-Write View<br/>all except destructive ops]

    FS_FULL --> P

    C[AI Client] --> P

    style P fill:#4a9eff,color:#fff
    style RO fill:#28a745,color:#fff
    style SAFE fill:#ffc107,color:#000
```

```json
{
  "mcp": {
    "filesystem-readonly": {
      "type": "stdio", "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "."],
      "tools": {
        "prefix": "fs",
        "filter": { "mode": "allowlist", "patterns": ["read_*", "list_*", "get_*"] }
      }
    },
    "filesystem-safe": {
      "type": "stdio", "command": "npx",
      "arguments": ["-y", "@anthropic/mcp-server-filesystem", "."],
      "tools": {
        "prefix": "fsw",
        "filter": { "mode": "denylist", "patterns": ["delete_*", "remove_*", "clear_*"] }
      }
    }
  }
}
```

---

## 6. Hooks Pipeline

Hooks intercept tool calls before and after execution. Chain multiple hooks with priority ordering for logging, auth, rate limiting, content filtering, and more.

```mermaid
graph TD
    REQ["Tool Call Request"] --> H1

    subgraph "Pre-Invoke Hooks (ordered by priority)"
        H1["Logging<br/>priority: -1000"] --> H2["Audit<br/>priority: -950"]
        H2 --> H3["Rate Limit<br/>priority: -900"]
        H3 --> H4["Timeout<br/>priority: -800"]
        H4 --> H5["Authorization<br/>priority: -700"]
    end

    H5 --> EXEC["Backend MCP Server<br/>Execute Tool"]

    EXEC --> P1

    subgraph "Post-Invoke Hooks (ordered by priority)"
        P1["Content Filter<br/>priority: 800"] --> P2["Metrics<br/>priority: 900"]
        P2 --> P3["Audit<br/>priority: 950"]
    end

    P3 --> RES["Filtered Response"]

    style REQ fill:#ffc107,color:#000
    style EXEC fill:#6c757d,color:#fff
    style RES fill:#28a745,color:#fff
```

Hooks can block requests (authorization), modify inputs (input transform), modify outputs (content filter, PII redaction), or observe (logging, metrics, audit).

```json
{
  "hooks": {
    "preInvoke": [
      { "type": "logging",       "config": { "logArguments": true } },
      { "type": "rateLimit",     "config": { "maxRequests": 100, "windowSeconds": 60 } },
      { "type": "authorization", "config": { "allowedTools": ["read_file", "list_directory"] } }
    ],
    "postInvoke": [
      { "type": "contentFilter", "config": { "patterns": ["password", "api[_-]?key"], "action": "redact" } }
    ]
  }
}
```

---

## 7. Authentication and Backend Auth

Secure the proxy endpoint and authenticate to protected backends using different credential flows.

```mermaid
graph LR
    C[AI Client] -- "Authorization: Bearer eyJ..." --> P

    subgraph MCP Proxy
        P[Proxy<br/>Azure AD Auth]
        P --> VAL{Validate JWT<br/>scopes & roles}
    end

    VAL -- "On-Behalf-Of flow" --> B1[Protected Backend A<br/>HTTP]
    VAL -- "Managed Identity" --> B2[Protected Backend B<br/>HTTP]
    VAL -- "Forward header" --> B3[Backend C<br/>HTTP]
    VAL -- "stdio" --> B4[Local Backend D<br/>STDIO]

    style P fill:#4a9eff,color:#fff
    style VAL fill:#dc3545,color:#fff
    style B1 fill:#28a745,color:#fff
    style B2 fill:#28a745,color:#fff
    style B3 fill:#28a745,color:#fff
    style B4 fill:#6c757d,color:#fff
```

The proxy validates incoming requests and uses separate credential flows per backend -- client credentials, on-behalf-of, managed identity, or simple header forwarding.

---

## 8. SDK: Virtual Tools and Interceptors

Using the programmatic SDK, you can add custom tools that run inside the proxy (no backend needed), intercept tool lists, and modify tool calls on the fly.

```mermaid
graph TD
    C[AI Client] --> P[MCP Proxy<br/>SDK Mode]

    P --> INT{"Tool Interceptors<br/>(modify tool list)"}

    INT --> VT["Virtual Tools<br/>proxy_status, calculate, echo"]
    INT --> FS["Filesystem MCP<br/>(after rename, remove, modify)"]
    INT --> C7["Context7 MCP"]

    subgraph "SDK Capabilities"
        VT
        HOOK["Global Hooks<br/>pre/post invoke"]
        CI["Call Interceptors<br/>short-circuit calls"]
    end

    HOOK -.-> P
    CI -.-> P

    style P fill:#4a9eff,color:#fff
    style VT fill:#9b59b6,color:#fff
    style INT fill:#ffc107,color:#000
```

```csharp
builder.Services.AddMcpProxy(proxy =>
{
    proxy.AddTool("proxy_status", "Get proxy health", (req, ct) => ...);
    proxy.RenameTool("fs_read_file", "read");
    proxy.RemoveToolsByPattern("*_deprecated");
    proxy.InterceptTools(tools => tools.Where(t => !t.Tool.Name.Contains("dangerous")));
});
```

---

## 9. Telemetry and Debugging

Export metrics and traces via OpenTelemetry, dump request/response payloads, and expose a health endpoint for monitoring.

```mermaid
graph TD
    C[AI Client] --> P[MCP Proxy]

    P --> FS[Filesystem MCP]
    P --> MEM[Memory MCP]

    P -- "OTLP gRPC" --> OTEL[OpenTelemetry Collector<br/>Jaeger / Prometheus]
    P -- "dump to disk" --> DUMP[./dumps/<br/>request/response JSON]
    P -- "GET /debug/health" --> HEALTH["Health Endpoint<br/>per-backend status"]

    subgraph "Observability"
        OTEL
        DUMP
        HEALTH
    end

    style P fill:#4a9eff,color:#fff
    style OTEL fill:#e74c3c,color:#fff
    style DUMP fill:#f39c12,color:#fff
    style HEALTH fill:#28a745,color:#fff
```

---

## 10. Teams Integration (Sample 15) -- Full Architecture

The most advanced scenario: a complete Microsoft Teams MCP Server integration with caching, credential scanning, enhanced descriptions, virtual tools, and two authentication modes.

```mermaid
graph TD
    subgraph "AI Client"
        VS[VS Code / Claude Desktop]
    end

    VS -- "HTTP or STDIO" --> PROXY

    subgraph PROXY["MCP Proxy"]
        direction TB

        AUTH{"Auth Mode?"}
        AUTH -- "forward-auth<br/>(HTTP)" --> FWD["Forward Authorization<br/>client handles OAuth"]
        AUTH -- "proxy-auth<br/>(STDIO)" --> CC["Client Credentials<br/>proxy acquires token"]

        subgraph HOOKS["Pre-Invoke Hooks"]
            direction LR
            H1["Pagination<br/>auto top=20"]
            H2["Credential Scan<br/>block secrets"]
            H3["Message Prefix<br/>[AI] tag"]
            H4["Message Defaults<br/>contentType=html"]
        end

        subgraph INTERCEPTORS["Interceptors"]
            direction LR
            I1["Cache Interceptor<br/>short-circuit from cache"]
            I2["Description Enhancer<br/>rewrite tool docs"]
        end

        subgraph POST["Post-Invoke Hooks"]
            P1["Cache Populate<br/>extract & store entities"]
        end

        subgraph CACHE["Teams Cache Service"]
            direction LR
            CMEM["In-Memory<br/>ConcurrentDictionary"]
            CFILE["File Persistence<br/>teams-cache.json"]
        end

        subgraph VIRTUAL["Virtual Tools (optional)"]
            direction LR
            VT1["teams_resolve"]
            VT2["teams_lookup_*"]
            VT3["teams_cache_status"]
            VT4["teams_cache_refresh"]
            VT5["teams_parse_url"]
            VT6["teams_validate_message"]
        end
    end

    FWD --> TEAMS
    CC --> TEAMS

    TEAMS["Microsoft Teams MCP Server<br/>agent365.svc.cloud.microsoft"]

    %% Flow connections
    HOOKS --> I1
    I1 -- "cache miss" --> TEAMS
    I1 -- "cache hit" --> VS
    TEAMS --> P1
    P1 --> CMEM
    CMEM <--> CFILE
    VIRTUAL --> CMEM

    style PROXY fill:#1a1a2e,color:#fff
    style TEAMS fill:#6264a7,color:#fff
    style AUTH fill:#dc3545,color:#fff
    style CACHE fill:#28a745,color:#fff
    style VIRTUAL fill:#9b59b6,color:#fff
    style HOOKS fill:#ffc107,color:#000
    style INTERCEPTORS fill:#fd7e14,color:#fff
    style POST fill:#20c997,color:#fff
```

### How it works

1. **Request arrives** -- authenticated via forward-auth (browser OAuth) or proxy-auth (client credentials).
2. **Pre-invoke hooks fire** -- auto-paginate list calls, scan messages for leaked credentials, optionally prefix messages with `[AI]`, set `contentType=html` on messages.
3. **Cache interceptor** checks if fresh data exists for list/get operations -- returns cached data immediately if available, skipping the network call entirely.
4. **Tool description interceptor** rewrites all Teams tool descriptions with enhanced context about proxy capabilities (caching, pagination, credential scanning).
5. **Backend call** goes to the Microsoft Teams MCP Server (with the appropriate auth token).
6. **Post-invoke cache populate hook** parses responses from list/get operations and extracts people, chats, teams, and channels into the in-memory cache (persisted to disk).
7. **Virtual tools** (optional) provide direct cache access: resolve names, look up entities, check cache health, parse Teams URLs, and validate messages before sending.

### Config snippet

```json
{
  "proxy": {
    "serverInfo": { "name": "Teams Integration", "version": "1.0.0" }
  },
  "mcp": {
    "teams": {
      "type": "sse",
      "url": "https://agent365.svc.cloud.microsoft/agents/tenants/${TENANT_ID}/servers/mcp_TeamsServer",
      "auth": { "type": "ForwardAuthorization" }
    }
  }
}
```

```csharp
builder.Services.AddMcpProxy(proxy =>
{
    proxy.AddHttpServer("teams", teamsUrl)
         .WithBackendAuth(BackendAuthType.ForwardAuthorization)
         .Build();

    proxy.WithTeamsIntegration(teamsContext);
});
```
