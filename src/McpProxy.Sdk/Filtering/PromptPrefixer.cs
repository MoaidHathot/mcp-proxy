using McpProxy.Abstractions;
using McpProxy.Sdk.Configuration;
using ModelContextProtocol.Protocol;

namespace McpProxy.Sdk.Filtering;

/// <summary>
/// Transforms prompt names by adding a server-specific prefix.
/// </summary>
public sealed class PromptPrefixer : IPromptTransformer
{
    private readonly string _prefix;
    private readonly string _separator;

    /// <summary>
    /// Initializes a new instance of <see cref="PromptPrefixer"/>.
    /// </summary>
    /// <param name="prefix">The prefix to add to prompt names.</param>
    /// <param name="separator">The separator between prefix and prompt name.</param>
    public PromptPrefixer(string prefix, string separator = "_")
    {
        _prefix = prefix;
        _separator = separator;
    }

    /// <inheritdoc />
    public Prompt Transform(Prompt prompt, string serverName)
    {
        var prefixedName = AddPrefix(prompt.Name);

        return new Prompt
        {
            Name = prefixedName,
            Description = prompt.Description,
            Arguments = prompt.Arguments
        };
    }

    /// <summary>
    /// Removes the prefix from a prompt name to get the original name.
    /// </summary>
    /// <param name="prefixedName">The prefixed prompt name.</param>
    /// <returns>The original prompt name, or the input if prefix not found.</returns>
    public string RemovePrefix(string prefixedName)
    {
        var expectedPrefix = $"{_prefix}{_separator}";
        if (prefixedName.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return prefixedName[expectedPrefix.Length..];
        }

        return prefixedName;
    }

    /// <summary>
    /// Checks if a prompt name has this prefixer's prefix.
    /// </summary>
    /// <param name="promptName">The prompt name to check.</param>
    /// <returns>True if the name has the prefix.</returns>
    public bool HasPrefix(string promptName)
    {
        var expectedPrefix = $"{_prefix}{_separator}";
        return promptName.StartsWith(expectedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adds the prefix to a prompt name.
    /// </summary>
    /// <param name="promptName">The original prompt name.</param>
    /// <returns>The prefixed prompt name.</returns>
    public string AddPrefix(string promptName)
    {
        return $"{_prefix}{_separator}{promptName}";
    }
}

/// <summary>
/// Identity transformer that returns prompts unchanged.
/// </summary>
public sealed class NoPromptTransform : IPromptTransformer
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NoPromptTransform Instance = new();

    /// <inheritdoc />
    public Prompt Transform(Prompt prompt, string serverName) => prompt;
}

/// <summary>
/// Factory for creating prompt transformers from configuration.
/// </summary>
public static class PromptTransformerFactory
{
    /// <summary>
    /// Creates a transformer from configuration.
    /// </summary>
    /// <param name="config">The prompts configuration.</param>
    /// <returns>The created transformer.</returns>
    public static IPromptTransformer Create(PromptsConfiguration config)
    {
        if (string.IsNullOrEmpty(config.Prefix))
        {
            return NoPromptTransform.Instance;
        }

        return new PromptPrefixer(config.Prefix, config.PrefixSeparator);
    }
}
