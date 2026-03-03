namespace McpProxy.Core.Debugging;

/// <summary>
/// Dumps request and response payloads for debugging purposes.
/// </summary>
public interface IRequestDumper
{
    /// <summary>
    /// Dumps a request payload.
    /// </summary>
    /// <param name="serverName">The name of the server handling the request.</param>
    /// <param name="toolName">The name of the tool being invoked.</param>
    /// <param name="request">The request object to dump.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask DumpRequestAsync(string serverName, string toolName, object request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dumps a response payload.
    /// </summary>
    /// <param name="serverName">The name of the server that handled the request.</param>
    /// <param name="toolName">The name of the tool that was invoked.</param>
    /// <param name="response">The response object to dump.</param>
    /// <param name="duration">The duration of the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask DumpResponseAsync(string serverName, string toolName, object response, TimeSpan duration, CancellationToken cancellationToken = default);
}
