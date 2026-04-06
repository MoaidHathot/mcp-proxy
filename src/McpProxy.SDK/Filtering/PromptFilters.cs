using System.Text.RegularExpressions;
using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using ModelContextProtocol.Protocol;

namespace McpProxy.SDK.Filtering;

/// <summary>
/// Filter that includes all prompts (no filtering).
/// </summary>
public sealed class NoPromptFilter : IPromptFilter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NoPromptFilter Instance = new();

    /// <inheritdoc />
    public bool ShouldInclude(Prompt prompt, string serverName) => true;
}

/// <summary>
/// Filter that includes only prompts matching specified name patterns.
/// </summary>
public sealed class PromptAllowListFilter : IPromptFilter
{
    private readonly HashSet<string> _exactMatches;
    private readonly Regex[]? _wildcardPatterns;
    private readonly bool _caseInsensitive;

    /// <summary>
    /// Initializes a new instance of <see cref="PromptAllowListFilter"/>.
    /// </summary>
    /// <param name="patterns">Name patterns to include (supports * and ? wildcards).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public PromptAllowListFilter(IEnumerable<string> patterns, bool caseInsensitive = true)
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
    public bool ShouldInclude(Prompt prompt, string serverName)
    {
        var name = prompt.Name;
        
        if (_exactMatches.Contains(name))
        {
            return true;
        }

        if (_wildcardPatterns is { Length: > 0 })
        {
            foreach (var pattern in _wildcardPatterns)
            {
                if (pattern.IsMatch(name))
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
/// Filter that excludes prompts matching specified name patterns.
/// </summary>
public sealed class PromptDenyListFilter : IPromptFilter
{
    private readonly PromptAllowListFilter _innerFilter;

    /// <summary>
    /// Initializes a new instance of <see cref="PromptDenyListFilter"/>.
    /// </summary>
    /// <param name="patterns">Name patterns to exclude (supports * and ? wildcards).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public PromptDenyListFilter(IEnumerable<string> patterns, bool caseInsensitive = true)
    {
        _innerFilter = new PromptAllowListFilter(patterns, caseInsensitive);
    }

    /// <inheritdoc />
    public bool ShouldInclude(Prompt prompt, string serverName)
    {
        // Include if NOT matched by the deny list
        return !_innerFilter.ShouldInclude(prompt, serverName);
    }
}

/// <summary>
/// Filter that uses regex patterns for prompt include/exclude.
/// </summary>
public sealed class PromptRegexFilter : IPromptFilter
{
    private readonly Regex? _includePattern;
    private readonly Regex? _excludePattern;

    /// <summary>
    /// Initializes a new instance of <see cref="PromptRegexFilter"/>.
    /// </summary>
    /// <param name="includePattern">Regex pattern for prompts to include (null = include all).</param>
    /// <param name="excludePattern">Regex pattern for prompts to exclude (null = exclude none).</param>
    /// <param name="caseInsensitive">Whether matching is case-insensitive.</param>
    public PromptRegexFilter(string? includePattern, string? excludePattern = null, bool caseInsensitive = true)
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
    public bool ShouldInclude(Prompt prompt, string serverName)
    {
        var name = prompt.Name;
        
        // If include pattern exists, prompt must match it
        if (_includePattern is not null && !_includePattern.IsMatch(name))
        {
            return false;
        }

        // If exclude pattern exists and matches, exclude the prompt
        if (_excludePattern is not null && _excludePattern.IsMatch(name))
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Factory for creating prompt filters from configuration.
/// </summary>
public static class PromptFilterFactory
{
    /// <summary>
    /// Creates a prompt filter from configuration.
    /// </summary>
    /// <param name="config">The filter configuration.</param>
    /// <returns>The created filter.</returns>
    public static IPromptFilter Create(FilterConfiguration config)
    {
        if (config.Mode == FilterMode.None || config.Patterns is null or { Length: 0 })
        {
            return NoPromptFilter.Instance;
        }

        return config.Mode switch
        {
            FilterMode.AllowList => new PromptAllowListFilter(config.Patterns, config.CaseInsensitive),
            FilterMode.DenyList => new PromptDenyListFilter(config.Patterns, config.CaseInsensitive),
            FilterMode.Regex => CreateRegexFilter(config),
            _ => NoPromptFilter.Instance
        };
    }

    private static PromptRegexFilter CreateRegexFilter(FilterConfiguration config)
    {
        var patterns = config.Patterns!;
        var includePattern = patterns.Length > 0 ? patterns[0] : null;
        var excludePattern = patterns.Length > 1 ? patterns[1] : null;

        return new PromptRegexFilter(includePattern, excludePattern, config.CaseInsensitive);
    }
}
