using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration.Cache;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Interceptors;

/// <summary>
/// A tool call interceptor that short-circuits calls to ListChats, ListTeams, etc.
/// when fresh cached data is available, avoiding unnecessary MCP calls.
/// </summary>
public sealed partial class TeamsCacheInterceptor : IToolCallInterceptor
{
    private readonly ILogger<TeamsCacheInterceptor> _logger;
    private readonly ITeamsCacheService _cacheService;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="TeamsCacheInterceptor"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cacheService">The cache service to use for short-circuiting.</param>
    public TeamsCacheInterceptor(
        ILogger<TeamsCacheInterceptor> logger,
        ITeamsCacheService cacheService)
    {
        _logger = logger;
        _cacheService = cacheService;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc />
    public int Priority => 100; // Execute early to avoid MCP calls when possible

    /// <inheritdoc />
    public ValueTask<CallToolResult?> InterceptAsync(ToolCallContext context, CancellationToken cancellationToken)
    {
        var normalizedTool = NormalizeToolName(context.ToolName);

        // Try to short-circuit based on tool name
        var cachedResult = normalizedTool switch
        {
            "listchats" => TryGetCachedChats(),
            "listteams" => TryGetCachedTeams(),
            "listchannels" => TryGetCachedChannels(context),
            "listchatmembers" => TryGetCachedChatMembers(context),
            "getchat" => TryGetCachedChat(context),
            "getteam" => TryGetCachedTeam(context),
            "getchannel" => TryGetCachedChannel(context),
            _ => null
        };

        if (cachedResult is not null)
        {
            LogCacheHit(_logger, context.ToolName);
            return ValueTask.FromResult<CallToolResult?>(cachedResult);
        }

        // No cache hit - continue with normal MCP call
        return ValueTask.FromResult<CallToolResult?>(null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cache Lookup Methods
    // ═══════════════════════════════════════════════════════════════════════

    private CallToolResult? TryGetCachedChats()
    {
        var chats = _cacheService.GetAllChats();
        if (chats is null)
        {
            return null;
        }

        LogCacheLookup(_logger, "chats", chats.Count);

        var response = new
        {
            value = chats.Select(c => new
            {
                id = c.Id,
                topic = c.Topic,
                chatType = c.ChatType,
                memberCount = c.Members.Count,
                cachedAt = c.CachedAt,
                isCached = true
            }),
            fromCache = true,
            cacheStatus = _cacheService.GetStatus()
        };

        return CreateJsonResult(response);
    }

    private CallToolResult? TryGetCachedTeams()
    {
        var teams = _cacheService.GetAllTeams();
        if (teams is null)
        {
            return null;
        }

        LogCacheLookup(_logger, "teams", teams.Count);

        var response = new
        {
            value = teams.Select(t => new
            {
                id = t.Id,
                displayName = t.DisplayName,
                channelCount = t.Channels.Count,
                cachedAt = t.CachedAt,
                isCached = true
            }),
            fromCache = true,
            cacheStatus = _cacheService.GetStatus()
        };

        return CreateJsonResult(response);
    }

    private CallToolResult? TryGetCachedChannels(ToolCallContext context)
    {
        // Get teamId from request arguments
        var teamId = GetArgumentString(context, "teamId", "team_id", "groupId");
        if (string.IsNullOrEmpty(teamId))
        {
            return null;
        }

        var channels = _cacheService.GetChannelsForTeam(teamId);
        if (channels is null)
        {
            return null;
        }

        LogCacheLookupChannels(_logger, teamId, channels.Count);

        var response = new
        {
            value = channels.Select(c => new
            {
                id = c.Id,
                displayName = c.DisplayName,
                teamId = c.TeamId,
                isCached = true
            }),
            fromCache = true,
            teamId
        };

        return CreateJsonResult(response);
    }

    private CallToolResult? TryGetCachedChatMembers(ToolCallContext context)
    {
        // Get chatId from request arguments
        var chatId = GetArgumentString(context, "chatId", "chat_id", "threadId");
        if (string.IsNullOrEmpty(chatId))
        {
            return null;
        }

        var members = _cacheService.GetChatMembers(chatId);
        if (members is null)
        {
            return null;
        }

        LogCacheLookupMembers(_logger, chatId, members.Count);

        var response = new
        {
            value = members.Select(m => new
            {
                id = m.UserId,
                displayName = m.DisplayName,
                userPrincipalName = m.Upn,
                mri = m.Mri,
                isCached = true
            }),
            fromCache = true,
            chatId
        };

        return CreateJsonResult(response);
    }

    private CallToolResult? TryGetCachedChat(ToolCallContext context)
    {
        var chatId = GetArgumentString(context, "chatId", "chat_id", "id");
        if (string.IsNullOrEmpty(chatId))
        {
            return null;
        }

        var chat = _cacheService.GetChatById(chatId);
        if (chat is null)
        {
            return null;
        }

        LogCacheLookup(_logger, "chat", 1);

        var response = new
        {
            id = chat.Id,
            topic = chat.Topic,
            chatType = chat.ChatType,
            members = chat.Members.Select(m => new
            {
                id = m.UserId,
                displayName = m.DisplayName,
                userPrincipalName = m.Upn
            }),
            cachedAt = chat.CachedAt,
            isCached = true
        };

        return CreateJsonResult(response);
    }

    private CallToolResult? TryGetCachedTeam(ToolCallContext context)
    {
        var teamId = GetArgumentString(context, "teamId", "team_id", "id", "groupId");
        if (string.IsNullOrEmpty(teamId))
        {
            return null;
        }

        var team = _cacheService.GetTeamById(teamId);
        if (team is null)
        {
            return null;
        }

        LogCacheLookup(_logger, "team", 1);

        var response = new
        {
            id = team.Id,
            displayName = team.DisplayName,
            channels = team.Channels.Select(c => new
            {
                id = c.Id,
                displayName = c.DisplayName
            }),
            cachedAt = team.CachedAt,
            isCached = true
        };

        return CreateJsonResult(response);
    }

    private CallToolResult? TryGetCachedChannel(ToolCallContext context)
    {
        var channelId = GetArgumentString(context, "channelId", "channel_id", "id");
        if (string.IsNullOrEmpty(channelId))
        {
            return null;
        }

        var channel = _cacheService.GetChannelById(channelId);
        if (channel is null)
        {
            return null;
        }

        LogCacheLookup(_logger, "channel", 1);

        var response = new
        {
            id = channel.Id,
            displayName = channel.DisplayName,
            teamId = channel.TeamId,
            isCached = true
        };

        return CreateJsonResult(response);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════

    private CallToolResult CreateJsonResult(object data)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }]
        };
    }

    private static string? GetArgumentString(ToolCallContext context, params string[] paramNames)
    {
        var args = context.Request?.Arguments;
        if (args is null)
        {
            return null;
        }

        foreach (var name in paramNames)
        {
            if (args.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string NormalizeToolName(string toolName)
    {
        // Remove common prefixes and convert to lowercase
        var normalized = toolName
            .Replace("teams_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("msgraph_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("graph_", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        return normalized;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Logging
    // ═══════════════════════════════════════════════════════════════════════

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache hit for {ToolName}, returning cached data")]
    private static partial void LogCacheHit(ILogger logger, string toolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache lookup for {EntityType}: found {Count} items")]
    private static partial void LogCacheLookup(ILogger logger, string entityType, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache lookup for channels in team {TeamId}: found {Count} items")]
    private static partial void LogCacheLookupChannels(ILogger logger, string teamId, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache lookup for members in chat {ChatId}: found {Count} items")]
    private static partial void LogCacheLookupMembers(ILogger logger, string chatId, int count);
}
