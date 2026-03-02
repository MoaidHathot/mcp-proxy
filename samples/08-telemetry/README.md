# OpenTelemetry Integration

This example demonstrates how to integrate MCP Proxy with OpenTelemetry for comprehensive observability including metrics and distributed tracing.

## Features Demonstrated

- **Metrics Collection**: Track tool calls, durations, and errors
- **Distributed Tracing**: Trace requests across the proxy and backends
- **Console Exporter**: Quick debugging output
- **OTLP Exporter**: Production-ready telemetry export
- **Custom Attributes**: Add context to metrics and traces
- **Metrics Hook**: Per-server metric collection

## Telemetry Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         MCP PROXY                                   │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐             │
│  │   Metrics    │  │   Tracing    │  │   Logging    │             │
│  │  Collector   │  │  Collector   │  │              │             │
│  └──────┬───────┘  └──────┬───────┘  └──────────────┘             │
│         │                 │                                        │
│         └────────┬────────┘                                        │
│                  │                                                 │
│           ┌──────┴──────┐                                         │
│           │    OTLP     │                                         │
│           │  Exporter   │                                         │
│           └──────┬──────┘                                         │
└──────────────────┼──────────────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    TELEMETRY BACKEND                                │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │
│  │  Prometheus  │  │    Jaeger    │  │    Grafana   │              │
│  │   (Metrics)  │  │  (Tracing)   │  │ (Dashboards) │              │
│  └──────────────┘  └──────────────┘  └──────────────┘              │
└──────────────────────────────────────────────────────────────────────┘
```

## Configuration Options

### Basic Configuration (Console Output)

```json
{
  "telemetry": {
    "enabled": true,
    "serviceName": "mcp-proxy",
    "serviceVersion": "1.0.0",
    "metrics": {
      "enabled": true,
      "consoleExporter": true
    },
    "tracing": {
      "enabled": true,
      "consoleExporter": true
    }
  }
}
```

### Production Configuration (OTLP)

```json
{
  "telemetry": {
    "enabled": true,
    "serviceName": "mcp-proxy",
    "serviceVersion": "1.0.0",
    "metrics": {
      "enabled": true,
      "consoleExporter": false,
      "otlpEndpoint": "http://otel-collector:4317"
    },
    "tracing": {
      "enabled": true,
      "consoleExporter": false,
      "otlpEndpoint": "http://otel-collector:4317"
    }
  }
}
```

## Collected Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `mcp.proxy.tool.calls` | Counter | Total tool invocations |
| `mcp.proxy.tool.duration` | Histogram | Tool execution duration |
| `mcp.proxy.tool.errors` | Counter | Tool invocation errors |
| `mcp.proxy.resource.reads` | Counter | Resource read operations |
| `mcp.proxy.prompt.gets` | Counter | Prompt retrieval operations |
| `mcp.proxy.connections.active` | Gauge | Active client connections |

### Metric Attributes

| Attribute | Description |
|-----------|-------------|
| `server` | Backend server name |
| `tool` | Tool name |
| `status` | success/error |
| `error.type` | Error type (on failure) |

## Trace Spans

| Span | Description |
|------|-------------|
| `mcp.proxy.tool.call` | Root span for tool invocation |
| `mcp.proxy.tool.route` | Routing decision |
| `mcp.backend.invoke` | Backend invocation |
| `mcp.hook.pre` | Pre-invoke hook execution |
| `mcp.hook.post` | Post-invoke hook execution |

## Running the Example

### Console Output (Development)

```bash
mcpproxy -t stdio -c ./mcp-proxy.json -v
```

You'll see telemetry output like:
```
[METRICS] mcp.proxy.tool.calls: 1 {server=filesystem, tool=read_file}
[TRACE] mcp.proxy.tool.call started (trace_id=abc123)
[TRACE] mcp.backend.invoke completed (duration=142ms)
```

### With OTLP Backend (Production)

1. Start the OTEL Collector (see docker-compose.yml)
2. Use the OTLP configuration:

```bash
mcpproxy -t sse -c ./mcp-proxy-otlp.json -p 5000
```

## Docker Compose Setup

Create `docker-compose.yml`:

```yaml
version: '3.8'

services:
  mcp-proxy:
    build: .
    ports:
      - "5000:5000"
    environment:
      - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
    command: ["-t", "sse", "-c", "/app/mcp-proxy-otlp.json", "-p", "5000"]

  otel-collector:
    image: otel/opentelemetry-collector-contrib:latest
    command: ["--config=/etc/otel-collector-config.yaml"]
    volumes:
      - ./otel-collector-config.yaml:/etc/otel-collector-config.yaml
    ports:
      - "4317:4317"   # OTLP gRPC
      - "4318:4318"   # OTLP HTTP

  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686" # UI
      - "14250:14250" # gRPC

  prometheus:
    image: prom/prometheus:latest
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    ports:
      - "9090:9090"

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
```

## OTEL Collector Configuration

Create `otel-collector-config.yaml`:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

processors:
  batch:
    timeout: 1s
    send_batch_size: 1024

exporters:
  prometheus:
    endpoint: "0.0.0.0:8889"
  
  jaeger:
    endpoint: jaeger:14250
    tls:
      insecure: true

  logging:
    loglevel: debug

service:
  pipelines:
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [prometheus, logging]
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [jaeger, logging]
```

## Metrics Hook

Add custom metrics per server:

```json
{
  "hooks": {
    "preInvoke": [
      {
        "type": "metrics",
        "config": {
          "recordCount": true,
          "attributes": {
            "server": "my-server",
            "environment": "production"
          }
        }
      }
    ],
    "postInvoke": [
      {
        "type": "metrics",
        "config": {
          "recordDuration": true,
          "recordErrors": true
        }
      }
    ]
  }
}
```

## Viewing Telemetry

### Jaeger (Traces)
Open http://localhost:16686 and search for service `mcp-proxy`

### Prometheus (Metrics)
Open http://localhost:9090 and query:
- `mcp_proxy_tool_calls_total`
- `histogram_quantile(0.95, mcp_proxy_tool_duration_seconds)`

### Grafana (Dashboards)
Open http://localhost:3000 and create dashboards using Prometheus as data source

## Example Queries

### Prometheus/PromQL

```promql
# Tool calls per minute by server
rate(mcp_proxy_tool_calls_total[1m])

# 95th percentile duration
histogram_quantile(0.95, rate(mcp_proxy_tool_duration_seconds_bucket[5m]))

# Error rate
rate(mcp_proxy_tool_errors_total[5m]) / rate(mcp_proxy_tool_calls_total[5m])
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `OTEL_SERVICE_NAME` | Override service name |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP endpoint URL |
| `OTEL_EXPORTER_OTLP_HEADERS` | Auth headers for OTLP |

## Next Steps

- See [09-per-server-routing](../09-per-server-routing) for per-server endpoints
- See [10-enterprise-complete](../10-enterprise-complete) for a complete production setup
