# Enterprise Complete Setup

This comprehensive example demonstrates a production-ready MCP Proxy configuration with all enterprise features enabled. It combines authentication, authorization, audit logging, rate limiting, content filtering, telemetry, and secure backend connections.

## Features Demonstrated

| Category | Features |
|----------|----------|
| **Authentication** | Azure AD OAuth2/OIDC |
| **Authorization** | Role-based access control with hooks |
| **Audit** | Comprehensive audit logging |
| **Rate Limiting** | Per-user and global rate limits |
| **Security** | Content filtering, PII redaction |
| **Observability** | OpenTelemetry metrics and tracing |
| **Backends** | STDIO, HTTP with OBO, Managed Identity |
| **Caching** | Tool list caching |
| **Filtering** | Allowlist and denylist filters |

## Architecture

```
┌──────────────────────────────────────────────────────────────────────────┐
│                           ENTERPRISE MCP PROXY                           │
├──────────────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │   Azure AD   │  │    Audit     │  │    Rate      │  │   Content    │ │
│  │     Auth     │  │   Logging    │  │   Limiting   │  │   Filter     │ │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘ │
│         │                 │                 │                 │         │
│         └─────────────────┴─────────────────┴─────────────────┘         │
│                                    │                                     │
│                           ┌────────┴────────┐                           │
│                           │   Hook Pipeline │                           │
│                           └────────┬────────┘                           │
│                                    │                                     │
│    ┌───────────────┬───────────────┼───────────────┬───────────────┐    │
│    │               │               │               │               │    │
│    ▼               ▼               ▼               ▼               │    │
│ ┌─────┐       ┌─────────┐    ┌──────────┐    ┌─────────┐          │    │
│ │ FS  │       │Database │    │Analytics │    │ Memory  │          │    │
│ │STDIO│       │HTTP+OBO │    │HTTP+MI   │    │ STDIO   │          │    │
│ └─────┘       └─────────┘    └──────────┘    └─────────┘          │    │
└──────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
                        ┌─────────────────────┐
                        │  Telemetry Backend  │
                        │  (OTLP Collector)   │
                        └─────────────────────┘
```

## Security Layers

### Layer 1: Authentication
```json
{
  "authentication": {
    "enabled": true,
    "type": "azureAd",
    "azureAd": {
      "tenantId": "...",
      "clientId": "...",
      "requiredScopes": ["MCP.Access"]
    }
  }
}
```

### Layer 2: Authorization (Hooks)
```json
{
  "type": "authorization",
  "config": {
    "requiredRoles": ["MCP.FileAdmin"],
    "allowedTools": ["write_file", "delete_file"]
  }
}
```

### Layer 3: Tool Filtering
```json
{
  "tools": {
    "filter": {
      "mode": "denylist",
      "patterns": ["drop_*", "truncate_*"]
    }
  }
}
```

### Layer 4: Content Filtering
```json
{
  "type": "contentFilter",
  "config": {
    "patterns": ["password", "\\b\\d{3}-\\d{2}-\\d{4}\\b"],
    "action": "redact"
  }
}
```

## Backend Authentication Patterns

### 1. STDIO (Local)
No authentication needed - process runs locally.

### 2. HTTP + On-Behalf-Of
User identity delegated to backend.
```json
{
  "auth": {
    "type": "AzureAdOnBehalfOf",
    "azureAd": {
      "clientSecret": "env:AZURE_CLIENT_SECRET",
      "scopes": ["api://backend/.default"]
    }
  }
}
```

### 3. HTTP + Managed Identity
For Azure-hosted deployments.
```json
{
  "auth": {
    "type": "AzureAdManagedIdentity",
    "azureAd": {
      "scopes": ["api://backend/.default"]
    }
  }
}
```

## Hook Pipeline

```
Request → Logging → Audit → RateLimit → Timeout → Authorization → Tool Execution
                                                                        │
Response ← Metrics ← Audit ← ContentFilter ←────────────────────────────┘
```

### Hook Priorities (Lower = Earlier)

| Priority | Hook | Phase |
|----------|------|-------|
| -1000 | Logging | Pre |
| -950 | Audit | Pre |
| -900 | RateLimit | Pre |
| -800 | Timeout | Pre |
| -700 | Authorization | Pre |
| 800 | ContentFilter | Post |
| 900 | Metrics | Post |
| 950 | Audit | Post |

## Environment Variables

```bash
# Azure AD
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_ID="your-client-id"
export AZURE_CLIENT_SECRET="your-client-secret"

# Telemetry
export OTEL_EXPORTER_OTLP_ENDPOINT="http://otel-collector:4317"
```

## Running the Example

### Development

```bash
# Set environment variables
export AZURE_TENANT_ID="..."
export AZURE_CLIENT_ID="..."
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"

# Start with verbose logging
mcpproxy -t sse -c ./mcp-proxy.json -p 5000 -v
```

### Production (Docker)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
COPY mcp-proxy.json .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "McpProxy.dll", "-t", "sse", "-c", "mcp-proxy.json", "-p", "5000"]
```

### Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mcp-proxy
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: mcp-proxy
        image: your-registry/mcp-proxy:latest
        ports:
        - containerPort: 5000
        env:
        - name: AZURE_TENANT_ID
          valueFrom:
            secretKeyRef:
              name: mcp-proxy-secrets
              key: tenant-id
        - name: AZURE_CLIENT_ID
          valueFrom:
            secretKeyRef:
              name: mcp-proxy-secrets
              key: client-id
        - name: AZURE_CLIENT_SECRET
          valueFrom:
            secretKeyRef:
              name: mcp-proxy-secrets
              key: client-secret
        - name: OTEL_EXPORTER_OTLP_ENDPOINT
          value: "http://otel-collector.monitoring:4317"
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health
            port: 5000
          initialDelaySeconds: 10
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 5000
          initialDelaySeconds: 5
          periodSeconds: 10
```

## Audit Log Format

```json
{
  "timestamp": "2024-01-15T10:30:45.123Z",
  "correlationId": "abc-123-def",
  "event": "tool_invocation",
  "phase": "pre",
  "user": {
    "id": "user@example.com",
    "name": "John Doe",
    "roles": ["MCP.User", "MCP.FileAdmin"]
  },
  "server": "filesystem",
  "tool": "write_file",
  "arguments": {
    "path": "/data/report.txt"
  }
}
```

## Monitoring Dashboard

### Key Metrics to Track

| Metric | Alert Threshold |
|--------|----------------|
| `mcp.proxy.tool.errors` | > 5% error rate |
| `mcp.proxy.tool.duration_p95` | > 5s |
| `mcp.proxy.rate_limit.exceeded` | > 10/min |
| `mcp.proxy.auth.failures` | > 5/min |
| `mcp.proxy.connections.active` | > 100 |

### Grafana Alerts

```yaml
- alert: HighErrorRate
  expr: rate(mcp_proxy_tool_errors_total[5m]) / rate(mcp_proxy_tool_calls_total[5m]) > 0.05
  for: 5m
  labels:
    severity: critical
  annotations:
    summary: "High MCP Proxy error rate"
```

## Security Checklist

- [ ] Azure AD app registration configured
- [ ] Required scopes/roles defined
- [ ] Client secrets stored in Key Vault/secrets manager
- [ ] Audit logs sent to SIEM
- [ ] Rate limits configured appropriately
- [ ] PII redaction patterns cover all sensitive data
- [ ] TLS enabled for all HTTP endpoints
- [ ] Network policies restrict backend access
- [ ] Managed Identity used where possible
- [ ] Regular security reviews scheduled

## Compliance

### SOC 2
- Audit logging captures all tool invocations
- Access controls documented
- Encryption in transit (TLS)

### GDPR
- PII redaction in outputs
- Audit trail for data access
- Data minimization via tool filtering

### HIPAA
- PHI patterns in content filter
- Comprehensive audit logs
- Role-based access control

## Troubleshooting

### Common Issues

| Issue | Solution |
|-------|----------|
| 401 Unauthorized | Check Azure AD configuration, token expiry |
| 403 Forbidden | Verify user has required roles/scopes |
| 429 Too Many Requests | Adjust rate limits or wait |
| 502 Backend Error | Check backend server health |
| Slow responses | Review timeout settings, check backend latency |

### Debug Mode

```bash
mcpproxy -t sse -c ./mcp-proxy.json -p 5000 -v --log-level debug
```

## Performance Tuning

### Caching
```json
{
  "caching": {
    "tools": {
      "enabled": true,
      "ttlSeconds": 300  // 5 minutes
    }
  }
}
```

### Connection Pooling
Backend HTTP connections are pooled automatically.

### Async Processing
All hook execution is async and non-blocking.

## Next Steps

1. Review and customize authentication settings
2. Configure audit log destination (SIEM integration)
3. Set up alerting based on metrics
4. Create Grafana dashboards
5. Schedule security review
6. Document operational procedures
