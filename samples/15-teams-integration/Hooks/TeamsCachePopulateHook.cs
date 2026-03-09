using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration.Cache;
using McpProxy.Samples.TeamsIntegration.Cache.Models;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Hooks;

/// <summary>
/// A post-invoke hook that automatically populates the Teams cache from MCP tool responses.
/// Extracts people, chats, teams, and channels from ListChats, ListTeams, etc. responses.
/// </summary>
public sealed partial class TeamsCachePopulateHook : IPostInvokeHook
{
    private readonly ILogger<TeamsCachePopulateHook> _logger;
    private readonly ITeamsCacheService _cacheService;
    private readonly bool _autoSave;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="TeamsCachePopulateHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cacheService">The cache service to populate.</param>
    /// <param name="autoSave">Whether to auto-save cache after updates. Default is false.</param>
    public TeamsCachePopulateHook(
        ILogger<TeamsCachePopulateHook> logger,
        ITeamsCacheService cacheService,
        bool autoSave = false)
    {
        _logger = logger;
        _cacheService = cacheService;
        _autoSave = autoSave;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc />
    public int Priority => 900; // Execute late, after other processing

    /// <inheritdoc />
    public async ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        // Don't process error results
        if (result.IsError ?? false)
        {
            return result;
        }

        var toolName = context.ToolName;
        var textContent = GetTextContent(result);

        if (string.IsNullOrEmpty(textContent))
        {
            return result;
        }

        try
        {
            // Try to parse and cache based on tool name
            var cached = await TryPopulateCacheAsync(toolName, textContent).ConfigureAwait(false);

            if (cached && _autoSave)
            {
                await _cacheService.SaveAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Don't fail the request if caching fails
            LogCachePopulateError(_logger, toolName, ex.Message);
        }

        return result;
    }

    private ValueTask<bool> TryPopulateCacheAsync(string toolName, string content)
    {
        // Normalize tool name (remove prefixes)
        var normalizedTool = NormalizeToolName(toolName);

        var result = normalizedTool switch
        {
            "listchats" => TryPopulateChats(content),
            "listteams" => TryPopulateTeams(content),
            "listchannels" => TryPopulateChannels(content),
            "listchatmembers" => TryPopulatePeople(content),
            "getuser" or "getme" => TryPopulatePerson(content),
            "getchat" => TryPopulateSingleChat(content),
            "getteam" => TryPopulateSingleTeam(content),
            "getchannel" => TryPopulateSingleChannel(content),
            _ => false
        };

        return ValueTask.FromResult(result);
    }

    private bool TryPopulateChats(string content)
    {
        var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        // Look for 'value' array (Graph API response format)
        var chatsArray = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? value
                : default;

        if (chatsArray.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var cachedChats = new List<CachedChat>();

        foreach (var chatElement in chatsArray.EnumerateArray())
        {
            var chat = ParseChat(chatElement);
            if (chat is not null)
            {
                cachedChats.Add(chat);
            }
        }

        if (cachedChats.Count > 0)
        {
            _cacheService.CacheChats(cachedChats);
            _cacheService.MarkRefreshed(CacheScope.Chats);
            LogChatsPopulated(_logger, cachedChats.Count);
            return true;
        }

        return false;
    }

    private bool TryPopulateTeams(string content)
    {
        var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        var teamsArray = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? value
                : default;

        if (teamsArray.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var cachedTeams = new List<CachedTeam>();

        foreach (var teamElement in teamsArray.EnumerateArray())
        {
            var team = ParseTeam(teamElement);
            if (team is not null)
            {
                cachedTeams.Add(team);
            }
        }

        if (cachedTeams.Count > 0)
        {
            _cacheService.CacheTeams(cachedTeams);
            _cacheService.MarkRefreshed(CacheScope.Teams);
            LogTeamsPopulated(_logger, cachedTeams.Count);
            return true;
        }

        return false;
    }

    private bool TryPopulateChannels(string content)
    {
        var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        var channelsArray = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? value
                : default;

        if (channelsArray.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var cachedChannels = new List<CachedChannel>();

        foreach (var channelElement in channelsArray.EnumerateArray())
        {
            var channel = ParseChannel(channelElement);
            if (channel is not null)
            {
                cachedChannels.Add(channel);
            }
        }

        if (cachedChannels.Count > 0)
        {
            _cacheService.CacheChannels(cachedChannels);
            LogChannelsPopulated(_logger, cachedChannels.Count);
            return true;
        }

        return false;
    }

    private bool TryPopulatePeople(string content)
    {
        var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        var peopleArray = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? value
                : default;

        if (peopleArray.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var cachedPeople = new List<CachedPerson>();

        foreach (var personElement in peopleArray.EnumerateArray())
        {
            var person = ParsePerson(personElement);
            if (person is not null)
            {
                cachedPeople.Add(person);
            }
        }

        if (cachedPeople.Count > 0)
        {
            _cacheService.CachePeople(cachedPeople);
            LogPeoplePopulated(_logger, cachedPeople.Count);
            return true;
        }

        return false;
    }

    private bool TryPopulatePerson(string content)
    {
        var jsonDoc = JsonDocument.Parse(content);
        var person = ParsePerson(jsonDoc.RootElement);

        if (person is not null)
        {
            _cacheService.CachePerson(person);
            LogPersonPopulated(_logger, person.DisplayName);
            return true;
        }

        return false;
    }

    private bool TryPopulateSingleChat(string content)
    {
        var jsonDoc = JsonDocument.Parse(content);
        var chat = ParseChat(jsonDoc.RootElement);

        if (chat is not null)
        {
            _cacheService.CacheChat(chat);
            LogChatPopulated(_logger, chat.Id);
            return true;
        }

        return false;
    }

    private bool TryPopulateSingleTeam(string content)
    {
        var jsonDoc = JsonDocument.Parse(content);
        var team = ParseTeam(jsonDoc.RootElement);

        if (team is not null)
        {
            _cacheService.CacheTeam(team);
            LogTeamPopulated(_logger, team.DisplayName);
            return true;
        }

        return false;
    }

    private bool TryPopulateSingleChannel(string content)
    {
        var jsonDoc = JsonDocument.Parse(content);
        var channel = ParseChannel(jsonDoc.RootElement);

        if (channel is not null)
        {
            _cacheService.CacheChannel(channel);
            LogChannelPopulated(_logger, channel.DisplayName);
            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Parsing Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static CachedPerson? ParsePerson(JsonElement element)
    {
        // Try different property names for user ID
        var userId = GetStringProperty(element, "id", "userId", "user_id");
        var displayName = GetStringProperty(element, "displayName", "display_name", "name");
        var upn = GetStringProperty(element, "userPrincipalName", "email", "mail", "upn");

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(displayName))
        {
            return null;
        }

        return new CachedPerson
        {
            UserId = userId,
            DisplayName = displayName,
            Upn = upn ?? $"{userId}@unknown"
        };
    }

    private static CachedChat? ParseChat(JsonElement element)
    {
        var chatId = GetStringProperty(element, "id", "chatId", "chat_id");
        var chatType = GetStringProperty(element, "chatType", "chat_type", "type") ?? "Unknown";
        var topic = GetStringProperty(element, "topic", "name", "displayName");

        if (string.IsNullOrEmpty(chatId))
        {
            return null;
        }

        // Parse members if present
        var members = new List<CachedPerson>();
        if (element.TryGetProperty("members", out var membersElement) && membersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var memberElement in membersElement.EnumerateArray())
            {
                var person = ParsePerson(memberElement);
                if (person is not null)
                {
                    members.Add(person);
                }
            }
        }

        return new CachedChat
        {
            Id = chatId,
            ChatType = chatType,
            Topic = topic,
            Members = members
        };
    }

    private static CachedTeam? ParseTeam(JsonElement element)
    {
        var teamId = GetStringProperty(element, "id", "teamId", "team_id");
        var displayName = GetStringProperty(element, "displayName", "display_name", "name");

        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(displayName))
        {
            return null;
        }

        // Parse channels if present
        var channels = new List<CachedChannel>();
        if (element.TryGetProperty("channels", out var channelsElement) && channelsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var channelElement in channelsElement.EnumerateArray())
            {
                var channel = ParseChannelWithTeamId(channelElement, teamId);
                if (channel is not null)
                {
                    channels.Add(channel);
                }
            }
        }

        return new CachedTeam
        {
            Id = teamId,
            DisplayName = displayName,
            Channels = channels
        };
    }

    private static CachedChannel? ParseChannel(JsonElement element)
    {
        var channelId = GetStringProperty(element, "id", "channelId", "channel_id");
        var displayName = GetStringProperty(element, "displayName", "display_name", "name");
        var teamId = GetStringProperty(element, "teamId", "team_id", "groupId");

        if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(displayName))
        {
            return null;
        }

        return new CachedChannel
        {
            Id = channelId,
            DisplayName = displayName,
            TeamId = teamId ?? "unknown"
        };
    }

    private static CachedChannel? ParseChannelWithTeamId(JsonElement element, string teamId)
    {
        var channelId = GetStringProperty(element, "id", "channelId", "channel_id");
        var displayName = GetStringProperty(element, "displayName", "display_name", "name");

        if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(displayName))
        {
            return null;
        }

        return new CachedChannel
        {
            Id = channelId,
            DisplayName = displayName,
            TeamId = teamId
        };
    }

    private static string? GetStringProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private static string? GetTextContent(CallToolResult result)
    {
        if (result.Content is null)
        {
            return null;
        }

        foreach (var content in result.Content)
        {
            if (content is TextContentBlock textContent)
            {
                return textContent.Text;
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Populated cache with {Count} chats")]
    private static partial void LogChatsPopulated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Populated cache with {Count} teams")]
    private static partial void LogTeamsPopulated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Populated cache with {Count} channels")]
    private static partial void LogChannelsPopulated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Populated cache with {Count} people")]
    private static partial void LogPeoplePopulated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Populated cache with person: {Name}")]
    private static partial void LogPersonPopulated(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Populated cache with chat: {ChatId}")]
    private static partial void LogChatPopulated(ILogger logger, string chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Populated cache with team: {Name}")]
    private static partial void LogTeamPopulated(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Populated cache with channel: {Name}")]
    private static partial void LogChannelPopulated(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to populate cache from {ToolName}: {Error}")]
    private static partial void LogCachePopulateError(ILogger logger, string toolName, string error);
}
