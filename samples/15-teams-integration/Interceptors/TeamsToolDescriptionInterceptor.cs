using McpProxy.Abstractions;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Interceptors;

/// <summary>
/// Intercepts the tool list to enhance Teams tool descriptions with proxy capabilities.
/// This tells the LLM about the automatic behaviors (caching, pagination, credential scanning)
/// so it doesn't need to read a separate Skill document.
/// </summary>
public sealed class TeamsToolDescriptionInterceptor : IToolInterceptor
{
    private readonly TeamsIntegrationOptions _options;

    // Enhanced descriptions for Teams MCP tools.
    // These replace the original descriptions to inform the LLM about proxy-added behaviors.
    // The proxy handles caching, pagination, credential scanning, and resolution transparently —
    // the LLM should NOT be told to call virtual tools; it just calls these tools directly.
    private static readonly Dictionary<string, string> s_enhancedDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ListChats"] = """
            List the user's chats. The proxy automatically adds pagination (top=20) if not specified \
            and caches results for faster subsequent lookups. Pass forceRefresh=true to bypass cache. \
            Supports filters: userUpns (array of UPNs), topic (chat topic), top (page size, default 20).
            """,
        ["ListTeams"] = """
            List the user's teams. The proxy automatically adds pagination and caches results. \
            Pass forceRefresh=true to bypass cache and get live data. \
            Requires userId (GUID) parameter.
            """,
        ["ListChannels"] = """
            List channels for a team. The proxy automatically caches results. \
            Pass forceRefresh=true to bypass cache. \
            Requires teamId from a prior ListTeams call.
            """,
        ["ListChatMembers"] = """
            List members of a chat. The proxy caches member data automatically. \
            Pass forceRefresh=true to bypass cache. \
            Requires chatId. Results include displayName, UPN, and userId for each member.
            """,
        ["PostMessage"] = """
            Send a message to a chat. The proxy automatically scans for credentials and blocks messages \
            containing API keys, tokens, passwords, or other secrets. \
            Use contentType='html' for formatted messages (bold, lists, links, headings, tables). \
            For plain text, use contentType='text'.
            """,
        ["SendChatMessage"] = """
            Send a message to a chat. The proxy automatically scans for credentials and blocks messages \
            containing API keys, tokens, passwords, or other secrets. \
            Use contentType='html' for formatted messages.
            """,
        ["PostChannelMessage"] = """
            Post a message to a channel. The proxy automatically scans for credentials and blocks \
            messages containing secrets. \
            Use contentType='html' for formatted messages.
            """,
        ["SendChannelMessage"] = """
            Post a message to a channel. The proxy automatically scans for credentials and blocks \
            messages containing secrets. \
            Use contentType='html' for formatted messages.
            """,
        ["ReplyToChannelMessage"] = """
            Reply to a channel message thread. The proxy automatically scans for credentials. \
            Requires teamId, channelId, and messageId. Use contentType='html' for formatting.
            """,
        ["ReplyToMessage"] = """
            Reply to a message. The proxy automatically scans for credentials. \
            Requires the parent messageId. Use contentType='html' for formatting.
            """,
        ["ListChatMessages"] = """
            Read messages from a chat. Supports OData query parameters: \
            $top (limit results), $filter (e.g. date range), $orderby (e.g. 'createdDateTime desc'). \
            Example: top=10, orderby='createdDateTime desc' for the 10 most recent messages.
            """,
        ["ListChannelMessages"] = """
            Read messages from a channel. Supports OData: $top, $expand='replies' (include thread replies). \
            The proxy caches results for faster subsequent reads.
            """,
        ["GetChat"] = """
            Get details of a specific chat by chatId. The proxy returns cached data if available and fresh. \
            Pass forceRefresh=true to bypass cache and get live data.
            """,
        ["GetTeam"] = """
            Get details of a specific team by teamId. The proxy returns cached data if available and fresh. \
            Pass forceRefresh=true to bypass cache. \
            Supports $select and $expand parameters.
            """,
        ["GetChannel"] = """
            Get details of a specific channel. The proxy returns cached data if available and fresh. \
            Pass forceRefresh=true to bypass cache. \
            Requires teamId and channelId. Supports $select and $filter.
            """,
        ["CreateChat"] = """
            Create a new chat. For 1:1 chat: chatType='oneOnOne' with member UPNs. \
            For group chat: chatType='group' with topic and member UPNs. \
            Always include the current user's UPN when creating chats.
            """,
        ["SearchTeamsMessages"] = """
            Search across Teams messages using natural language. Returns matching messages with context.
            """,
    };

    /// <summary>
    /// Suffix appended to all Teams tool descriptions to inform the LLM about proxy capabilities.
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

            // Get the base tool name (strip prefix)
            var baseName = GetBaseToolName(toolWithServer.Tool.Name);

            if (s_enhancedDescriptions.TryGetValue(baseName, out var enhanced))
            {
                toolWithServer.Tool = new Tool
                {
                    Name = toolWithServer.Tool.Name,
                    Title = toolWithServer.Tool.Title,
                    Description = enhanced.Trim(),
                    InputSchema = toolWithServer.Tool.InputSchema,
                    OutputSchema = toolWithServer.Tool.OutputSchema,
                    Annotations = toolWithServer.Tool.Annotations,
                };
            }
            else if (!string.IsNullOrEmpty(_proxySuffix))
            {
                // For tools without a custom description, append the proxy capabilities suffix
                toolWithServer.Tool = new Tool
                {
                    Name = toolWithServer.Tool.Name,
                    Title = toolWithServer.Tool.Title,
                    Description = (toolWithServer.Tool.Description ?? "") + _proxySuffix,
                    InputSchema = toolWithServer.Tool.InputSchema,
                    OutputSchema = toolWithServer.Tool.OutputSchema,
                    Annotations = toolWithServer.Tool.Annotations,
                };
            }

            yield return toolWithServer;
        }
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
