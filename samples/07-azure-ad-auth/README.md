# Azure AD Authentication

This example demonstrates enterprise-grade authentication using Microsoft Entra ID (Azure AD). It shows both client authentication to the proxy and backend authentication to protected MCP servers.

## Features Demonstrated

- **Azure AD Client Authentication**: OAuth2/OIDC authentication for incoming requests
- **Required Scopes**: Enforce OAuth scopes for access control
- **Required Roles**: Enforce app roles for fine-grained permissions
- **Role-Based Authorization Hooks**: Restrict tools based on user roles
- **On-Behalf-Of Flow**: Delegate user identity to backend services
- **Client Credentials Flow**: App-to-app authentication

## Architecture

```
┌─────────────┐                   ┌─────────────┐                   ┌─────────────┐
│   Client    │ ── Bearer Token ─►│  MCP Proxy  │ ── OBO Token ────►│  Protected  │
│  (with JWT) │                   │  (validates)│                   │   Backend   │
└─────────────┘                   └──────┬──────┘                   └─────────────┘
       │                                 │
       │                          ┌──────┴──────┐
       ▼                          │  Azure AD   │
┌─────────────┐                   │  Validates  │
│  Azure AD   │                   │   Tokens    │
│  Login      │                   └─────────────┘
└─────────────┘
```

## Azure AD Setup

### 1. Register an Application in Azure AD

1. Go to Azure Portal → Azure Active Directory → App registrations
2. Click "New registration"
3. Configure:
   - Name: `MCP Proxy`
   - Supported account types: Choose based on your needs
   - Redirect URI: (optional for API)

### 2. Configure API Permissions

1. Go to your app → API permissions
2. Add permissions:
   - Microsoft Graph → User.Read (delegated)
   - Your backend API (if using OBO flow)

### 3. Define App Roles (Optional)

1. Go to your app → App roles
2. Create roles:
   ```json
   {
     "allowedMemberTypes": ["User", "Application"],
     "displayName": "MCP Admin",
     "description": "Can perform write operations",
     "value": "MCP.Admin"
   }
   ```

### 4. Expose an API (Optional)

1. Go to your app → Expose an API
2. Set Application ID URI: `api://<client-id>`
3. Add scopes:
   - `MCP.Read` - Read access to MCP tools
   - `MCP.Write` - Write access to MCP tools

### 5. Create Client Secret (for OBO flow)

1. Go to your app → Certificates & secrets
2. New client secret
3. Copy the secret value immediately

## Configuration

### Client Authentication

```json
{
  "authentication": {
    "enabled": true,
    "type": "azureAd",
    "azureAd": {
      "tenantId": "your-tenant-id",
      "clientId": "your-client-id",
      "audience": "api://your-client-id",
      "requiredScopes": ["MCP.Read"],
      "requiredRoles": ["MCP.User"]
    }
  }
}
```

### Backend Authentication Types

#### Client Credentials (App-to-App)

```json
{
  "auth": {
    "type": "AzureAdClientCredentials",
    "azureAd": {
      "tenantId": "env:AZURE_TENANT_ID",
      "clientId": "env:AZURE_CLIENT_ID",
      "clientSecret": "env:AZURE_CLIENT_SECRET",
      "scopes": ["api://backend-api/.default"]
    }
  }
}
```

#### On-Behalf-Of (User Delegation)

```json
{
  "auth": {
    "type": "AzureAdOnBehalfOf",
    "azureAd": {
      "tenantId": "env:AZURE_TENANT_ID",
      "clientId": "env:AZURE_CLIENT_ID",
      "clientSecret": "env:AZURE_CLIENT_SECRET",
      "scopes": ["api://backend-api/user_impersonation"]
    }
  }
}
```

#### Managed Identity (Azure-hosted)

```json
{
  "auth": {
    "type": "AzureAdManagedIdentity",
    "azureAd": {
      "clientId": "user-assigned-mi-client-id",  // optional for user-assigned
      "scopes": ["api://backend-api/.default"]
    }
  }
}
```

## Running the Example

### 1. Set Environment Variables

```bash
# Required
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_ID="your-client-id"

# For OBO/Client Credentials flow
export AZURE_CLIENT_SECRET="your-client-secret"
```

### 2. Start the Proxy

```bash
mcpproxy -t sse -c ./mcp-proxy.json -p 5000
```

### 3. Obtain an Access Token

Using Azure CLI:
```bash
az login
az account get-access-token --resource api://your-client-id --query accessToken -o tsv
```

Using MSAL:
```csharp
var app = PublicClientApplicationBuilder
    .Create(clientId)
    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
    .Build();

var result = await app.AcquireTokenInteractive(scopes).ExecuteAsync();
var token = result.AccessToken;
```

### 4. Connect to the Proxy

```bash
curl -H "Authorization: Bearer <access-token>" \
  http://localhost:5000/mcp/sse
```

## Role-Based Tool Authorization

Combine Azure AD roles with authorization hooks:

```json
{
  "hooks": {
    "preInvoke": [
      {
        "type": "authorization",
        "config": {
          "requiredRoles": ["MCP.Admin"],
          "allowedTools": ["write_file", "delete_file"]
        }
      }
    ]
  }
}
```

This ensures only users with the `MCP.Admin` role can use write/delete operations.

## Token Claims Available

| Claim | Description |
|-------|-------------|
| `sub` | User/app unique identifier |
| `name` | User display name |
| `email` | User email address |
| `roles` | App roles assigned |
| `scp` | Delegated scopes |
| `tid` | Tenant ID |
| `oid` | Object ID |

## Error Responses

| Status | Description |
|--------|-------------|
| 401 | Missing or invalid token |
| 403 | Valid token but missing required scope/role |

## Security Best Practices

1. **Use Managed Identity** when running in Azure
2. **Minimize Scopes** - Request only needed permissions
3. **Validate Audience** - Always check the `aud` claim
4. **Short Token Lifetimes** - Use refresh tokens
5. **Monitor Sign-ins** - Review Azure AD sign-in logs

## Troubleshooting

### Invalid Token
- Check token expiration
- Verify audience matches client ID
- Ensure tenant ID is correct

### Missing Scopes
- Check API permissions in Azure portal
- Ensure admin consent was granted
- Verify scopes in token request

### Role Not Found
- Verify role is assigned to user/app
- Check role value matches configuration
- Ensure app role is enabled

## Next Steps

- See [08-telemetry](../08-telemetry) for monitoring with OpenTelemetry
- See [10-enterprise-complete](../10-enterprise-complete) for a complete enterprise setup
