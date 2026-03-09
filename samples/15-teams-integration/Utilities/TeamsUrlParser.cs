using System.Text.RegularExpressions;
using System.Web;

namespace McpProxy.Samples.TeamsIntegration.Utilities;

/// <summary>
/// Result of parsing a Teams URL.
/// </summary>
public sealed record TeamsUrlParseResult
{
    /// <summary>
    /// Whether the URL was successfully parsed as a Teams URL.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The type of Teams entity: Chat, Channel, Message, Meeting, Team, or Unknown.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Chat ID if present (format: 19:xxx@thread.v2 or 48:notes).
    /// </summary>
    public string? ChatId { get; init; }

    /// <summary>
    /// Team ID if present (GUID format).
    /// </summary>
    public string? TeamId { get; init; }

    /// <summary>
    /// Channel ID if present (format: 19:xxx@thread.tacv2).
    /// </summary>
    public string? ChannelId { get; init; }

    /// <summary>
    /// Message ID if present.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Parent message ID if this is a reply.
    /// </summary>
    public string? ParentMessageId { get; init; }

    /// <summary>
    /// Meeting ID if present.
    /// </summary>
    public string? MeetingId { get; init; }

    /// <summary>
    /// Tenant ID if present.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Error message if parsing failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// The original URL that was parsed.
    /// </summary>
    public string? OriginalUrl { get; init; }
}

/// <summary>
/// Parses Microsoft Teams URLs to extract entity IDs.
/// Supports chat links, channel links, meeting links, and message deep links.
/// </summary>
public static partial class TeamsUrlParser
{
    /// <summary>
    /// Parses a Teams URL and extracts relevant IDs.
    /// </summary>
    /// <param name="url">The Teams URL to parse.</param>
    /// <returns>The parse result with extracted IDs.</returns>
    public static TeamsUrlParseResult Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new TeamsUrlParseResult
            {
                Success = false,
                Type = "Unknown",
                Error = "URL is empty or null"
            };
        }

        var trimmedUrl = url.Trim();

        // Check if it's a Teams URL
        if (!IsTeamsUrl(trimmedUrl))
        {
            return new TeamsUrlParseResult
            {
                Success = false,
                Type = "Unknown",
                Error = "Not a Microsoft Teams URL",
                OriginalUrl = trimmedUrl
            };
        }

        try
        {
            var uri = new Uri(trimmedUrl);

            // Try different URL patterns
            var chatResult = TryParseChatUrl(uri);
            if (chatResult is not null) return chatResult with { OriginalUrl = trimmedUrl };

            var channelResult = TryParseChannelUrl(uri);
            if (channelResult is not null) return channelResult with { OriginalUrl = trimmedUrl };

            var meetingResult = TryParseMeetingUrl(uri);
            if (meetingResult is not null) return meetingResult with { OriginalUrl = trimmedUrl };

            var messageResult = TryParseMessageUrl(uri);
            if (messageResult is not null) return messageResult with { OriginalUrl = trimmedUrl };

            // Fallback: try to extract any IDs from query parameters
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var contextParam = queryParams["context"];

            if (!string.IsNullOrEmpty(contextParam))
            {
                var contextResult = TryParseContext(contextParam);
                if (contextResult is not null) return contextResult with { OriginalUrl = trimmedUrl };
            }

            return new TeamsUrlParseResult
            {
                Success = false,
                Type = "Unknown",
                Error = "Could not parse Teams URL structure",
                OriginalUrl = trimmedUrl
            };
        }
        catch (UriFormatException ex)
        {
            return new TeamsUrlParseResult
            {
                Success = false,
                Type = "Unknown",
                Error = $"Invalid URL format: {ex.Message}",
                OriginalUrl = trimmedUrl
            };
        }
    }

    /// <summary>
    /// Checks if a URL is a Microsoft Teams URL.
    /// </summary>
    public static bool IsTeamsUrl(string url) =>
        !string.IsNullOrWhiteSpace(url) &&
        (url.Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
         url.Contains("teams.live.com", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("msteams:", StringComparison.OrdinalIgnoreCase));

    // ═══════════════════════════════════════════════════════════════════════
    // URL Pattern Parsers
    // ═══════════════════════════════════════════════════════════════════════

    private static TeamsUrlParseResult? TryParseChatUrl(Uri uri)
    {
        // Pattern: /l/chat/{chatId}
        // Pattern: /_#/conversations/{chatId}
        var path = uri.AbsolutePath;

        var chatMatch = ChatPathPattern().Match(path);
        if (chatMatch.Success)
        {
            var chatId = HttpUtility.UrlDecode(chatMatch.Groups["chatId"].Value);
            return new TeamsUrlParseResult
            {
                Success = true,
                Type = "Chat",
                ChatId = chatId
            };
        }

        // Check for chat in query params
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        var threadId = queryParams["threadId"];

        if (!string.IsNullOrEmpty(threadId) && IsChatId(threadId))
        {
            return new TeamsUrlParseResult
            {
                Success = true,
                Type = "Chat",
                ChatId = threadId
            };
        }

        return null;
    }

    private static TeamsUrlParseResult? TryParseChannelUrl(Uri uri)
    {
        // Pattern: /l/channel/{channelId}/...?groupId={teamId}
        // Pattern: /_#/channel/{channelId}/...?groupId={teamId}&tenantId={tenantId}
        var path = uri.AbsolutePath;
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var channelMatch = ChannelPathPattern().Match(path);
        if (channelMatch.Success)
        {
            var channelId = HttpUtility.UrlDecode(channelMatch.Groups["channelId"].Value);
            var teamId = queryParams["groupId"];
            var tenantId = queryParams["tenantId"];

            return new TeamsUrlParseResult
            {
                Success = true,
                Type = "Channel",
                ChannelId = channelId,
                TeamId = teamId,
                TenantId = tenantId
            };
        }

        return null;
    }

    private static TeamsUrlParseResult? TryParseMeetingUrl(Uri uri)
    {
        // Pattern: /l/meetup-join/{meetingId}
        // Pattern: /meet/{meetingLink}
        var path = uri.AbsolutePath;

        var meetingMatch = MeetingPathPattern().Match(path);
        if (meetingMatch.Success)
        {
            var meetingId = HttpUtility.UrlDecode(meetingMatch.Groups["meetingId"].Value);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var tenantId = queryParams["tenantId"];

            return new TeamsUrlParseResult
            {
                Success = true,
                Type = "Meeting",
                MeetingId = meetingId,
                TenantId = tenantId
            };
        }

        return null;
    }

    private static TeamsUrlParseResult? TryParseMessageUrl(Uri uri)
    {
        // Pattern with message context in query params
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        var messageId = queryParams["messageId"];
        var parentMessageId = queryParams["parentMessageId"];
        var threadId = queryParams["threadId"];

        if (!string.IsNullOrEmpty(messageId))
        {
            // Determine if it's a chat or channel message
            var isChannel = !string.IsNullOrEmpty(threadId) && IsChannelId(threadId);

            return new TeamsUrlParseResult
            {
                Success = true,
                Type = "Message",
                MessageId = messageId,
                ParentMessageId = parentMessageId,
                ChatId = isChannel ? null : threadId,
                ChannelId = isChannel ? threadId : null,
                TeamId = queryParams["groupId"],
                TenantId = queryParams["tenantId"]
            };
        }

        return null;
    }

    private static TeamsUrlParseResult? TryParseContext(string contextJson)
    {
        // Context is URL-encoded JSON with threadId, chatId, etc.
        try
        {
            var decoded = HttpUtility.UrlDecode(contextJson);

            // Simple extraction without full JSON parsing
            string? chatId = null;
            string? channelId = null;
            string? teamId = null;

            var chatIdMatch = Regex.Match(decoded, @"""chatId""\s*:\s*""([^""]+)""");
            if (chatIdMatch.Success) chatId = chatIdMatch.Groups[1].Value;

            var threadIdMatch = Regex.Match(decoded, @"""threadId""\s*:\s*""([^""]+)""");
            if (threadIdMatch.Success)
            {
                var threadId = threadIdMatch.Groups[1].Value;
                if (IsChannelId(threadId)) channelId = threadId;
                else if (IsChatId(threadId)) chatId = threadId;
            }

            var groupIdMatch = Regex.Match(decoded, @"""groupId""\s*:\s*""([^""]+)""");
            if (groupIdMatch.Success) teamId = groupIdMatch.Groups[1].Value;

            if (chatId is not null || channelId is not null)
            {
                return new TeamsUrlParseResult
                {
                    Success = true,
                    Type = channelId is not null ? "Channel" : "Chat",
                    ChatId = chatId,
                    ChannelId = channelId,
                    TeamId = teamId
                };
            }
        }
        catch
        {
            // Failed to parse context
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ID Format Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if an ID looks like a chat ID (19:xxx@thread.v2 or 48:notes).
    /// </summary>
    public static bool IsChatId(string id) =>
        !string.IsNullOrEmpty(id) &&
        (id.Contains("@thread.v2", StringComparison.OrdinalIgnoreCase) ||
         id.Equals("48:notes", StringComparison.OrdinalIgnoreCase) ||
         (id.StartsWith("19:", StringComparison.OrdinalIgnoreCase) && !id.Contains("@thread.tacv2")));

    /// <summary>
    /// Checks if an ID looks like a channel ID (19:xxx@thread.tacv2).
    /// </summary>
    public static bool IsChannelId(string id) =>
        !string.IsNullOrEmpty(id) &&
        id.Contains("@thread.tacv2", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if an ID looks like a team ID (GUID format).
    /// </summary>
    public static bool IsTeamId(string id) =>
        Guid.TryParse(id, out _);

    // ═══════════════════════════════════════════════════════════════════════
    // Compiled Regex Patterns
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Matches chat paths: /l/chat/{chatId} or /_#/conversations/{chatId}
    /// </summary>
    [GeneratedRegex(
        @"(?:/l/chat/|/_#/conversations/)(?<chatId>[^/?]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ChatPathPattern();

    /// <summary>
    /// Matches channel paths: /l/channel/{channelId} or /_#/channel/{channelId}
    /// </summary>
    [GeneratedRegex(
        @"(?:/l/channel/|/_#/channel/)(?<channelId>[^/?]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ChannelPathPattern();

    /// <summary>
    /// Matches meeting paths: /l/meetup-join/{meetingId} or /meet/{meetingLink}
    /// </summary>
    [GeneratedRegex(
        @"(?:/l/meetup-join/|/meet/)(?<meetingId>[^/?]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MeetingPathPattern();
}
