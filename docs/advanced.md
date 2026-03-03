---
layout: default
title: Advanced Features
description: Telemetry, caching, notifications, and advanced MCP protocol support
---

## Advanced Features

MCP Proxy includes advanced features for production deployments and complex use cases.

<div class="toc">
<h4>On this page</h4>
<ul>
<li><a href="#telemetry-opentelemetry">Telemetry (OpenTelemetry)</a></li>
<li><a href="#caching">Caching</a></li>
<li><a href="#notifications">Notifications</a></li>
<li><a href="#resource-subscriptions">Resource Subscriptions</a></li>
<li><a href="#advanced-mcp-protocol">Advanced MCP Protocol</a></li>
</ul>
</div>

## Telemetry (OpenTelemetry)

MCP Proxy includes built-in OpenTelemetry integration for observability in production environments.

### Configuration

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

### Metrics

MCP Proxy exports the following metrics:

| Metric | Type | Description |
|--------|------|-------------|
| `mcpproxy.tool_calls.total` | Counter | Total tool calls |
| `mcpproxy.tool_calls.successful` | Counter | Successful tool calls |
| `mcpproxy.tool_calls.failed` | Counter | Failed tool calls |
| `mcpproxy.tool_call.duration` | Histogram | Tool call duration (ms) |
| `mcpproxy.resource_reads.total` | Counter | Total resource reads |
| `mcpproxy.resource_read.duration` | Histogram | Resource read duration (ms) |
| `mcpproxy.prompt_gets.total` | Counter | Total prompt gets |
| `mcpproxy.prompt_get.duration` | Histogram | Prompt get duration (ms) |
| `mcpproxy.backend_connections.active` | UpDownCounter | Active backend connections |

All metrics include a `server` tag identifying the backend server.

### Tracing

MCP Proxy creates spans for all MCP operations:

| Activity | Description |
|----------|-------------|
| `mcpproxy.tool_call` | Individual tool call |
| `mcpproxy.resource_read` | Resource read operation |
| `mcpproxy.prompt_get` | Prompt get operation |
| `mcpproxy.list_tools` | List tools operation |
| `mcpproxy.list_resources` | List resources operation |
| `mcpproxy.list_prompts` | List prompts operation |

Spans include tags:
- `mcp.server`: Backend server name
- `mcp.tool`, `mcp.resource`, or `mcp.prompt`: Item name
- `mcp.operation`: Operation type
- `error.type`, `error.message`: On failures

### Exporters

#### Console Exporter (Debugging)

Enable console output for debugging:

```json
{
  "telemetry": {
    "metrics": { "consoleExporter": true },
    "tracing": { "consoleExporter": true }
  }
}
```

#### OTLP Exporter (Production)

Export to an OpenTelemetry collector:

```json
{
  "telemetry": {
    "metrics": { "otlpEndpoint": "http://otel-collector:4317" },
    "tracing": { "otlpEndpoint": "http://otel-collector:4317" }
  }
}
```

### Integration Examples

#### Jaeger

```yaml
# docker-compose.yml
services:
  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686"  # UI
      - "4317:4317"    # OTLP gRPC
```

```json
{
  "telemetry": {
    "tracing": { "otlpEndpoint": "http://localhost:4317" }
  }
}
```

#### Prometheus + Grafana

```yaml
services:
  otel-collector:
    image: otel/opentelemetry-collector:latest
    command: ["--config=/etc/otel-collector.yaml"]
    volumes:
      - ./otel-collector.yaml:/etc/otel-collector.yaml
    ports:
      - "4317:4317"
      - "8889:8889"  # Prometheus metrics
```

## Caching

MCP Proxy caches tool lists from backend servers to reduce latency and load.

### Configuration

```json
{
  "proxy": {
    "caching": {
      "tools": {
        "enabled": true,
        "ttlSeconds": 60
      }
    }
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `tools.enabled` | bool | `true` | Enable caching |
| `tools.ttlSeconds` | int | `60` | Cache TTL in seconds |

### Cache Invalidation

The cache is automatically invalidated when:

1. **TTL expires**: Cache entries expire after the configured TTL
2. **List changed notification**: Backend sends `tools/list_changed`
3. **Manual invalidation**: Via programmatic API

### How It Works

```
Client: ListTools
    │
    ▼
┌─────────────┐
│ Check Cache │
└──────┬──────┘
       │
  ┌────┴────┐
  │ Cache   │ Cache Miss
  │ Hit?    │────────────┐
  └────┬────┘            │
       │ Yes             │
       ▼                 ▼
┌─────────────┐   ┌─────────────┐
│ Return      │   │ Fetch from  │
│ Cached List │   │ Backends    │
└─────────────┘   └──────┬──────┘
                         │
                         ▼
                  ┌─────────────┐
                  │ Update      │
                  │ Cache       │
                  └──────┬──────┘
                         │
                         ▼
                  ┌─────────────┐
                  │ Return List │
                  └─────────────┘
```

## Notifications

MCP Proxy forwards notifications between backend servers and clients.

### Supported Notifications

| Notification | Direction | Description |
|--------------|-----------|-------------|
| `notifications/progress` | Backend → Client | Progress updates for long operations |
| `tools/list_changed` | Backend → Client | Tool list has changed |
| `resources/list_changed` | Backend → Client | Resource list has changed |
| `resources/updated` | Backend → Client | Specific resource was updated |
| `prompts/list_changed` | Backend → Client | Prompt list has changed |

### Progress Notifications

When a backend tool reports progress, the proxy forwards it to the client:

```
Backend Server                Proxy                    Client
      │                         │                        │
      │ progress: 25%           │                        │
      ├────────────────────────►│                        │
      │                         │ progress: 25%          │
      │                         ├───────────────────────►│
      │                         │                        │
      │ progress: 50%           │                        │
      ├────────────────────────►│                        │
      │                         │ progress: 50%          │
      │                         ├───────────────────────►│
      │                         │                        │
```

### List Changed Notifications

When a backend's capabilities change, clients are notified:

```json
// Backend sends:
{ "method": "tools/list_changed" }

// Proxy forwards to client:
{ "method": "tools/list_changed" }

// Proxy also invalidates its cache for that server
```

## Resource Subscriptions

Clients can subscribe to resource updates from backend servers.

### How It Works

1. Client subscribes to a resource via the proxy
2. Proxy tracks the subscription and forwards it to the backend
3. When the resource changes, backend notifies the proxy
4. Proxy forwards the notification to subscribed clients

```
Client                        Proxy                    Backend
   │                            │                         │
   │ Subscribe(resource)        │                         │
   ├───────────────────────────►│                         │
   │                            │ Subscribe(resource)     │
   │                            ├────────────────────────►│
   │                            │                         │
   │                            │ (time passes...)        │
   │                            │                         │
   │                            │ resource/updated        │
   │                            │◄────────────────────────┤
   │ resource/updated           │                         │
   │◄───────────────────────────┤                         │
   │                            │                         │
```

### Subscription Management

The proxy tracks active subscriptions per client session:

- Subscriptions are automatically cleaned up when clients disconnect
- Multiple clients can subscribe to the same resource
- Unsubscribe removes only that client's subscription

## Advanced MCP Protocol

MCP Proxy supports advanced MCP protocol features by forwarding requests between backends and clients.

### Sampling

Backend MCP servers can request LLM completions from the client through the proxy.

**Use case**: A backend tool needs to generate text using the client's LLM.

```
Backend Server                Proxy                    Client
      │                         │                        │
      │ sampling/create         │                        │
      ├────────────────────────►│                        │
      │                         │ sampling/create        │
      │                         ├───────────────────────►│
      │                         │                        │
      │                         │ sampling/result        │
      │                         │◄───────────────────────┤
      │ sampling/result         │                        │
      │◄────────────────────────┤                        │
      │                         │                        │
```

**Configuration**:

```json
{
  "proxy": {
    "capabilities": {
      "client": {
        "sampling": true
      }
    }
  }
}
```

### Elicitation

Backend MCP servers can request structured user input through the proxy.

**Use case**: A backend tool needs additional information from the user to complete an operation.

```
Backend Server                Proxy                    Client
      │                         │                        │
      │ elicit request          │                        │
      ├────────────────────────►│                        │
      │                         │ elicit request         │
      │                         ├───────────────────────►│
      │                         │                        │
      │                         │ elicit response        │
      │                         │◄───────────────────────┤
      │ elicit response         │                        │
      │◄────────────────────────┤                        │
      │                         │                        │
```

**Configuration**:

```json
{
  "proxy": {
    "capabilities": {
      "client": {
        "elicitation": true
      }
    }
  }
}
```

### Roots

Backend MCP servers can request file system root information from the client.

**Use case**: A backend tool needs to know the client's workspace roots.

**Configuration**:

```json
{
  "proxy": {
    "capabilities": {
      "client": {
        "roots": true
      }
    }
  }
}
```

### Graceful Degradation

If the connected client doesn't support a capability:

| Capability | Fallback Behavior |
|------------|-------------------|
| Sampling | Returns error to backend |
| Elicitation | Returns "decline" action |
| Roots | Returns empty roots list |

This allows backend servers to handle unsupported capabilities gracefully.

### Experimental Capabilities

Both client and server can advertise experimental capabilities:

```json
{
  "proxy": {
    "capabilities": {
      "client": {
        "experimental": {
          "customFeature": { "enabled": true, "version": "1.0" }
        }
      },
      "server": {
        "experimental": {
          "proxyMetadata": { "supported": true }
        }
      }
    }
  }
}
```

These are passed through during capability negotiation, allowing custom protocol extensions.

## Sample Projects

For hands-on examples of advanced features, see these sample projects:

| Sample | Description |
|--------|-------------|
| [08-caching](../samples/08-caching/) | Tool caching configuration with TTL |
| [09-telemetry](../samples/09-telemetry/) | OpenTelemetry metrics and tracing setup |
| [12-sdk-hooks-interceptors](../samples/12-sdk-hooks-interceptors/) | Programmatic hooks and interceptors |

See the [samples README](../samples/README.md) for the complete list of 13 samples.
