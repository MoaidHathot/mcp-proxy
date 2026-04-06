namespace McpProxy.SDK.Exceptions;

/// <summary>
/// Exception thrown when a rate limit is exceeded.
/// </summary>
public sealed class RateLimitExceededException : Exception
{
    /// <summary>
    /// Gets the rate limit key that was exceeded.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the maximum number of requests allowed in the window.
    /// </summary>
    public int Limit { get; }

    /// <summary>
    /// Gets the window duration in seconds.
    /// </summary>
    public int WindowSeconds { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="RateLimitExceededException"/>.
    /// </summary>
    /// <param name="key">The rate limit key that was exceeded.</param>
    /// <param name="limit">The maximum number of requests allowed.</param>
    /// <param name="windowSeconds">The window duration in seconds.</param>
    public RateLimitExceededException(string key, int limit, int windowSeconds)
        : base($"Rate limit exceeded for key '{key}'. Maximum {limit} requests per {windowSeconds} seconds.")
    {
        Key = key;
        Limit = limit;
        WindowSeconds = windowSeconds;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RateLimitExceededException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    public RateLimitExceededException(string message) : base(message)
    {
        Key = string.Empty;
        Limit = 0;
        WindowSeconds = 0;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RateLimitExceededException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public RateLimitExceededException(string message, Exception innerException) : base(message, innerException)
    {
        Key = string.Empty;
        Limit = 0;
        WindowSeconds = 0;
    }
}

/// <summary>
/// Exception thrown when authorization is denied for a tool invocation.
/// </summary>
public sealed class AuthorizationException : Exception
{
    /// <summary>
    /// Gets the tool name that was denied.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the principal ID that was denied.
    /// </summary>
    public string? PrincipalId { get; }

    /// <summary>
    /// Gets the reason for the denial.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AuthorizationException"/>.
    /// </summary>
    /// <param name="toolName">The tool name that was denied.</param>
    /// <param name="principalId">The principal ID that was denied.</param>
    /// <param name="reason">The reason for the denial.</param>
    public AuthorizationException(string toolName, string? principalId, string reason)
        : base($"Authorization denied for tool '{toolName}'" + 
               (principalId is not null ? $" (principal: {principalId})" : "") + 
               $": {reason}")
    {
        ToolName = toolName;
        PrincipalId = principalId;
        Reason = reason;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AuthorizationException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AuthorizationException(string message) : base(message)
    {
        ToolName = string.Empty;
        PrincipalId = null;
        Reason = message;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AuthorizationException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AuthorizationException(string message, Exception innerException) : base(message, innerException)
    {
        ToolName = string.Empty;
        PrincipalId = null;
        Reason = message;
    }
}
