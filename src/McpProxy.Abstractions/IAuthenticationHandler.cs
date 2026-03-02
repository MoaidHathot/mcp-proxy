using Microsoft.AspNetCore.Http;

namespace McpProxy.Abstractions;

/// <summary>
/// Result of an authentication attempt.
/// </summary>
public sealed class AuthenticationResult
{
    /// <summary>
    /// Gets a value indicating whether authentication was successful.
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// Gets the authenticated principal identifier (e.g., user ID, API key name).
    /// </summary>
    public string? PrincipalId { get; init; }

    /// <summary>
    /// Gets the reason for authentication failure, if applicable.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Gets additional claims or properties associated with the authenticated principal.
    /// </summary>
    public IDictionary<string, string>? Properties { get; init; }

    /// <summary>
    /// Creates a successful authentication result.
    /// </summary>
    /// <param name="principalId">The authenticated principal identifier.</param>
    /// <param name="properties">Additional properties.</param>
    /// <returns>A successful authentication result.</returns>
    public static AuthenticationResult Success(string principalId, IDictionary<string, string>? properties = null)
        => new() { IsAuthenticated = true, PrincipalId = principalId, Properties = properties };

    /// <summary>
    /// Creates a failed authentication result.
    /// </summary>
    /// <param name="reason">The reason for failure.</param>
    /// <returns>A failed authentication result.</returns>
    public static AuthenticationResult Failure(string reason)
        => new() { IsAuthenticated = false, FailureReason = reason };
}

/// <summary>
/// Interface for handling authentication of incoming requests.
/// </summary>
public interface IAuthenticationHandler
{
    /// <summary>
    /// Gets the authentication scheme name (e.g., "ApiKey", "Bearer").
    /// </summary>
    string SchemeName { get; }

    /// <summary>
    /// Authenticates an incoming HTTP request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authentication result.</returns>
    ValueTask<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles an authentication challenge (e.g., returning 401 with appropriate headers).
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default);
}
