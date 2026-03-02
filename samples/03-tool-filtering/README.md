# Tool Filtering

This example demonstrates how to filter which tools are exposed through the proxy. Tool filtering is essential for security, access control, and providing focused toolsets to clients.

## Features Demonstrated

- **Allowlist Mode**: Only expose tools matching specific patterns
- **Denylist Mode**: Expose all tools except those matching patterns
- **Wildcard Patterns**: Use `*` and `?` for flexible matching
- **Case-Insensitive Matching**: Optional case-insensitive pattern matching
- **Multiple Filtered Servers**: Same backend server with different filter configurations

## How It Works

Tool filtering is applied before tools are exposed to clients. The proxy intercepts tool list requests and filters out tools based on the configured patterns.

```
                                    ┌─────────────────────────────────────┐
                                    │           Filesystem Server         │
                                    │  (read_file, write_file, delete_file,│
                                    │   list_directory, create_directory)  │
                                    └─────────────────────────────────────┘
                                                      │
                                    ┌─────────────────┴─────────────────┐
                                    │                                   │
                              ┌─────┴─────┐                       ┌─────┴─────┐
                              │ Allowlist │                       │ Allowlist │
                              │ read_*    │                       │ write_*   │
                              │ list_*    │                       │ create_*  │
                              └─────┬─────┘                       └─────┬─────┘
                                    │                                   │
                              ┌─────┴─────┐                       ┌─────┴─────┐
                              │ fs_read_  │                       │fsw_write_ │
                              │ fs_list_  │                       │fsw_create_│
                              └───────────┘                       └───────────┘
```

## Filter Modes

### 1. None (Default)
No filtering - all tools are exposed.

```json
{
  "filter": {
    "mode": "none"
  }
}
```

### 2. Allowlist
Only tools matching at least one pattern are exposed.

```json
{
  "filter": {
    "mode": "allowlist",
    "patterns": ["read_*", "get_*"]
  }
}
```

### 3. Denylist
All tools are exposed except those matching any pattern.

```json
{
  "filter": {
    "mode": "denylist",
    "patterns": ["delete_*", "remove_*"]
  }
}
```

### 4. Regex
Use regular expressions for complex matching rules.

```json
{
  "filter": {
    "mode": "regex",
    "include": ["^(read|list)_.*$"],
    "exclude": [".*_internal$"]
  }
}
```

## Pattern Syntax

| Pattern | Description | Example Matches |
|---------|-------------|-----------------|
| `*` | Matches any sequence of characters | `read_*` matches `read_file`, `read_directory` |
| `?` | Matches any single character | `get_?` matches `get_a`, `get_1` |
| `read_*` | Prefix match | `read_file`, `read_json`, `read_config` |
| `*_file` | Suffix match | `read_file`, `write_file`, `delete_file` |
| `*config*` | Contains match | `get_config`, `config_load`, `my_config_file` |

## Configuration in This Example

### Read-Only Filesystem
```json
{
  "filter": {
    "mode": "allowlist",
    "patterns": ["read_*", "list_*", "search_*", "get_*"]
  }
}
```
Only exposes tools for reading and listing - no write or delete operations.

### Write-Only Filesystem
```json
{
  "filter": {
    "mode": "allowlist",
    "patterns": ["write_*", "create_*", "move_*", "delete_*"]
  }
}
```
Only exposes tools for writing and modifying files.

### Memory Server (No Delete)
```json
{
  "filter": {
    "mode": "denylist",
    "patterns": ["delete_*", "remove_*", "clear_*"]
  }
}
```
Exposes all tools except those that delete data.

## Running the Example

```bash
mcpproxy -t stdio -c ./mcp-proxy.json
```

## Available Tools After Filtering

### Filesystem Read-Only (`fs_*`)
- `fs_read_file`
- `fs_list_directory`
- `fs_search_files`
- `fs_get_file_info`

### Filesystem Write-Only (`fsw_*`)
- `fsw_write_file`
- `fsw_create_directory`
- `fsw_move_file`

### Memory (`mem_*`)
- `mem_create_entities`
- `mem_create_relations`
- `mem_search_nodes`
- `mem_read_graph`
- ~~`mem_delete_entities`~~ (blocked by denylist)

## Use Cases

1. **Security**: Prevent LLMs from accessing dangerous operations
2. **Role-Based Access**: Different filter configs for different user roles
3. **Focused Tools**: Provide only relevant tools for specific tasks
4. **Testing**: Isolate read operations during testing

## Resource and Prompt Filtering

The same filtering mechanisms work for resources and prompts:

```json
{
  "resources": {
    "filter": {
      "mode": "allowlist",
      "patterns": ["config:*", "data:*"]
    }
  },
  "prompts": {
    "filter": {
      "mode": "denylist",
      "patterns": ["admin_*", "internal_*"]
    }
  }
}
```

## Next Steps

- See [04-remote-servers](../04-remote-servers) to learn how to connect to remote MCP servers
- See [05-http-api-key-auth](../05-http-api-key-auth) to add authentication to your proxy
- See [06-hooks](../06-hooks) for runtime filtering with hooks
