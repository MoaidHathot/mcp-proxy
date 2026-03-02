using ModelContextProtocol.Protocol;

namespace McpProxy.Abstractions;

/// <summary>
/// Interface for filtering tools based on configurable criteria.
/// </summary>
public interface IToolFilter
{
    /// <summary>
    /// Determines whether a tool should be included in the exposed tools.
    /// </summary>
    /// <param name="tool">The tool to evaluate.</param>
    /// <param name="serverName">The name of the server providing this tool.</param>
    /// <returns>True if the tool should be included; otherwise, false.</returns>
    bool ShouldInclude(Tool tool, string serverName);
}

/// <summary>
/// Interface for transforming tool information before exposure.
/// </summary>
public interface IToolTransformer
{
    /// <summary>
    /// Transforms a tool's metadata before it is exposed to clients.
    /// </summary>
    /// <param name="tool">The original tool.</param>
    /// <param name="serverName">The name of the server providing this tool.</param>
    /// <returns>The transformed tool.</returns>
    Tool Transform(Tool tool, string serverName);
}

/// <summary>
/// Composite interface for filters that also transform tools.
/// </summary>
public interface IToolFilterAndTransformer : IToolFilter, IToolTransformer
{
}
