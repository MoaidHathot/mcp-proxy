using ModelContextProtocol.Protocol;

namespace McpProxy.Abstractions;

/// <summary>
/// Interface for filtering resources based on configurable criteria.
/// </summary>
public interface IResourceFilter
{
    /// <summary>
    /// Determines whether a resource should be included in the exposed resources.
    /// </summary>
    /// <param name="resource">The resource to evaluate.</param>
    /// <param name="serverName">The name of the server providing this resource.</param>
    /// <returns>True if the resource should be included; otherwise, false.</returns>
    bool ShouldInclude(Resource resource, string serverName);
}

/// <summary>
/// Interface for transforming resource information before exposure.
/// </summary>
public interface IResourceTransformer
{
    /// <summary>
    /// Transforms a resource's metadata before it is exposed to clients.
    /// </summary>
    /// <param name="resource">The original resource.</param>
    /// <param name="serverName">The name of the server providing this resource.</param>
    /// <returns>The transformed resource.</returns>
    Resource Transform(Resource resource, string serverName);
}
