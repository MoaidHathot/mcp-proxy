---
layout: default
title: Features
description: Filtering, prefixing, and hooks in MCP Proxy
---

## Features

MCP Proxy provides powerful features to control how tools, resources, and prompts are exposed from backend servers.

<div class="toc">
<h4>On this page</h4>
<ul>
<li><a href="#filtering">Filtering</a></li>
<li><a href="#prefixing">Prefixing</a></li>
<li><a href="#hooks">Hooks</a></li>
</ul>
</div>

## Filtering

Filtering allows you to control which tools, resources, and prompts are exposed from each backend server. This is useful for:

- **Security**: Hide sensitive tools from certain environments
- **Simplicity**: Only expose relevant tools to reduce clutter
- **Compliance**: Prevent access to restricted functionality

### Filter Modes

MCP Proxy supports four filter modes:

| Mode | Description |
|------|-------------|
| `none` | No filtering - expose all items (default) |
| `allowlist` | Only expose items matching at least one pattern |
| `denylist` | Expose all items except those matching any pattern |
| `regex` | Use regex patterns for fine-grained control |

### Tool Filtering

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "tools": {
        "filter": {
          "mode": "allowlist",
          "patterns": ["read_*", "list_*", "get_*"],
          "caseInsensitive": true
        }
      }
    }
  }
}
```

### Resource Filtering

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "resources": {
        "filter": {
          "mode": "denylist",
          "patterns": ["internal:*", "debug:*"]
        }
      }
    }
  }
}
```

### Prompt Filtering

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "prompts": {
        "filter": {
          "mode": "allowlist",
          "patterns": ["generate_*", "create_*"]
        }
      }
    }
  }
}
```

### Pattern Syntax

#### Wildcard Patterns (AllowList/DenyList)

Patterns support simple wildcard matching:

- `*` matches any sequence of characters
- `?` matches any single character
- Patterns are matched against the entire name
- Case-insensitive by default (configurable)

**Examples:**

| Pattern | Matches | Doesn't Match |
|---------|---------|---------------|
| `read_*` | `read_file`, `read_data` | `write_file`, `reader` |
| `*_file` | `read_file`, `write_file` | `file_read`, `files` |
| `*user*` | `get_user`, `user_list`, `find_users` | `getUser` (if case-sensitive) |
| `get_?` | `get_a`, `get_1` | `get_ab`, `get` |

#### Regex Patterns

For regex mode, provide one or two patterns:

1. First pattern: Include regex (items matching this are included)
2. Second pattern (optional): Exclude regex (items matching this are excluded)

```json
{
  "tools": {
    "filter": {
      "mode": "regex",
      "patterns": [
        "^(read|list|get)_.*$",
        "^.*_internal$"
      ]
    }
  }
}
```

This includes tools starting with `read_`, `list_`, or `get_`, but excludes any ending with `_internal`.

### Filter Order

When both filtering and prefixing are configured, filtering is applied **before** prefixing. This means patterns should match the original tool names, not the prefixed names.

## Prefixing

Prefixing adds a server-specific prefix to tool, resource, and prompt names. This prevents name collisions when aggregating multiple servers that might have identically-named items.

### Tool Prefixing

```json
{
  "mcp": {
    "github": {
      "type": "http",
      "url": "https://github-mcp.example.com",
      "tools": {
        "prefix": "gh",
        "prefixSeparator": "_"
      }
    },
    "gitlab": {
      "type": "http",
      "url": "https://gitlab-mcp.example.com",
      "tools": {
        "prefix": "gl",
        "prefixSeparator": "_"
      }
    }
  }
}
```

With this configuration:
- GitHub's `create_issue` becomes `gh_create_issue`
- GitLab's `create_issue` becomes `gl_create_issue`

### Resource Prefixing

Resources use a different default separator to work with URI schemes:

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "resources": {
        "prefix": "myserver",
        "prefixSeparator": "://"
      }
    }
  }
}
```

A resource `file:///path/to/doc.md` becomes `myserver://file:///path/to/doc.md`.

### Prompt Prefixing

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "prompts": {
        "prefix": "myserver",
        "prefixSeparator": "_"
      }
    }
  }
}
```

### Prefix Configuration

| Property | Default (Tools) | Default (Resources) | Default (Prompts) |
|----------|-----------------|---------------------|-------------------|
| `prefix` | `null` | `null` | `null` |
| `prefixSeparator` | `"_"` | `"://"` | `"_"` |

### How Routing Works

When a client calls a prefixed tool, MCP Proxy:

1. Receives the call with the prefixed name (e.g., `gh_create_issue`)
2. Identifies the server from the prefix
3. Strips the prefix to get the original name (`create_issue`)
4. Routes the call to the correct backend server

This is transparent to both the client and the backend server.

## Hooks

Hooks allow you to execute custom logic before and after tool calls. Use cases include:

- **Logging**: Track all tool invocations
- **Transformation**: Modify inputs or outputs
- **Validation**: Check arguments before execution
- **Redaction**: Remove sensitive data from responses

### Hook Types

| Interface | Phase | Purpose |
|-----------|-------|---------|
| `IPreInvokeHook` | Before tool call | Modify request, validate, log |
| `IPostInvokeHook` | After tool call | Modify response, log, redact |
| `IToolHook` | Both | Combined pre and post hook |

### JSON Configuration

```json
{
  "mcp": {
    "my-server": {
      "type": "stdio",
      "command": "my-server",
      "hooks": {
        "preInvoke": [
          {
            "type": "logging",
            "config": {
              "logLevel": "debug",
              "logArguments": true
            }
          }
        ],
        "postInvoke": [
          {
            "type": "outputTransform",
            "config": {
              "redactPatterns": ["password", "secret", "token", "api_key"]
            }
          }
        ]
      }
    }
  }
}
```

### Built-in Hooks

MCP Proxy provides several built-in hooks for common enterprise scenarios:

| Hook Type | Purpose | Priority |
|-----------|---------|----------|
| `logging` | Log tool invocations | -1000 |
| `audit` | Compliance audit trail | -950 (pre), 950 (post) |
| `rateLimit` | Prevent abuse, protect backends | -900 |
| `timeout` | Enforce maximum execution time | -800 |
| `authorization` | Role/scope-based access control | -700 |
| `retry` | Auto-retry transient failures | 100 |
| `contentFilter` | Block/redact prohibited content | 800 |
| `metrics` | Detailed observability metrics | 900 |
| `inputTransform` | Transform tool inputs | 0 |
| `outputTransform` | Transform tool outputs | 0 |

Lower priority values execute first for pre-invoke hooks, and last for post-invoke hooks.

#### Logging Hook

Logs tool invocations with configurable detail level.

```json
{
  "type": "logging",
  "config": {
    "logLevel": "information",
    "logArguments": false,
    "logResult": false
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `logLevel` | string | `"information"` | Log level: `debug`, `information`, `warning`, `error` |
| `logArguments` | bool | `false` | Include tool arguments in log |
| `logResult` | bool | `false` | Include tool result in log |

#### Rate Limiting Hook

Prevents abuse by limiting request rates per user/client.

```json
{
  "type": "rateLimit",
  "config": {
    "maxRequests": 100,
    "windowSeconds": 60,
    "keyType": "client",
    "errorMessage": "Rate limit exceeded. Please try again later."
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `maxRequests` | int | `100` | Maximum requests per window |
| `windowSeconds` | int | `60` | Time window in seconds |
| `keyType` | string | `"client"` | Rate limit key: `client`, `tool`, `server`, `combined` |
| `errorMessage` | string | `"Rate limit exceeded..."` | Error message when limit exceeded |

**Key Types:**
- `client` - Rate limit by client/principal ID
- `tool` - Rate limit by tool name
- `server` - Rate limit by server name
- `combined` - Rate limit by combination of client, server, and tool

**Behavior:**
- Throws `RateLimitExceededException` when limit exceeded
- Uses `IMemoryCache` for rate tracking (can swap for distributed cache)
- User identity extracted from `AuthenticationResult.PrincipalId`

#### Timeout Hook

Enforces maximum execution time for tool calls.

```json
{
  "type": "timeout",
  "config": {
    "defaultTimeoutSeconds": 30,
    "perTool": {
      "long_running_tool": 120,
      "quick_tool": 5
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `defaultTimeoutSeconds` | int | `30` | Default timeout for all tools |
| `perTool` | object | `{}` | Per-tool timeout overrides (keys can be exact names or wildcard patterns) |

**Behavior:**
- Creates a linked `CancellationTokenSource` with timeout
- Tool calls exceeding timeout are cancelled
- Original cancellation token is preserved

#### Authorization Hook

Fine-grained role and scope-based access control integrating with Azure AD.

```json
{
  "type": "authorization",
  "config": {
    "requireAuthentication": true,
    "defaultAllow": false,
    "mode": "anyOf",
    "rules": [
      {
        "toolPattern": "admin_*",
        "serverPattern": "*",
        "requiredRoles": ["admin", "superuser"],
        "requiredScopes": [],
        "allow": true
      },
      {
        "toolPattern": "read_*",
        "requiredRoles": ["reader", "admin"],
        "allow": true
      },
      {
        "toolPattern": "*",
        "requiredRoles": ["user"],
        "allow": true
      }
    ]
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `requireAuthentication` | bool | `false` | Require authenticated user |
| `defaultAllow` | bool | `false` | Allow when no rules match |
| `mode` | string | `"anyOf"` | `anyOf` or `allOf` for multiple rules |
| `rules` | array | `[]` | Authorization rules |

**Rule Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `toolPattern` | string | Tool name pattern (supports `*` wildcards) |
| `serverPattern` | string | Server name pattern (supports `*` wildcards) |
| `requiredRoles` | string[] | Required roles (any match grants access) |
| `requiredScopes` | string[] | Required scopes (any match grants access) |
| `allow` | bool | Allow or deny when matched |

**Behavior:**
- Throws `AuthorizationException` when access denied
- Roles extracted from `AuthenticationResult.Properties["roles"]`
- Scopes extracted from `AuthenticationResult.Properties["scopes"]`
- Integrates with Azure AD when using OAuth/OIDC authentication

#### Retry Hook

Automatically retries transient failures with exponential backoff.

```json
{
  "type": "retry",
  "config": {
    "maxRetries": 3,
    "initialDelayMs": 100,
    "maxDelayMs": 5000,
    "backoffMultiplier": 2.0,
    "retryablePatterns": ["timeout", "connection", "unavailable", "503", "429"]
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `maxRetries` | int | `3` | Maximum retry attempts |
| `initialDelayMs` | int | `100` | Initial delay between retries |
| `maxDelayMs` | int | `5000` | Maximum delay between retries |
| `backoffMultiplier` | double | `2.0` | Exponential backoff multiplier |
| `retryablePatterns` | string[] | `[...]` | Error patterns that trigger retry |

**Behavior:**
- Sets `context.Items["McpProxy.Retry.Requested"] = true` on transient errors
- Proxy server handles retry loop with exponential backoff
- Jitter added to prevent thundering herd

#### Metrics Hook

Records detailed observability metrics using OpenTelemetry.

```json
{
  "type": "metrics",
  "config": {
    "recordTiming": true,
    "recordSizes": true,
    "recordErrors": true
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `recordTiming` | bool | `true` | Record tool call durations |
| `recordSizes` | bool | `true` | Record request/response sizes |
| `recordErrors` | bool | `true` | Record error counts |

**Metrics emitted:**
- `mcp_proxy_hook_tool_call_duration_ms` - Histogram of call durations
- `mcp_proxy_hook_tool_call_count` - Counter of tool calls
- `mcp_proxy_hook_tool_call_errors` - Counter of errors
- `mcp_proxy_hook_request_size_bytes` - Histogram of request sizes
- `mcp_proxy_hook_response_size_bytes` - Histogram of response sizes

#### Audit Hook

Compliance audit trail for tool invocations.

```json
{
  "type": "audit",
  "config": {
    "level": "standard",
    "includeSensitiveData": false,
    "sensitiveArguments": ["password", "secret", "token", "key"],
    "includeCorrelationId": true,
    "maxValueLength": 500,
    "excludeTools": ["health_check", "ping"],
    "includeTools": []
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `level` | string | `"standard"` | `basic`, `standard`, or `detailed` |
| `includeSensitiveData` | bool | `false` | Include sensitive args in logs |
| `sensitiveArguments` | string[] | `[...]` | Argument names to redact |
| `includeCorrelationId` | bool | `true` | Add correlation ID for tracking |
| `maxValueLength` | int | `500` | Max length of logged values |
| `excludeTools` | string[] | `[]` | Tools to exclude from auditing |
| `includeTools` | string[] | `[]` | Tools to include (empty = all) |

**Audit Levels:**
- `basic` - Tool name, server, timestamp, user
- `standard` - Basic + arguments (sensitive redacted)
- `detailed` - Standard + full response content

**Log format:**
```
[AUDIT] Tool=create_file Server=filesystem User=alice@contoso.com 
        CorrelationId=abc123 Timestamp=2024-01-15T10:30:00Z
        Arguments={path:"docs/readme.md", content:"[500 chars]"}
        Duration=245ms Success=true
```

#### Content Filter Hook

Blocks or redacts prohibited content in responses.

```json
{
  "type": "contentFilter",
  "config": {
    "useDefaultPatterns": true,
    "patterns": [
      {
        "name": "ssn",
        "pattern": "\\d{3}-\\d{2}-\\d{4}",
        "mode": "redact",
        "redactReplacement": "[SSN-REDACTED]"
      },
      {
        "name": "private_key",
        "pattern": "-----BEGIN.*PRIVATE KEY-----",
        "mode": "block",
        "blockMessage": "Private keys are not allowed in responses"
      },
      {
        "name": "credit_card",
        "pattern": "\\b\\d{4}[- ]?\\d{4}[- ]?\\d{4}[- ]?\\d{4}\\b",
        "mode": "redact",
        "redactReplacement": "[CARD-REDACTED]"
      }
    ]
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `useDefaultPatterns` | bool | `true` | Include built-in patterns |
| `patterns` | array | `[]` | Custom filter patterns |

**Pattern Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `name` | string | Pattern identifier for logging |
| `pattern` | string | Regex pattern to match |
| `mode` | string | `block` or `redact` |
| `redactReplacement` | string | Replacement text for redaction |
| `blockMessage` | string | Error message when blocking |

**Default Patterns (when `useDefaultPatterns: true`):**
- Social Security Numbers (SSN)
- Credit card numbers
- API keys and tokens
- Private keys
- AWS access keys

**Behavior:**
- `redact` mode: Replaces matched content with replacement text
- `block` mode: Returns error result with block message
- Processes all text content in response

#### Input Transform Hook

Provides default values for tool input arguments. Transformations (functions) can only be configured programmatically, not via JSON configuration.

```json
{
  "type": "inputTransform",
  "config": {
    "defaults": {
      "timeout": 30,
      "format": "json"
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `defaults` | object | `null` | Default values for missing arguments |

#### Output Transform Hook

Transforms tool output before returning to the client.

```json
{
  "type": "outputTransform",
  "config": {
    "redactPatterns": ["password", "secret", "token", "api[_-]?key"],
    "redactedValue": "[REDACTED]"
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `redactPatterns` | string[] | `[]` | Regex patterns to redact from output |
| `redactedValue` | string | `"[REDACTED]"` | Replacement text for redacted content |

Matched patterns are replaced with the `redactedValue`.

### Hook Pipeline

Hooks execute in order of priority (lower numbers first):

```
Request
   │
   ▼
┌────────────────────┐
│ PreInvoke Hook 1   │ (priority: 1)
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ PreInvoke Hook 2   │ (priority: 2)
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ Backend Tool Call  │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ PostInvoke Hook 1  │ (priority: 1)
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ PostInvoke Hook 2  │ (priority: 2)
└─────────┬──────────┘
          │
          ▼
      Response
```

### Hook Context

Hooks can share data through the `HookContext`:

```csharp
// In pre-invoke hook
context.Items["startTime"] = DateTime.UtcNow;

// In post-invoke hook
var startTime = (DateTime)context.Items["startTime"];
var duration = DateTime.UtcNow - startTime;
```

### Custom Hooks (Programmatic)

For advanced use cases, implement custom hooks in code:

```csharp
public class ValidationHook : IPreInvokeHook
{
    public int Priority => 0;

    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var args = context.Request.Arguments;
        
        // Validate arguments
        if (args is not null && args.TryGetValue("path", out var path))
        {
            if (path.ToString()?.Contains("..") == true)
            {
                throw new InvalidOperationException("Path traversal not allowed");
            }
        }
        
        return ValueTask.CompletedTask;
    }
}
```

Register custom hooks with the `HookFactory`:

```csharp
var hookFactory = new HookFactory(loggerFactory, memoryCache, metrics);
hookFactory.RegisterHookType("validation", 
    (definition, factory) => new ValidationHook());
```

### Practical Examples

#### Audit Logging

Log all tool calls with timing:

```json
{
  "hooks": {
    "preInvoke": [
      {
        "type": "logging",
        "config": {
          "logLevel": "information",
          "logArguments": true
        }
      }
    ],
    "postInvoke": [
      {
        "type": "logging",
        "config": {
          "logLevel": "information",
          "logResults": true
        }
      }
    ]
  }
}
```

#### Security Redaction

Remove sensitive data from responses:

```json
{
  "hooks": {
    "postInvoke": [
      {
        "type": "outputTransform",
        "config": {
          "redactPatterns": [
            "password",
            "secret",
            "token",
            "api[_-]?key",
            "bearer\\s+[a-zA-Z0-9\\-._~+/]+=*",
            "[a-zA-Z0-9]{32,}"
          ]
        }
      }
    ]
  }
}
```

#### Enterprise Security Configuration

A complete example for enterprise deployments with rate limiting, authorization, content filtering, and audit logging:

```json
{
  "mcp": {
    "production-server": {
      "type": "http",
      "url": "https://mcp.internal.example.com",
      "hooks": {
        "preInvoke": [
          {
            "type": "audit",
            "config": {
              "level": "standard",
              "includeCorrelationId": true
            }
          },
          {
            "type": "rateLimit",
            "config": {
              "maxRequests": 100,
              "windowSeconds": 60,
              "keyType": "client"
            }
          },
          {
            "type": "timeout",
            "config": {
              "defaultTimeoutSeconds": 30,
              "perTool": {
                "generate_report": 300
              }
            }
          },
          {
            "type": "authorization",
            "config": {
              "requireAuthentication": true,
              "defaultAllow": false,
              "rules": [
                {
                  "toolPattern": "admin_*",
                  "requiredRoles": ["admin"]
                },
                {
                  "toolPattern": "write_*",
                  "requiredRoles": ["editor", "admin"]
                },
                {
                  "toolPattern": "read_*",
                  "requiredRoles": ["reader", "editor", "admin"]
                }
              ]
            }
          }
        ],
        "postInvoke": [
          {
            "type": "retry",
            "config": {
              "maxRetries": 3,
              "retryablePatterns": ["timeout", "503", "connection"]
            }
          },
          {
            "type": "contentFilter",
            "config": {
              "useDefaultPatterns": true,
              "patterns": [
                {
                  "name": "internal_ip",
                  "pattern": "10\\.\\d+\\.\\d+\\.\\d+",
                  "mode": "redact",
                  "redactReplacement": "[INTERNAL-IP]"
                }
              ]
            }
          },
          {
            "type": "metrics",
            "config": {
              "recordTiming": true,
              "recordSizes": true,
              "recordErrors": true
            }
          },
          {
            "type": "audit",
            "config": {
              "level": "standard"
            }
          }
        ]
      }
    }
  }
}
```

This configuration provides:
- **Rate limiting**: Max 100 requests per minute per user
- **Timeouts**: 30s default, 5 minutes for report generation
- **Authorization**: Role-based access with admin/editor/reader roles
- **Retry**: Auto-retry on transient failures (timeout, 503, connection errors)
- **Content filtering**: Redact SSNs, credit cards, private keys, and internal IPs
- **Metrics**: Full observability with timing and size metrics
- **Audit**: Complete audit trail with correlation IDs

## Sample Projects

For hands-on examples of these features, see the sample projects:

### JSON Configuration Samples

- **[03-tool-filtering](https://github.com/MoaidHathot/mcp-proxy/tree/main/samples/03-tool-filtering)** - Allowlist, denylist, and regex filtering examples
- **[06-hooks](https://github.com/MoaidHathot/mcp-proxy/tree/main/samples/06-hooks)** - Logging, rate limiting, audit, and content filtering hooks
- **[10-enterprise-complete](https://github.com/MoaidHathot/mcp-proxy/tree/main/samples/10-enterprise-complete)** - Full enterprise configuration with all security features

### SDK/Programmatic Samples

- **[12-sdk-hooks-interceptors](https://github.com/MoaidHathot/mcp-proxy/tree/main/samples/12-sdk-hooks-interceptors)** - Code-based hooks and interceptors
- **[13-sdk-virtual-tools](https://github.com/MoaidHathot/mcp-proxy/tree/main/samples/13-sdk-virtual-tools)** - Tool filtering and modification via code
