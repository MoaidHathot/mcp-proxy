# Per-Server Routing Mode

This example demonstrates the per-server routing mode, where each backend MCP server is exposed on its own dedicated HTTP route. This provides clear separation between servers and enables independent access control.

## Features Demonstrated

- **Per-Server Routes**: Each server gets its own endpoint
- **Independent Access**: Call specific servers directly
- **Clear URL Structure**: Intuitive API design
- **Custom Base Path**: Configure the API base path
- **Combined with Authentication**: Secure all endpoints

## Routing Modes Comparison

### Unified Mode (Default)
All servers aggregated on a single endpoint. Tools from different servers are distinguished by prefixes.

```
/mcp/sse           → All servers
/mcp/message       → Route by tool prefix
```

### Per-Server Mode
Each server has its own dedicated endpoints.

```
/api/filesystem/tools/list    → Filesystem server
/api/time/tools/call          → Time server
/api/memory/prompts/get       → Memory server
```

## Architecture

```
                                    ┌─────────────────────┐
                                    │    /api/filesystem  │───► Filesystem Server
                                    │    /api/time        │───► Time Server
GET /api/filesystem/tools/list ────►│    /api/memory      │───► Memory Server
                                    └─────────────────────┘
```

## Configuration

```json
{
  "proxy": {
    "routing": {
      "mode": "perServer",    // Enable per-server routing
      "basePath": "/api"      // Base path for all endpoints
    }
  },
  "mcp": {
    "filesystem": { ... },    // Available at /api/filesystem/*
    "time": { ... },          // Available at /api/time/*
    "memory": { ... }         // Available at /api/memory/*
  }
}
```

## HTTP Endpoints

Each server exposes the following endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/{server}/sse` | GET | SSE connection for this server |
| `/api/{server}/message` | POST | Send JSON-RPC messages |
| `/api/{server}/tools/list` | POST | List tools from this server |
| `/api/{server}/tools/call` | POST | Call a tool on this server |
| `/api/{server}/resources/list` | POST | List resources |
| `/api/{server}/resources/read` | POST | Read a resource |
| `/api/{server}/resources/templates/list` | POST | List resource templates |
| `/api/{server}/prompts/list` | POST | List prompts |
| `/api/{server}/prompts/get` | POST | Get a prompt |

## Running the Example

### 1. Set API Key

```bash
export MCP_PROXY_API_KEY="your-api-key"
```

### 2. Start the Proxy

```bash
mcpproxy -t sse -c ./mcp-proxy.json -p 5000
```

### 3. List Available Servers

```bash
curl -H "X-API-Key: your-api-key" http://localhost:5000/api/
```

Response:
```json
{
  "servers": [
    {"name": "filesystem", "title": "Filesystem Server"},
    {"name": "time", "title": "Time Server"},
    {"name": "memory", "title": "Memory Server"}
  ]
}
```

## API Examples

### List Tools from Filesystem Server

```bash
curl -X POST \
  -H "X-API-Key: your-api-key" \
  -H "Content-Type: application/json" \
  http://localhost:5000/api/filesystem/tools/list
```

Response:
```json
{
  "tools": [
    {"name": "read_file", "description": "Read file contents"},
    {"name": "write_file", "description": "Write file contents"},
    {"name": "list_directory", "description": "List directory contents"}
  ]
}
```

### Call a Tool on Time Server

```bash
curl -X POST \
  -H "X-API-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"name": "get_current_time", "arguments": {}}' \
  http://localhost:5000/api/time/tools/call
```

### SSE Connection to Specific Server

```bash
curl -N -H "X-API-Key: your-api-key" \
  http://localhost:5000/api/memory/sse
```

## Use Cases

### 1. Microservices Architecture
Each MCP server represents a different service, and clients connect to specific services.

```
/api/users/       → User management MCP server
/api/orders/      → Order processing MCP server
/api/inventory/   → Inventory MCP server
```

### 2. Multi-Tenant Access
Different clients access different subsets of servers.

```
Client A → /api/analytics/     (analytics tools)
Client B → /api/filesystem/    (file tools)
Client C → /api/database/      (database tools)
```

### 3. API Gateway Pattern
The proxy acts as an API gateway with clear routing.

```
Frontend → /api/public/        (public tools)
Admin   → /api/admin/          (admin tools)
```

## Combining with Unified Mode

You can also access all servers through unified endpoints:

```json
{
  "routing": {
    "mode": "perServer",
    "basePath": "/api",
    "includeUnified": true,
    "unifiedPath": "/mcp"
  }
}
```

This exposes:
- `/api/{server}/*` - Per-server endpoints
- `/mcp/*` - Unified endpoint (all servers)

## Per-Server Authentication

Apply different authentication to different servers:

```json
{
  "mcp": {
    "public-tools": {
      "type": "stdio",
      "command": "...",
      "authentication": {
        "required": false
      }
    },
    "admin-tools": {
      "type": "stdio",
      "command": "...",
      "authentication": {
        "required": true,
        "requiredRoles": ["admin"]
      }
    }
  }
}
```

## Error Handling

| Status | Description |
|--------|-------------|
| 404 | Server not found |
| 401 | Authentication required |
| 403 | Access denied to this server |
| 502 | Backend server error |

Example error response:
```json
{
  "error": {
    "code": -32001,
    "message": "Server 'unknown' not found"
  }
}
```

## OpenAPI Documentation

Per-server mode generates clear OpenAPI documentation:

```yaml
paths:
  /api/filesystem/tools/list:
    post:
      summary: List filesystem tools
      tags: [filesystem]
  /api/time/tools/list:
    post:
      summary: List time tools
      tags: [time]
```

## Next Steps

- See [10-enterprise-complete](../10-enterprise-complete) for a complete production setup
- See [05-http-api-key-auth](../05-http-api-key-auth) for authentication details
