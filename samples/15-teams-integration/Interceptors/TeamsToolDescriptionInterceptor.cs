using McpProxy.Abstractions;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Interceptors;

/// <summary>
/// Intercepts the tool list to annotate Teams tool descriptions with the proxy behaviors that
/// apply to each tool (caching, pagination, credential scanning).
/// </summary>
/// <remarks>
/// This interceptor never rewrites the parameter documentation of a tool: it preserves the
/// backend's own description (and passes the backend's input schema through unchanged) and only
/// <b>appends</b> a short note about what the proxy adds. That way the parameter surface always
/// comes from the live underlying Teams MCP and cannot drift out of date in this sample.
/// </remarks>
public sealed class TeamsToolDescriptionInterceptor : IToolInterceptor
{
    private readonly TeamsIntegrationOptions _options;

    // Tool categories (normalized, lowercase, unprefixed) that the proxy applies behaviors to.
    // These mirror TeamsCacheInterceptor / TeamsCachePopulateHook / TeamsPaginationHook /
    // TeamsCredentialScanHook so the advertised behavior stays in step with the actual behavior.
    private static readonly HashSet<string> s_cacheManagedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "listchats", "listteams", "listchannels", "listchatmembers",
        "getchat", "getteam", "getchannel", "getuser", "getme"
    };

    private static readonly HashSet<string> s_paginatedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "listchats", "listteams", "listchannels", "listchatmembers",
        "listmessages", "listchatmessages", "listchannelmessages"
    };

    private static readonly HashSet<string> s_messageSendTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "postmessage", "sendchatmessage", "postchannelmessage", "sendchannelmessage",
        "replytomessage", "replytochannelmessage"
    };

    /// <summary>
    /// Suffix appended to Teams tools that are not in a specific behavior category, so the LLM still
    /// learns the proxy is active.
    /// </summary>
    private readonly string _proxySuffix;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="options">The Teams integration options.</param>
    public TeamsToolDescriptionInterceptor(TeamsIntegrationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var capabilities = new List<string>();
        if (options.EnableCacheShortCircuit)
        {
            capabilities.Add("auto-caching");
        }
        if (options.EnableAutoPagination)
        {
            capabilities.Add("auto-pagination");
        }
        if (options.EnableCredentialScanning)
        {
            capabilities.Add("credential scanning");
        }

        _proxySuffix = capabilities.Count > 0
            ? $" [Proxy: {string.Join(", ", capabilities)}]"
            : "";
    }

    /// <inheritdoc />
    public IEnumerable<ToolWithServer> InterceptTools(IEnumerable<ToolWithServer> tools)
    {
        foreach (var toolWithServer in tools)
        {
            // Only modify Teams server tools
            if (!IsTeamsTool(toolWithServer))
            {
                yield return toolWithServer;
                continue;
            }

            var baseName = GetBaseToolName(toolWithServer.Tool.Name);
            var original = toolWithServer.Tool.Description ?? "";
            var note = BuildProxyNote(baseName);

            string? enhanced = null;
            if (note is not null)
            {
                // Append the per-tool behavior note to the backend's own description.
                enhanced = string.IsNullOrWhiteSpace(original) ? note : $"{original.TrimEnd()}\n\n{note}";
            }
            else if (!string.IsNullOrEmpty(_proxySuffix))
            {
                // Uncategorized Teams tool: keep the original description + the general suffix.
                enhanced = original + _proxySuffix;
            }

            if (enhanced is not null)
            {
                toolWithServer.Tool = new Tool
                {
                    Name = toolWithServer.Tool.Name,
                    Title = toolWithServer.Tool.Title,
                    Description = enhanced,
                    InputSchema = toolWithServer.Tool.InputSchema,
                    OutputSchema = toolWithServer.Tool.OutputSchema,
                    Annotations = toolWithServer.Tool.Annotations,
                };
            }

            yield return toolWithServer;
        }
    }

    /// <summary>
    /// Builds a short note describing the proxy behaviors that apply to a tool, based on its
    /// category and the enabled options. Returns <see langword="null"/> when no specific behavior
    /// applies (the caller then falls back to the general proxy suffix).
    /// </summary>
    private string? BuildProxyNote(string baseName)
    {
        var name = baseName.ToLowerInvariant();
        var parts = new List<string>();

        if (_options.EnableCacheShortCircuit && s_cacheManagedTools.Contains(name))
        {
            parts.Add("results are cached by the proxy and repeat calls are served from cache (pass forceRefresh=true to bypass)");
        }

        if (_options.EnableAutoPagination && s_paginatedTools.Contains(name))
        {
            parts.Add("the proxy automatically bounds the result size to keep the call fast");
        }

        if (_options.EnableCredentialScanning && s_messageSendTools.Contains(name))
        {
            parts.Add("the proxy scans the message for secrets and blocks credentials");
        }

        return parts.Count > 0
            ? $"[Proxy: {string.Join("; ", parts)}.]"
            : null;
    }

    private bool IsTeamsTool(ToolWithServer tool)
    {
        // Match tools from the Teams server by server name or by known prefixes
        if (_options.TeamsServerName is not null)
        {
            return string.Equals(tool.ServerName, _options.TeamsServerName, StringComparison.OrdinalIgnoreCase);
        }

        // Default: match tools with teams_ prefix or from a server named "teams"
        return string.Equals(tool.ServerName, "teams", StringComparison.OrdinalIgnoreCase)
            || tool.Tool.Name.StartsWith("teams_", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetBaseToolName(string toolName)
    {
        // Strip common prefixes: teams_, microsoft-teams-, msgraph_, graph_
        ReadOnlySpan<string> prefixes = ["teams_", "microsoft-teams-", "msgraph_", "graph_"];
        foreach (var prefix in prefixes)
        {
            if (toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return toolName[prefix.Length..];
            }
        }

        return toolName;
    }
}
