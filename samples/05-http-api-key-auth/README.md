# HTTP Mode with API Key Authentication

This example demonstrates how to run MCP Proxy in HTTP/SSE mode with API key authentication. This is useful for exposing MCP servers as HTTP endpoints that require authentication.

## Features Demonstrated

- **HTTP/SSE Transport**: Run the proxy as an HTTP server
- **API Key Authentication**: Require API key for all requests
- **Multiple Auth Methods**: Support both header and query parameter
- **Request Logging**: Log incoming requests for debugging
- **Sensitive Data Masking**: Hide sensitive data in logs

## How It Works

When running in HTTP/SSE mode, the proxy exposes endpoints that clients can connect to over HTTP. Authentication is enforced on all MCP protocol requests.

```
┌─────────────┐                   ┌─────────────┐
│   Client    │ ── HTTP/SSE ───► │  MCP Proxy  │
│             │   + API Key      │             │
└─────────────┘                   └──────┬──────┘
                                         │
                          ┌──────────────┴──────────────┐
                          │                             │
                    ┌─────┴─────┐                 ┌─────┴─────┐
                    │ Filesystem│                 │   Time    │
                    │  Server   │                 │  Server   │
                    └───────────┘                 └───────────┘
```

## Authentication Configuration

### API Key via Header (Recommended)

```json
{
  "authentication": {
    "enabled": true,
    "type": "apiKey",
    "apiKey": {
      "header": "X-API-Key",
      "value": "env:MCP_PROXY_API_KEY"
    }
  }
}
```

Clients send the API key in a header:
```
X-API-Key: your-secret-api-key
```

### API Key via Query Parameter

```json
{
  "authentication": {
    "apiKey": {
      "header": "X-API-Key",
      "queryParameter": "api_key",
      "value": "env:MCP_PROXY_API_KEY"
    }
  }
}
```

Clients can also use a query parameter:
```
GET /mcp/sse?api_key=your-secret-api-key
```

Note: Header authentication is checked first, then query parameter as fallback.

## Running the Example

### 1. Set the API Key

```bash
# Linux/macOS
export MCP_PROXY_API_KEY="your-secret-api-key-here"

# Windows (PowerShell)
$env:MCP_PROXY_API_KEY = "your-secret-api-key-here"

# Windows (CMD)
set MCP_PROXY_API_KEY=your-secret-api-key-here
```

### 2. Start the Proxy in HTTP Mode

```bash
mcpproxy -t sse -c ./mcp-proxy.json -p 5000
```

### 3. Connect to the Proxy

#### SSE Endpoint
```
GET http://localhost:5000/mcp/sse
Headers:
  X-API-Key: your-secret-api-key-here
```

#### Message Endpoint
```
POST http://localhost:5000/mcp/message
Headers:
  X-API-Key: your-secret-api-key-here
  Content-Type: application/json
Body:
  { JSON-RPC message }
```

## HTTP Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/mcp/sse` | GET | SSE connection for server-to-client messages |
| `/mcp/message` | POST | Client-to-server JSON-RPC messages |

## Testing with cURL

### Test SSE Connection
```bash
curl -N -H "X-API-Key: your-secret-api-key-here" \
  http://localhost:5000/mcp/sse
```

### Test with Invalid API Key
```bash
curl -H "X-API-Key: wrong-key" \
  http://localhost:5000/mcp/sse
# Returns 401 Unauthorized
```

## Logging Configuration

```json
{
  "logging": {
    "logRequests": true,      // Log incoming requests
    "logResponses": false,    // Don't log responses (verbose)
    "sensitiveDataMask": true // Mask API keys in logs
  }
}
```

With `sensitiveDataMask: true`, log output shows:
```
Request: GET /mcp/sse - API Key: ****...key1
```

## Security Best Practices

1. **Use Environment Variables**: Never hardcode API keys in config files
2. **Use HTTPS in Production**: Always use TLS for HTTP endpoints
3. **Rotate Keys Regularly**: Change API keys periodically
4. **Use Strong Keys**: Generate cryptographically random API keys
5. **Limit Key Scope**: Use different keys for different clients/purposes

### Generating a Secure API Key

```bash
# Linux/macOS
openssl rand -base64 32

# PowerShell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

## Alternative: Hardcoded API Key (Not Recommended)

For testing only:
```json
{
  "authentication": {
    "apiKey": {
      "value": "test-api-key-12345"
    }
  }
}
```

## Integration with Reverse Proxies

When behind nginx, Apache, or other reverse proxies:

### nginx Configuration
```nginx
server {
    listen 443 ssl;
    server_name mcp.example.com;
    
    location /mcp/ {
        proxy_pass http://localhost:5000/mcp/;
        proxy_http_version 1.1;
        proxy_set_header Connection '';
        proxy_set_header X-API-Key $http_x_api_key;
        proxy_buffering off;
        proxy_cache off;
        chunked_transfer_encoding off;
    }
}
```

## Error Responses

| Status | Description |
|--------|-------------|
| 401 Unauthorized | Missing or invalid API key |
| 403 Forbidden | API key valid but access denied |
| 500 Internal Server Error | Server error |

## Next Steps

- See [06-hooks](../06-hooks) to add rate limiting and audit logging
- See [07-azure-ad-auth](../07-azure-ad-auth) for enterprise OAuth2/OIDC authentication
