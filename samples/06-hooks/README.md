# Hooks: Logging, Rate Limiting, Audit, and Content Filtering

This example demonstrates the powerful hook system in MCP Proxy. Hooks allow you to intercept tool invocations before and after execution to add logging, rate limiting, authorization, audit trails, and content filtering.

## Features Demonstrated

- **Pre-Invoke Hooks**: Execute before tool calls
- **Post-Invoke Hooks**: Execute after tool calls
- **Logging Hook**: Log tool invocations and results
- **Rate Limiting Hook**: Limit request frequency
- **Authorization Hook**: Control tool access
- **Timeout Hook**: Enforce maximum execution time
- **Audit Hook**: Create compliance audit trails
- **Content Filter Hook**: Redact sensitive data from outputs
- **Output Transform Hook**: Transform tool outputs

## How Hooks Work

Hooks form a pipeline that processes each tool invocation:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        TOOL INVOCATION                              │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     PRE-INVOKE HOOKS                                │
│  ┌─────────┐  ┌───────────┐  ┌─────────┐  ┌──────────────┐         │
│  │ Logging │──│Rate Limit │──│ Timeout │──│Authorization │         │
│  │(-1000)  │  │  (-900)   │  │ (-800)  │  │   (-700)     │         │
│  └─────────┘  └───────────┘  └─────────┘  └──────────────┘         │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     TOOL EXECUTION                                  │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     POST-INVOKE HOOKS                               │
│  ┌─────────┐  ┌───────────────┐  ┌─────────┐                       │
│  │ Logging │──│Content Filter │──│  Audit  │                       │
│  │ (100)   │  │    (800)      │  │  (950)  │                       │
│  └─────────┘  └───────────────┘  └─────────┘                       │
└─────────────────────────────────────────────────────────────────────┘
```

## Built-in Hook Types

| Hook Type | Description | Default Priority |
|-----------|-------------|-----------------|
| `logging` | Log tool invocations | Pre: -1000, Post: 1000 |
| `audit` | Compliance audit trail | Pre: -950, Post: 950 |
| `rateLimit` | Rate limiting | Pre: -900 |
| `timeout` | Execution timeout | Pre: -800 |
| `authorization` | Access control | Pre: -700 |
| `retry` | Auto-retry failures | Post: 100 |
| `contentFilter` | Filter/redact content | Post: 800 |
| `metrics` | OpenTelemetry metrics | Pre/Post: 900 |
| `inputTransform` | Transform inputs | Pre: 0 |
| `outputTransform` | Transform outputs | Post: 0 |

## Hook Configuration Examples

### Logging Hook

```json
{
  "type": "logging",
  "config": {
    "logLevel": "information",    // trace, debug, information, warning, error
    "logArguments": true,         // Log tool arguments
    "logResult": true,            // Log tool results (post-invoke)
    "logDuration": true,          // Log execution time (post-invoke)
    "logTimestamp": true          // Include timestamp
  }
}
```

### Rate Limiting Hook

```json
{
  "type": "rateLimit",
  "config": {
    "maxRequests": 100,           // Maximum requests
    "windowSeconds": 60,          // Time window
    "keyType": "client"           // client, tool, server, combined
  }
}
```

#### Rate Limit Key Types

| Key Type | Description |
|----------|-------------|
| `client` | Rate limit by client/principal ID |
| `tool` | Separate limit per tool |
| `server` | Separate limit per server |
| `combined` | Limit by combination of client, server, and tool |

### Authorization Hook

```json
{
  "type": "authorization",
  "config": {
    "allowedTools": ["read_*", "list_*"],  // Allowed patterns
    "deniedTools": ["delete_*", "write_*"], // Denied patterns
    "requiredRoles": ["admin"],             // Required user roles
    "requiredScopes": ["mcp.read"]          // Required OAuth scopes
  }
}
```

### Timeout Hook

```json
{
  "type": "timeout",
  "config": {
    "timeoutSeconds": 30          // Maximum execution time
  }
}
```

### Audit Hook

```json
{
  "type": "audit",
  "config": {
    "logDestination": "file",     // file, console, both
    "filePath": "./audit.log",    // Audit log file path
    "includeUser": true,          // Include user identity
    "includeTimestamp": true,     // Include timestamp
    "includeArguments": true,     // Include tool arguments
    "includeResult": true,        // Include tool result (post-invoke)
    "includeDuration": true       // Include duration (post-invoke)
  }
}
```

### Content Filter Hook

```json
{
  "type": "contentFilter",
  "config": {
    "patterns": [                 // Patterns to match
      "password",
      "secret",
      "api[_-]?key",
      "\\b\\d{3}-\\d{2}-\\d{4}\\b"  // SSN pattern
    ],
    "action": "redact",           // redact, block, warn
    "replacement": "[REDACTED]",  // Replacement text
    "caseInsensitive": true       // Case-insensitive matching
  }
}
```

#### Content Filter Actions

| Action | Description |
|--------|-------------|
| `redact` | Replace matched content with replacement text |
| `block` | Reject the response entirely |
| `warn` | Log a warning but allow the response |

### Output Transform Hook

```json
{
  "type": "outputTransform",
  "config": {
    "transforms": [
      {
        "pattern": "\\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Z|a-z]{2,}\\b",
        "replacement": "[EMAIL]"
      },
      {
        "pattern": "\\b\\d{4}[- ]?\\d{4}[- ]?\\d{4}[- ]?\\d{4}\\b",
        "replacement": "[CREDIT_CARD]"
      }
    ]
  }
}
```

## Running the Example

```bash
mcpproxy -t stdio -c ./mcp-proxy.json -v
```

The `-v` flag enables verbose logging so you can see hook execution.

## Example Log Output

```
[2024-01-15 10:30:45] PRE-INVOKE: filesystem.read_file
  Arguments: {"path": "/etc/config.json"}
  User: anonymous
  
[2024-01-15 10:30:45] Rate limit check: 15/100 requests (global)

[2024-01-15 10:30:46] POST-INVOKE: filesystem.read_file
  Duration: 142ms
  Result: {"content": "...[REDACTED]..."}
```

## Audit Log Format

```json
{
  "timestamp": "2024-01-15T10:30:45.123Z",
  "event": "tool_invocation",
  "server": "filesystem",
  "tool": "read_file",
  "user": "user@example.com",
  "arguments": {"path": "/etc/config.json"},
  "result": "success",
  "duration_ms": 142
}
```

## Hook Priorities

Hooks execute in priority order (lowest first). Use custom priorities to control execution order:

```json
{
  "type": "logging",
  "priority": -2000,    // Execute before default logging
  "config": { ... }
}
```

## Use Cases

### Security: Block Dangerous Operations
```json
{
  "type": "authorization",
  "config": {
    "deniedTools": ["delete_*", "remove_*", "drop_*"]
  }
}
```

### Compliance: Audit Trail
```json
{
  "type": "audit",
  "config": {
    "logDestination": "file",
    "filePath": "/var/log/mcp-audit.log",
    "includeUser": true,
    "includeArguments": true
  }
}
```

### Performance: Rate Limiting
```json
{
  "type": "rateLimit",
  "config": {
    "maxRequests": 1000,
    "windowSeconds": 3600,
    "scope": "perUser"
  }
}
```

### Privacy: PII Redaction
```json
{
  "type": "contentFilter",
  "config": {
    "patterns": [
      "\\b\\d{3}-\\d{2}-\\d{4}\\b",     // SSN
      "\\b\\d{4}[- ]?\\d{4}[- ]?\\d{4}[- ]?\\d{4}\\b", // Credit Card
      "\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b"  // Email
    ],
    "action": "redact",
    "caseInsensitive": true
  }
}
```

## Next Steps

- See [07-azure-ad-auth](../07-azure-ad-auth) for enterprise authentication with role-based authorization hooks
- See [08-telemetry](../08-telemetry) for OpenTelemetry integration with metrics hooks
