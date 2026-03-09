using McpProxy.Samples.TeamsIntegration.Cache.Models;

namespace McpProxy.Samples.TeamsIntegration.Cache;

/// <summary>
/// Service for caching and resolving Teams entities (people, chats, teams, channels).
/// Provides tiered lookup: recent contacts → full cache → built-in self-chat.
/// </summary>
public interface ITeamsCacheService
{
    // ═══════════════════════════════════════════════════════════════════════
    // Resolve Operations (tiered lookup)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a query to a person, chat, team, or channel using tiered lookup.
    /// Searches: recent contacts → full cache → built-in (self-chat).
    /// </summary>
    /// <param name="query">The name, UPN, ID, or alias to search for.</param>
    /// <returns>A resolve result indicating what was found and from which tier.</returns>
    ResolveResult Resolve(string query);

    /// <summary>
    /// Resolves a person by name, UPN, or user ID.
    /// </summary>
    CachedPerson? ResolvePerson(string query);

    /// <summary>
    /// Resolves a chat by topic, members, or chat ID.
    /// </summary>
    CachedChat? ResolveChat(string query);

    /// <summary>
    /// Resolves a team by name or team ID.
    /// </summary>
    CachedTeam? ResolveTeam(string query);

    /// <summary>
    /// Resolves a channel by name or channel ID.
    /// Optionally scoped to a specific team.
    /// </summary>
    CachedChannel? ResolveChannel(string query, string? teamId = null);

    // ═══════════════════════════════════════════════════════════════════════
    // Direct Lookup Operations (exact match by ID)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets a person by exact user ID.
    /// </summary>
    CachedPerson? GetPersonById(string userId);

    /// <summary>
    /// Gets a person by exact UPN.
    /// </summary>
    CachedPerson? GetPersonByUpn(string upn);

    /// <summary>
    /// Gets a chat by exact chat ID.
    /// </summary>
    CachedChat? GetChatById(string chatId);

    /// <summary>
    /// Gets a team by exact team ID.
    /// </summary>
    CachedTeam? GetTeamById(string teamId);

    /// <summary>
    /// Gets a channel by exact channel ID.
    /// </summary>
    CachedChannel? GetChannelById(string channelId);

    // ═══════════════════════════════════════════════════════════════════════
    // Cache Population Operations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds or updates a person in the cache.
    /// </summary>
    void CachePerson(CachedPerson person);

    /// <summary>
    /// Adds or updates multiple people in the cache.
    /// </summary>
    void CachePeople(IEnumerable<CachedPerson> people);

    /// <summary>
    /// Adds or updates a chat in the cache.
    /// </summary>
    void CacheChat(CachedChat chat);

    /// <summary>
    /// Adds or updates multiple chats in the cache.
    /// </summary>
    void CacheChats(IEnumerable<CachedChat> chats);

    /// <summary>
    /// Adds or updates a team in the cache.
    /// </summary>
    void CacheTeam(CachedTeam team);

    /// <summary>
    /// Adds or updates multiple teams in the cache.
    /// </summary>
    void CacheTeams(IEnumerable<CachedTeam> teams);

    /// <summary>
    /// Adds or updates a channel in the cache.
    /// </summary>
    void CacheChannel(CachedChannel channel);

    /// <summary>
    /// Adds or updates multiple channels in the cache.
    /// </summary>
    void CacheChannels(IEnumerable<CachedChannel> channels);

    // ═══════════════════════════════════════════════════════════════════════
    // Recent Contacts Operations (fast lookup tier)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a contact to the recent contacts list.
    /// </summary>
    void AddRecentContact(RecentContact contact);

    /// <summary>
    /// Gets all recent contacts.
    /// </summary>
    IReadOnlyList<RecentContact> GetRecentContacts();

    /// <summary>
    /// Clears the recent contacts list.
    /// </summary>
    void ClearRecentContacts();

    // ═══════════════════════════════════════════════════════════════════════
    // Cache Management Operations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets the current cache status including counts and staleness.
    /// </summary>
    CacheStatus GetStatus();

    /// <summary>
    /// Checks if the specified cache scope needs refresh (is stale).
    /// </summary>
    bool IsStale(CacheScope scope);

    /// <summary>
    /// Marks a cache scope as refreshed (updates the last refresh timestamp).
    /// </summary>
    void MarkRefreshed(CacheScope scope);

    /// <summary>
    /// Invalidates (clears) the specified cache scope.
    /// </summary>
    void Invalidate(CacheScope scope);

    /// <summary>
    /// Saves the cache to disk.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the cache from disk.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════════
    // List Operations (for cache short-circuit)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets all cached chats. Returns null if cache is stale/empty.
    /// </summary>
    IReadOnlyList<CachedChat>? GetAllChats();

    /// <summary>
    /// Gets all cached teams. Returns null if cache is stale/empty.
    /// </summary>
    IReadOnlyList<CachedTeam>? GetAllTeams();

    /// <summary>
    /// Gets all cached people. Returns null if cache is stale/empty.
    /// </summary>
    IReadOnlyList<CachedPerson>? GetAllPeople();

    /// <summary>
    /// Gets all cached channels for a team. Returns null if cache is stale/empty.
    /// </summary>
    IReadOnlyList<CachedChannel>? GetChannelsForTeam(string teamId);

    /// <summary>
    /// Gets chat members. Returns null if chat not found or cache is stale.
    /// </summary>
    IReadOnlyList<CachedPerson>? GetChatMembers(string chatId);
}
