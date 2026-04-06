using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using ModelContextProtocol.Protocol;

namespace McpProxy.SDK.Filtering;

/// <summary>
/// Transforms resource URIs by adding a server-specific prefix.
/// </summary>
public sealed class ResourcePrefixer : IResourceTransformer
{
    private readonly string _prefix;
    private readonly string _separator;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePrefixer"/>.
    /// </summary>
    /// <param name="prefix">The prefix to add to resource URIs.</param>
    /// <param name="separator">The separator between prefix and resource URI.</param>
    public ResourcePrefixer(string prefix, string separator = "://")
    {
        _prefix = prefix;
        _separator = separator;
    }

    /// <inheritdoc />
    public Resource Transform(Resource resource, string serverName)
    {
        var prefixedUri = AddPrefix(resource.Uri);

        return new Resource
        {
            Uri = prefixedUri,
            Name = resource.Name,
            Description = resource.Description,
            MimeType = resource.MimeType,
            Size = resource.Size,
            Annotations = resource.Annotations
        };
    }

    /// <summary>
    /// Removes the prefix from a resource URI to get the original URI.
    /// </summary>
    /// <param name="prefixedUri">The prefixed resource URI.</param>
    /// <returns>The original resource URI, or the input if prefix not found.</returns>
    public string RemovePrefix(string prefixedUri)
    {
        var expectedPrefix = $"{_prefix}{_separator}";
        if (prefixedUri.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return prefixedUri[expectedPrefix.Length..];
        }

        return prefixedUri;
    }

    /// <summary>
    /// Checks if a resource URI has this prefixer's prefix.
    /// </summary>
    /// <param name="uri">The resource URI to check.</param>
    /// <returns>True if the URI has the prefix.</returns>
    public bool HasPrefix(string uri)
    {
        var expectedPrefix = $"{_prefix}{_separator}";
        return uri.StartsWith(expectedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adds the prefix to a resource URI.
    /// </summary>
    /// <param name="uri">The original resource URI.</param>
    /// <returns>The prefixed resource URI.</returns>
    public string AddPrefix(string uri)
    {
        return $"{_prefix}{_separator}{uri}";
    }
}

/// <summary>
/// Identity transformer that returns resources unchanged.
/// </summary>
public sealed class NoResourceTransform : IResourceTransformer
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NoResourceTransform Instance = new();

    /// <inheritdoc />
    public Resource Transform(Resource resource, string serverName) => resource;
}

/// <summary>
/// Factory for creating resource transformers from configuration.
/// </summary>
public static class ResourceTransformerFactory
{
    /// <summary>
    /// Creates a transformer from configuration.
    /// </summary>
    /// <param name="config">The resources configuration.</param>
    /// <returns>The created transformer.</returns>
    public static IResourceTransformer Create(ResourcesConfiguration config)
    {
        if (string.IsNullOrEmpty(config.Prefix))
        {
            return NoResourceTransform.Instance;
        }

        return new ResourcePrefixer(config.Prefix, config.PrefixSeparator);
    }
}
