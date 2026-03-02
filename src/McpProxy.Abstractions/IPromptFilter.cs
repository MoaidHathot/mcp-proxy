using ModelContextProtocol.Protocol;

namespace McpProxy.Abstractions;

/// <summary>
/// Interface for filtering prompts based on configurable criteria.
/// </summary>
public interface IPromptFilter
{
    /// <summary>
    /// Determines whether a prompt should be included in the exposed prompts.
    /// </summary>
    /// <param name="prompt">The prompt to evaluate.</param>
    /// <param name="serverName">The name of the server providing this prompt.</param>
    /// <returns>True if the prompt should be included; otherwise, false.</returns>
    bool ShouldInclude(Prompt prompt, string serverName);
}

/// <summary>
/// Interface for transforming prompt information before exposure.
/// </summary>
public interface IPromptTransformer
{
    /// <summary>
    /// Transforms a prompt's metadata before it is exposed to clients.
    /// </summary>
    /// <param name="prompt">The original prompt.</param>
    /// <param name="serverName">The name of the server providing this prompt.</param>
    /// <returns>The transformed prompt.</returns>
    Prompt Transform(Prompt prompt, string serverName);
}
