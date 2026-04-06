using System.Text.RegularExpressions;
using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using ModelContextProtocol.Protocol;

namespace McpProxy.SDK.Filtering;

/// <summary>
/// Filter that includes all resources (no filtering).
/// </summary>
public sealed class NoResourceFilter : IResourceFilter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NoResourceFilter Instance = new();

    /// <inheritdoc />
    public bool ShouldInclude(Resource resource, string serverName) => true;
}

/// <summary>
/// Filter that includes only resources matching specified URI patterns.
/// </summary>
public sealed class ResourceAllowListFilter : IResourceFilter
{
    private readonly HashSet<string> _exactMatches;
    private readonly Regex[]? _wildcardPatterns;
    private readonly bool _caseInsensitive;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceAllowListFilter"/>.
    /// </summary>
    /// <param name="patterns">URI patterns to include (supports * and ? wildcards).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public ResourceAllowListFilter(IEnumerable<string> patterns, bool caseInsensitive = true)
    {
        _caseInsensitive = caseInsensitive;
        var comparison = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        var patternList = patterns.ToList();
        var exactPatterns = patternList.Where(p => !p.Contains('*') && !p.Contains('?'));
        var wildcardPatternStrings = patternList.Where(p => p.Contains('*') || p.Contains('?'));

        _exactMatches = new HashSet<string>(exactPatterns, comparison);
        _wildcardPatterns = wildcardPatternStrings
            .Select(p => WildcardToRegex(p, caseInsensitive))
            .ToArray();
    }

    /// <inheritdoc />
    public bool ShouldInclude(Resource resource, string serverName)
    {
        var uri = resource.Uri;
        
        if (_exactMatches.Contains(uri))
        {
            return true;
        }

        if (_wildcardPatterns is { Length: > 0 })
        {
            foreach (var pattern in _wildcardPatterns)
            {
                if (pattern.IsMatch(uri))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Regex WildcardToRegex(string pattern, bool caseInsensitive)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var options = RegexOptions.Compiled;
        if (caseInsensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(regexPattern, options);
    }
}

/// <summary>
/// Filter that excludes resources matching specified URI patterns.
/// </summary>
public sealed class ResourceDenyListFilter : IResourceFilter
{
    private readonly ResourceAllowListFilter _innerFilter;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceDenyListFilter"/>.
    /// </summary>
    /// <param name="patterns">URI patterns to exclude (supports * and ? wildcards).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public ResourceDenyListFilter(IEnumerable<string> patterns, bool caseInsensitive = true)
    {
        _innerFilter = new ResourceAllowListFilter(patterns, caseInsensitive);
    }

    /// <inheritdoc />
    public bool ShouldInclude(Resource resource, string serverName)
    {
        // Include if NOT matched by the deny list
        return !_innerFilter.ShouldInclude(resource, serverName);
    }
}

/// <summary>
/// Filter that uses regex patterns for resource include/exclude.
/// </summary>
public sealed class ResourceRegexFilter : IResourceFilter
{
    private readonly Regex? _includePattern;
    private readonly Regex? _excludePattern;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceRegexFilter"/>.
    /// </summary>
    /// <param name="includePattern">Regex pattern for resources to include (null = include all).</param>
    /// <param name="excludePattern">Regex pattern for resources to exclude (null = exclude none).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public ResourceRegexFilter(string? includePattern, string? excludePattern = null, bool caseInsensitive = true)
    {
        var options = RegexOptions.Compiled;
        if (caseInsensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        _includePattern = includePattern is not null ? new Regex(includePattern, options) : null;
        _excludePattern = excludePattern is not null ? new Regex(excludePattern, options) : null;
    }

    /// <inheritdoc />
    public bool ShouldInclude(Resource resource, string serverName)
    {
        var uri = resource.Uri;
        
        // If include pattern exists, resource must match it
        if (_includePattern is not null && !_includePattern.IsMatch(uri))
        {
            return false;
        }

        // If exclude pattern exists and matches, exclude the resource
        if (_excludePattern is not null && _excludePattern.IsMatch(uri))
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Factory for creating resource filters from configuration.
/// </summary>
public static class ResourceFilterFactory
{
    /// <summary>
    /// Creates a resource filter from configuration.
    /// </summary>
    /// <param name="config">The filter configuration.</param>
    /// <returns>The created filter.</returns>
    public static IResourceFilter Create(FilterConfiguration config)
    {
        if (config.Mode == FilterMode.None || config.Patterns is null or { Length: 0 })
        {
            return NoResourceFilter.Instance;
        }

        return config.Mode switch
        {
            FilterMode.AllowList => new ResourceAllowListFilter(config.Patterns, config.CaseInsensitive),
            FilterMode.DenyList => new ResourceDenyListFilter(config.Patterns, config.CaseInsensitive),
            FilterMode.Regex => CreateRegexFilter(config),
            _ => NoResourceFilter.Instance
        };
    }

    private static ResourceRegexFilter CreateRegexFilter(FilterConfiguration config)
    {
        var patterns = config.Patterns!;
        var includePattern = patterns.Length > 0 ? patterns[0] : null;
        var excludePattern = patterns.Length > 1 ? patterns[1] : null;

        return new ResourceRegexFilter(includePattern, excludePattern, config.CaseInsensitive);
    }
}
