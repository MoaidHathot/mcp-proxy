namespace McpProxy.Samples.TeamsIntegration.Cache.Models;

/// <summary>
/// Cached person/user information.
/// </summary>
public sealed record CachedPerson
{
    /// <summary>
    /// Display name of the person.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// User Principal Name (email).
    /// </summary>
    public required string Upn { get; init; }

    /// <summary>
    /// Azure AD user ID (GUID).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// When this entry was cached.
    /// </summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the MRI (Message Resource Identifier) for @mentions.
    /// Format: 8:orgid:{userId}
    /// </summary>
    public string Mri => $"8:orgid:{UserId}";
}

/// <summary>
/// Cached chat information.
/// </summary>
public sealed record CachedChat
{
    /// <summary>
    /// The chat ID (e.g., "19:abc...@thread.v2" or "48:notes" for self-chat).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Chat topic (may be empty for 1:1 chats).
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    /// Type of chat: OneOnOne, Group, Meeting, Self.
    /// </summary>
    public required string ChatType { get; init; }

    /// <summary>
    /// Members of the chat.
    /// </summary>
    public IReadOnlyList<CachedPerson> Members { get; init; } = [];

    /// <summary>
    /// When this entry was cached.
    /// </summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Checks if this is the self/notes chat.
    /// </summary>
    public bool IsSelfChat => Id == "48:notes" || ChatType.Equals("Self", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Cached team information.
/// </summary>
public sealed record CachedTeam
{
    /// <summary>
    /// Team ID (GUID).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name of the team.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Channels in this team.
    /// </summary>
    public IReadOnlyList<CachedChannel> Channels { get; init; } = [];

    /// <summary>
    /// When this entry was cached.
    /// </summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Cached channel information.
/// </summary>
public sealed record CachedChannel
{
    /// <summary>
    /// Channel ID (e.g., "19:abc...@thread.tacv2").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name of the channel.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Parent team ID.
    /// </summary>
    public required string TeamId { get; init; }
}

/// <summary>
/// Recent contact entry for fast lookup tier.
/// </summary>
public sealed record RecentContact
{
    /// <summary>
    /// Type of contact: Person, Chat, Channel.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Display name or topic.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The ID (userId, chatId, or channelId).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Parent ID (teamId for channels, null otherwise).
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// Additional identifier (UPN for person, chatType for chat).
    /// </summary>
    public string? SecondaryId { get; init; }

    /// <summary>
    /// When this contact was added.
    /// </summary>
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Result of a resolve operation.
/// </summary>
public sealed record ResolveResult
{
    /// <summary>
    /// Whether a match was found.
    /// </summary>
    public required bool Found { get; init; }

    /// <summary>
    /// Which tier the match came from: "contacts", "cache", "builtin".
    /// </summary>
    public string? Tier { get; init; }

    /// <summary>
    /// Matched person, if any.
    /// </summary>
    public CachedPerson? Person { get; init; }

    /// <summary>
    /// Matched chat, if any.
    /// </summary>
    public CachedChat? Chat { get; init; }

    /// <summary>
    /// Matched channel, if any.
    /// </summary>
    public CachedChannel? Channel { get; init; }

    /// <summary>
    /// Matched team, if any.
    /// </summary>
    public CachedTeam? Team { get; init; }

    /// <summary>
    /// All matches if the query is ambiguous.
    /// </summary>
    public AllMatches? AllMatches { get; init; }
}

/// <summary>
/// All matches from a resolve operation.
/// </summary>
public sealed record AllMatches
{
    public IReadOnlyList<CachedPerson> People { get; init; } = [];
    public IReadOnlyList<CachedChat> Chats { get; init; } = [];
    public IReadOnlyList<CachedTeam> Teams { get; init; } = [];
    public IReadOnlyList<CachedChannel> Channels { get; init; } = [];
}

/// <summary>
/// Cache status information.
/// </summary>
public sealed record CacheStatus
{
    public required int PersonCount { get; init; }
    public required int ChatCount { get; init; }
    public required int TeamCount { get; init; }
    public required int ChannelCount { get; init; }
    public required int RecentContactCount { get; init; }
    public required DateTimeOffset? ChatsLastRefreshed { get; init; }
    public required DateTimeOffset? TeamsLastRefreshed { get; init; }
    public required DateTimeOffset? PeopleLastRefreshed { get; init; }
    public required bool ChatsStale { get; init; }
    public required bool TeamsStale { get; init; }
    public required bool PeopleStale { get; init; }
    public required TimeSpan Ttl { get; init; }
}

/// <summary>
/// Scope for cache refresh operations.
/// </summary>
public enum CacheScope
{
    All,
    Chats,
    Teams,
    People
}
