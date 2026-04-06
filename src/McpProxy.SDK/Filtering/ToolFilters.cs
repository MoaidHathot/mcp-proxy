using System.Text.RegularExpressions;
using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using ModelContextProtocol.Protocol;

namespace McpProxy.SDK.Filtering;

/// <summary>
/// Filter that includes all tools (no filtering).
/// </summary>
public sealed class NoFilter : IToolFilter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NoFilter Instance = new();

    /// <inheritdoc />
    public bool ShouldInclude(Tool tool, string serverName) => true;
}

/// <summary>
/// Filter that includes only tools matching specified patterns.
/// </summary>
public sealed class AllowListFilter : IToolFilter
{
    private readonly HashSet<string> _exactMatches;
    private readonly Regex[]? _wildcardPatterns;
    private readonly bool _caseInsensitive;

    /// <summary>
    /// Initializes a new instance of <see cref="AllowListFilter"/>.
    /// </summary>
    /// <param name="patterns">Patterns to include (supports * and ? wildcards).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public AllowListFilter(IEnumerable<string> patterns, bool caseInsensitive = true)
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
    public bool ShouldInclude(Tool tool, string serverName)
    {
        if (_exactMatches.Contains(tool.Name))
        {
            return true;
        }

        if (_wildcardPatterns is { Length: > 0 })
        {
            foreach (var pattern in _wildcardPatterns)
            {
                if (pattern.IsMatch(tool.Name))
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
/// Filter that excludes tools matching specified patterns.
/// </summary>
public sealed class DenyListFilter : IToolFilter
{
    private readonly AllowListFilter _innerFilter;

    /// <summary>
    /// Initializes a new instance of <see cref="DenyListFilter"/>.
    /// </summary>
    /// <param name="patterns">Patterns to exclude (supports * and ? wildcards).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public DenyListFilter(IEnumerable<string> patterns, bool caseInsensitive = true)
    {
        _innerFilter = new AllowListFilter(patterns, caseInsensitive);
    }

    /// <inheritdoc />
    public bool ShouldInclude(Tool tool, string serverName)
    {
        // Include if NOT matched by the deny list
        return !_innerFilter.ShouldInclude(tool, serverName);
    }
}

/// <summary>
/// Filter that uses regex patterns for include/exclude.
/// </summary>
public sealed class RegexFilter : IToolFilter
{
    private readonly Regex? _includePattern;
    private readonly Regex? _excludePattern;

    /// <summary>
    /// Initializes a new instance of <see cref="RegexFilter"/>.
    /// </summary>
    /// <param name="includePattern">Regex pattern for tools to include (null = include all).</param>
    /// <param name="excludePattern">Regex pattern for tools to exclude (null = exclude none).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public RegexFilter(string? includePattern, string? excludePattern = null, bool caseInsensitive = true)
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
    public bool ShouldInclude(Tool tool, string serverName)
    {
        // If include pattern exists, tool must match it
        if (_includePattern is not null && !_includePattern.IsMatch(tool.Name))
        {
            return false;
        }

        // If exclude pattern exists and matches, exclude the tool
        if (_excludePattern is not null && _excludePattern.IsMatch(tool.Name))
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Factory for creating filters from configuration.
/// </summary>
public static class FilterFactory
{
    /// <summary>
    /// Creates a filter from configuration.
    /// </summary>
    /// <param name="config">The filter configuration.</param>
    /// <returns>The created filter.</returns>
    public static IToolFilter Create(FilterConfiguration config)
    {
        if (config.Mode == FilterMode.None || config.Patterns is null or { Length: 0 })
        {
            return NoFilter.Instance;
        }

        return config.Mode switch
        {
            FilterMode.AllowList => new AllowListFilter(config.Patterns, config.CaseInsensitive),
            FilterMode.DenyList => new DenyListFilter(config.Patterns, config.CaseInsensitive),
            FilterMode.Regex => CreateRegexFilter(config),
            _ => NoFilter.Instance
        };
    }

    private static RegexFilter CreateRegexFilter(FilterConfiguration config)
    {
        var patterns = config.Patterns!;
        var includePattern = patterns.Length > 0 ? patterns[0] : null;
        var excludePattern = patterns.Length > 1 ? patterns[1] : null;

        return new RegexFilter(includePattern, excludePattern, config.CaseInsensitive);
    }
}
