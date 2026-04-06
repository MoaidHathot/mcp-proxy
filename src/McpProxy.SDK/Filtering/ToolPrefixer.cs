using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using ModelContextProtocol.Protocol;

namespace McpProxy.SDK.Filtering;

/// <summary>
/// Transforms tool names by adding a server-specific prefix.
/// </summary>
public sealed class ToolPrefixer : IToolTransformer
{
    private readonly string _prefix;
    private readonly string _separator;

    /// <summary>
    /// Initializes a new instance of <see cref="ToolPrefixer"/>.
    /// </summary>
    /// <param name="prefix">The prefix to add to tool names.</param>
    /// <param name="separator">The separator between prefix and tool name.</param>
    public ToolPrefixer(string prefix, string separator = "_")
    {
        _prefix = prefix;
        _separator = separator;
    }

    /// <inheritdoc />
    public Tool Transform(Tool tool, string serverName)
    {
        var prefixedName = $"{_prefix}{_separator}{tool.Name}";

        return new Tool
        {
            Name = prefixedName,
            Title = tool.Title,
            Description = tool.Description,
            InputSchema = tool.InputSchema,
            OutputSchema = tool.OutputSchema,
            Annotations = tool.Annotations
        };
    }

    /// <summary>
    /// Removes the prefix from a tool name to get the original name.
    /// </summary>
    /// <param name="prefixedName">The prefixed tool name.</param>
    /// <returns>The original tool name, or the input if prefix not found.</returns>
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
    /// Checks if a tool name has this prefixer's prefix.
    /// </summary>
    /// <param name="toolName">The tool name to check.</param>
    /// <returns>True if the name has the prefix.</returns>
    public bool HasPrefix(string toolName)
    {
        var expectedPrefix = $"{_prefix}{_separator}";
        return toolName.StartsWith(expectedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adds the prefix to a tool name.
    /// </summary>
    /// <param name="toolName">The original tool name.</param>
    /// <returns>The prefixed tool name.</returns>
    public string AddPrefix(string toolName)
    {
        return $"{_prefix}{_separator}{toolName}";
    }
}

/// <summary>
/// Identity transformer that returns tools unchanged.
/// </summary>
public sealed class NoTransform : IToolTransformer
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NoTransform Instance = new();

    /// <inheritdoc />
    public Tool Transform(Tool tool, string serverName) => tool;
}

/// <summary>
/// Factory for creating tool transformers from configuration.
/// </summary>
public static class TransformerFactory
{
    /// <summary>
    /// Creates a transformer from configuration.
    /// </summary>
    /// <param name="config">The tools configuration.</param>
    /// <returns>The created transformer.</returns>
    public static IToolTransformer Create(ToolsConfiguration config)
    {
        if (string.IsNullOrEmpty(config.Prefix))
        {
            return NoTransform.Instance;
        }

        return new ToolPrefixer(config.Prefix, config.PrefixSeparator);
    }
}
