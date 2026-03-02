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

**Output example:**

```
[INF] Tool invocation: server=github, tool=create_issue
[DBG] Tool arguments: {"title": "Bug fix", "body": "..."}
[INF] Tool completed: server=github, tool=create_issue, duration=245ms
```

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
context.Properties["startTime"] = DateTime.UtcNow;

// In post-invoke hook
var startTime = (DateTime)context.Properties["startTime"];
var duration = DateTime.UtcNow - startTime;
```

### Custom Hooks (Programmatic)

For advanced use cases, implement custom hooks in code:

```csharp
public class ValidationHook : IPreInvokeHook
{
    public int Priority => 0;

    public Task<HookResult> OnPreInvokeAsync(
        HookContext context,
        CancellationToken cancellationToken)
    {
        var args = context.Arguments;
        
        // Validate arguments
        if (args.TryGetValue("path", out var path))
        {
            if (path.ToString().Contains(".."))
            {
                return Task.FromResult(HookResult.Reject(
                    "Path traversal not allowed"));
            }
        }
        
        return Task.FromResult(HookResult.Continue());
    }
}
```

Register custom hooks with the `HookFactory`:

```csharp
var hookFactory = new HookFactory();
hookFactory.RegisterHookType("validation", 
    config => new ValidationHook());
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
