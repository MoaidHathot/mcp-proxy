using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using Microsoft.AspNetCore.Http;

namespace McpProxy.SDK.Authentication;

/// <summary>
/// Authentication handler that validates API keys.
/// </summary>
public sealed class ApiKeyAuthHandler : IAuthenticationHandler
{
    private readonly string _headerName;
    private readonly string? _queryParameter;
    private readonly string _expectedKey;

    /// <summary>
    /// Initializes a new instance of <see cref="ApiKeyAuthHandler"/>.
    /// </summary>
    /// <param name="config">The API key configuration.</param>
    public ApiKeyAuthHandler(ApiKeyConfiguration config)
    {
        _headerName = config.Header;
        _queryParameter = config.QueryParameter;
        _expectedKey = ResolveValue(config.Value ?? string.Empty);
    }

    /// <inheritdoc />
    public string SchemeName => "ApiKey";

    /// <inheritdoc />
    public ValueTask<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // Try to get API key from header
        if (context.Request.Headers.TryGetValue(_headerName, out var headerValue) &&
            !string.IsNullOrEmpty(headerValue))
        {
            if (ValidateKey(headerValue!))
            {
                return ValueTask.FromResult(AuthenticationResult.Success("api-key-user"));
            }

            return ValueTask.FromResult(AuthenticationResult.Failure("Invalid API key"));
        }

        // Try to get API key from query parameter
        if (!string.IsNullOrEmpty(_queryParameter) &&
            context.Request.Query.TryGetValue(_queryParameter, out var queryValue) &&
            !string.IsNullOrEmpty(queryValue))
        {
            if (ValidateKey(queryValue!))
            {
                return ValueTask.FromResult(AuthenticationResult.Success("api-key-user"));
            }

            return ValueTask.FromResult(AuthenticationResult.Failure("Invalid API key"));
        }

        return ValueTask.FromResult(AuthenticationResult.Failure("API key not provided"));
    }

    /// <inheritdoc />
    public ValueTask ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.Append("WWW-Authenticate", $"ApiKey realm=\"MCP Proxy\", header=\"{_headerName}\"");
        return ValueTask.CompletedTask;
    }

    private bool ValidateKey(string providedKey)
    {
        return string.Equals(providedKey, _expectedKey, StringComparison.Ordinal);
    }

    private static string ResolveValue(string value)
    {
        // Support "env:VARIABLE_NAME" syntax
        if (value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var envName = value[4..];
            return Environment.GetEnvironmentVariable(envName) ?? string.Empty;
        }

        return value;
    }
}
