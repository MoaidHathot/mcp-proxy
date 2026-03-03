# Debugging Features Example

This sample demonstrates the debugging features of MCP Proxy:

1. **Hook Execution Tracing** - Detailed visibility into hook pipeline execution with timing
2. **Request/Response Dumping** - Full payload dumps to files for troubleshooting  
3. **Connection Health Dashboard** - `/debug/health` endpoint (localhost-only) for SSE mode

## Configuration

### Debug Settings

```json
{
  "proxy": {
    "debug": {
      "hookTracing": true,
      "healthEndpoint": true,
      "healthPath": "/debug/health",
      "dump": {
        "enabled": true,
        "requests": true,
        "responses": true,
        "outputPath": "./dumps",
        "format": "json",
        "includeTimestamp": true
      }
    }
  }
}
```

### Dump Hook

The `dump` hook can be added to any server's hooks to capture request/response payloads:

```json
{
  "hooks": {
    "preInvoke": [
      {
        "type": "dump",
        "config": {
          "dumpRequests": true,
          "dumpResponses": false
        }
      }
    ],
    "postInvoke": [
      {
        "type": "dump",
        "config": {
          "dumpRequests": false,
          "dumpResponses": true
        }
      }
    ]
  }
}
```

## Running the Example

### HTTP/SSE Mode (recommended for debugging)

```bash
mcp-proxy --transport http --config samples/14-debugging/mcp-proxy.json --port 5000 --verbose
```

### Access the Health Endpoint

Once running in HTTP mode, access the health dashboard from localhost:

```bash
curl http://localhost:5000/debug/health
```

Example response:

```json
{
  "status": "healthy",
  "uptimeSeconds": 123.45,
  "timestamp": "2025-03-02T12:00:00Z",
  "version": "1.0.0",
  "totalRequests": 42,
  "failedRequests": 0,
  "activeConnections": 2,
  "backends": {
    "filesystem": {
      "name": "filesystem",
      "status": "healthy",
      "connected": true,
      "lastSuccessfulRequest": "2025-03-02T11:59:55Z",
      "totalRequests": 30,
      "failedRequests": 0,
      "averageResponseTimeMs": 15.5,
      "toolCount": 5,
      "consecutiveFailures": 0
    },
    "memory": {
      "name": "memory",
      "status": "healthy",
      "connected": true,
      "lastSuccessfulRequest": "2025-03-02T11:59:50Z",
      "totalRequests": 12,
      "failedRequests": 0,
      "averageResponseTimeMs": 8.2,
      "toolCount": 4,
      "consecutiveFailures": 0
    }
  }
}
```

## Dump Files

Request and response payloads are written to the `./dumps` directory with timestamped filenames:

```
dumps/
├── filesystem_read_file_request_20250302_120000_123.json
├── filesystem_read_file_response_20250302_120000_456.json
├── memory_create_entity_request_20250302_120005_789.json
└── memory_create_entity_response_20250302_120005_012.json
```

## Health Status Values

| Status | Description |
|--------|-------------|
| `healthy` | All backends connected and responding normally |
| `degraded` | Some backends experiencing issues but still functional |
| `unhealthy` | Critical failures, most backends unavailable |
| `unknown` | Unable to determine health status |

## Security Note

The `/debug/health` endpoint is restricted to localhost access only. Requests from non-loopback IP addresses will receive HTTP 403 Forbidden.

## Hook Tracing

When `hookTracing` is enabled, detailed logs are emitted for each hook execution:

```
[DBG] Hook trace started for tool 'read_file' on server 'filesystem'
[DBG] Hook 'LoggingHook' (Pre, priority 0) executing
[DBG] Hook 'LoggingHook' completed in 0.52ms
[DBG] Hook 'DumpHook' (Pre, priority 0) executing
[DBG] Hook 'DumpHook' completed in 2.15ms
[INF] Hook trace completed for 'read_file' on 'filesystem': 2 hooks (2 completed, 0 failed) in 2.67ms
```
