using System.Text;
using Azure.Identity;

namespace McpProxy.Sdk.Exceptions;

/// <summary>
/// Exception that represents a failure to authenticate to a backend MCP server
/// (for example, an expired <see cref="InteractiveBrowserCredential"/> refresh token
/// where interactive sign-in could not complete).
/// </summary>
/// <remarks>
/// This is a proxy-level wrapper that carries the originating <see cref="ServerName"/>
/// and <see cref="CredentialGroup"/> so the failure can be surfaced to the MCP client
/// with an actionable message instead of being collapsed into a silent "0 tools" result.
/// </remarks>
public sealed class BackendAuthenticationException : Exception
{
    /// <summary>
    /// Gets the name of the backend server whose authentication failed.
    /// </summary>
    public string ServerName { get; }

    /// <summary>
    /// Gets the credential group the backend belongs to, if any. Backends in the same
    /// group share a single credential, so a failure here affects the whole group.
    /// </summary>
    public string? CredentialGroup { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BackendAuthenticationException"/> class.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="credentialGroup">The credential group, if any.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The originating exception, if any.</param>
    public BackendAuthenticationException(
        string serverName,
        string? credentialGroup,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ServerName = serverName;
        CredentialGroup = credentialGroup;
    }

    /// <summary>
    /// Builds a <see cref="BackendAuthenticationException"/> for a single backend from the
    /// originating credential/transport exception, producing an actionable remediation message.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="credentialGroup">The credential group, if any.</param>
    /// <param name="inner">The originating exception.</param>
    /// <returns>A wrapped exception with a human-readable message.</returns>
    public static BackendAuthenticationException From(string serverName, string? credentialGroup, Exception inner)
    {
        var group = credentialGroup is not null ? $" (credential group '{credentialGroup}')" : string.Empty;
        var message =
            $"Backend '{serverName}'{group} requires interactive sign-in and authentication did not complete: " +
            $"{inner.Message} A browser window may have opened for sign-in — complete it and retry.";
        return new BackendAuthenticationException(serverName, credentialGroup, message, inner);
    }

    /// <summary>
    /// Combines one or more backend authentication failures into a single exception. When a
    /// single failure is supplied it is returned as-is; otherwise the affected servers/groups
    /// are summarized into one message.
    /// </summary>
    /// <param name="failures">The failures to aggregate. Must contain at least one element.</param>
    /// <returns>An aggregated <see cref="BackendAuthenticationException"/>.</returns>
    public static BackendAuthenticationException Aggregate(IReadOnlyCollection<BackendAuthenticationException> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        if (failures.Count == 1)
        {
            return failures.First();
        }

        var servers = failures.Select(f => f.ServerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var builder = new StringBuilder();
        builder.Append("Authentication did not complete for backend(s): ");
        builder.Append(string.Join(", ", servers));
        builder.Append(". A browser window may have opened for sign-in — complete it and retry. Details: ");
        builder.Append(string.Join(" | ", failures.Select(f => f.Message)));

        // Use the first failure's server/group as the representative scope.
        var first = failures.First();
        return new BackendAuthenticationException(first.ServerName, first.CredentialGroup, builder.ToString());
    }
}

/// <summary>
/// Classifies exceptions to determine whether they represent a backend authentication failure
/// (token acquisition / interactive sign-in failure) as opposed to a generic transport or
/// protocol error. Used to decide whether a failure should be surfaced to the MCP client or
/// treated with the resilient log-and-continue path.
/// </summary>
public static class BackendAuthClassifier
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="exception"/> (or any of its inner exceptions)
    /// represents an authentication/token-acquisition failure.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns><c>true</c> if the exception chain contains an authentication failure.</returns>
    public static bool IsAuthFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                // Already-classified proxy failure.
                case BackendAuthenticationException:
                // Azure.Identity: AuthenticationFailedException is the base for
                // CredentialUnavailableException and AuthenticationRequiredException.
                case AuthenticationFailedException:
                // MSAL-backed flows (client credentials / OBO / managed identity).
                case McpProxy.Sdk.Authentication.AuthenticationException:
                    return true;
            }

            // Fallback for MSAL exception types we don't reference directly
            // (e.g., MsalUiRequiredException, MsalServiceException).
            var typeName = current.GetType().FullName;
            if (typeName is not null && typeName.StartsWith("Microsoft.Identity.Client", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
