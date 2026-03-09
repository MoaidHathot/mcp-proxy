using System.Collections.Concurrent;
using System.Text.Json;
using McpProxy.Samples.TeamsIntegration.Cache.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Samples.TeamsIntegration.Cache;

/// <summary>
/// Thread-safe Teams cache implementation with JSON file persistence.
/// </summary>
public sealed class TeamsCacheService : ITeamsCacheService, IDisposable
{
    private readonly ConcurrentDictionary<string, CachedPerson> _peopleById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedPerson> _peopleByUpn = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedChat> _chatsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedTeam> _teamsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedChannel> _channelsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<CachedChannel>> _channelsByTeam = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<RecentContact> _recentContacts = new();

    private readonly TimeSpan _ttl;
    private readonly int _maxRecentContacts;
    private readonly string _cacheFilePath;
    private readonly ILogger<TeamsCacheService>? _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    private DateTimeOffset? _chatsLastRefreshed;
    private DateTimeOffset? _teamsLastRefreshed;
    private DateTimeOffset? _peopleLastRefreshed;

    // Built-in self-chat (always available)
    private static readonly CachedChat s_selfChat = new()
    {
        Id = "48:notes",
        Topic = "Notes / Self Chat",
        ChatType = "Self",
        Members = []
    };

    /// <summary>
    /// Initializes a new instance of <see cref="TeamsCacheService"/>.
    /// </summary>
    /// <param name="cacheFilePath">Path to the JSON cache file.</param>
    /// <param name="ttl">Time-to-live for cache entries. Default is 4 hours.</param>
    /// <param name="maxRecentContacts">Maximum number of recent contacts to keep. Default is 50.</param>
    /// <param name="logger">Optional logger.</param>
    public TeamsCacheService(
        string? cacheFilePath = null,
        TimeSpan? ttl = null,
        int maxRecentContacts = 50,
        ILogger<TeamsCacheService>? logger = null)
    {
        _cacheFilePath = cacheFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mcp-proxy",
            "teams-cache.json");
        _ttl = ttl ?? TimeSpan.FromHours(4);
        _maxRecentContacts = maxRecentContacts;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Resolve Operations (tiered lookup)
    // ═══════════════════════════════════════════════════════════════════════

    public ResolveResult Resolve(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ResolveResult { Found = false };
        }

        var normalizedQuery = query.Trim();

        // Check for self-chat aliases
        if (IsSelfChatQuery(normalizedQuery))
        {
            return new ResolveResult
            {
                Found = true,
                Tier = "builtin",
                Chat = s_selfChat
            };
        }

        // Tier 1: Recent contacts (fastest)
        var recentMatch = FindInRecentContacts(normalizedQuery);
        if (recentMatch is not null)
        {
            return recentMatch;
        }

        // Tier 2: Full cache search
        var cacheMatch = FindInCache(normalizedQuery);
        if (cacheMatch is not null)
        {
            return cacheMatch;
        }

        // Nothing found - return all potential matches for disambiguation
        return new ResolveResult
        {
            Found = false,
            AllMatches = FindAllMatches(normalizedQuery)
        };
    }

    public CachedPerson? ResolvePerson(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var normalizedQuery = query.Trim();

        // Direct ID/UPN lookup
        if (_peopleById.TryGetValue(normalizedQuery, out var byId)) return byId;
        if (_peopleByUpn.TryGetValue(normalizedQuery, out var byUpn)) return byUpn;

        // Fuzzy name match
        return _peopleById.Values
            .FirstOrDefault(p => p.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || p.Upn.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase));
    }

    public CachedChat? ResolveChat(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var normalizedQuery = query.Trim();

        // Self-chat check
        if (IsSelfChatQuery(normalizedQuery)) return s_selfChat;

        // Direct ID lookup
        if (_chatsById.TryGetValue(normalizedQuery, out var byId)) return byId;

        // Topic match
        var byTopic = _chatsById.Values
            .FirstOrDefault(c => c.Topic?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) == true);
        if (byTopic is not null) return byTopic;

        // Member name match (for 1:1 chats)
        return _chatsById.Values
            .FirstOrDefault(c => c.Members.Any(m =>
                m.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)));
    }

    public CachedTeam? ResolveTeam(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var normalizedQuery = query.Trim();

        // Direct ID lookup
        if (_teamsById.TryGetValue(normalizedQuery, out var byId)) return byId;

        // Name match
        return _teamsById.Values
            .FirstOrDefault(t => t.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
    }

    public CachedChannel? ResolveChannel(string query, string? teamId = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var normalizedQuery = query.Trim();

        // Direct ID lookup
        if (_channelsById.TryGetValue(normalizedQuery, out var byId))
        {
            if (teamId is null || byId.TeamId.Equals(teamId, StringComparison.OrdinalIgnoreCase))
            {
                return byId;
            }
        }

        // Search in specific team or all channels
        IEnumerable<CachedChannel> searchScope = teamId is not null && _channelsByTeam.TryGetValue(teamId, out var teamChannels)
            ? teamChannels
            : _channelsById.Values;

        return searchScope
            .FirstOrDefault(c => c.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Direct Lookup Operations
    // ═══════════════════════════════════════════════════════════════════════

    public CachedPerson? GetPersonById(string userId) =>
        _peopleById.TryGetValue(userId, out var person) ? person : null;

    public CachedPerson? GetPersonByUpn(string upn) =>
        _peopleByUpn.TryGetValue(upn, out var person) ? person : null;

    public CachedChat? GetChatById(string chatId)
    {
        if (IsSelfChatId(chatId)) return s_selfChat;
        return _chatsById.TryGetValue(chatId, out var chat) ? chat : null;
    }

    public CachedTeam? GetTeamById(string teamId) =>
        _teamsById.TryGetValue(teamId, out var team) ? team : null;

    public CachedChannel? GetChannelById(string channelId) =>
        _channelsById.TryGetValue(channelId, out var channel) ? channel : null;

    // ═══════════════════════════════════════════════════════════════════════
    // Cache Population Operations
    // ═══════════════════════════════════════════════════════════════════════

    public void CachePerson(CachedPerson person)
    {
        _peopleById[person.UserId] = person;
        _peopleByUpn[person.Upn] = person;
    }

    public void CachePeople(IEnumerable<CachedPerson> people)
    {
        foreach (var person in people)
        {
            CachePerson(person);
        }
    }

    public void CacheChat(CachedChat chat)
    {
        _chatsById[chat.Id] = chat;

        // Also cache members
        foreach (var member in chat.Members)
        {
            CachePerson(member);
        }
    }

    public void CacheChats(IEnumerable<CachedChat> chats)
    {
        foreach (var chat in chats)
        {
            CacheChat(chat);
        }
    }

    public void CacheTeam(CachedTeam team)
    {
        _teamsById[team.Id] = team;

        // Cache channels from the team
        if (team.Channels.Count > 0)
        {
            var channelList = new List<CachedChannel>(team.Channels);
            _channelsByTeam[team.Id] = channelList;

            foreach (var channel in team.Channels)
            {
                _channelsById[channel.Id] = channel;
            }
        }
    }

    public void CacheTeams(IEnumerable<CachedTeam> teams)
    {
        foreach (var team in teams)
        {
            CacheTeam(team);
        }
    }

    public void CacheChannel(CachedChannel channel)
    {
        _channelsById[channel.Id] = channel;

        _channelsByTeam.AddOrUpdate(
            channel.TeamId,
            _ => [channel],
            (_, list) =>
            {
                var existing = list.FindIndex(c => c.Id == channel.Id);
                if (existing >= 0)
                {
                    list[existing] = channel;
                }
                else
                {
                    list.Add(channel);
                }
                return list;
            });
    }

    public void CacheChannels(IEnumerable<CachedChannel> channels)
    {
        foreach (var channel in channels)
        {
            CacheChannel(channel);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Recent Contacts Operations
    // ═══════════════════════════════════════════════════════════════════════

    public void AddRecentContact(RecentContact contact)
    {
        _recentContacts.Enqueue(contact);

        // Trim excess contacts
        while (_recentContacts.Count > _maxRecentContacts)
        {
            _recentContacts.TryDequeue(out _);
        }
    }

    public IReadOnlyList<RecentContact> GetRecentContacts() =>
        [.. _recentContacts];

    public void ClearRecentContacts()
    {
        while (_recentContacts.TryDequeue(out _)) { }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cache Management Operations
    // ═══════════════════════════════════════════════════════════════════════

    public CacheStatus GetStatus()
    {
        return new CacheStatus
        {
            PersonCount = _peopleById.Count,
            ChatCount = _chatsById.Count,
            TeamCount = _teamsById.Count,
            ChannelCount = _channelsById.Count,
            RecentContactCount = _recentContacts.Count,
            ChatsLastRefreshed = _chatsLastRefreshed,
            TeamsLastRefreshed = _teamsLastRefreshed,
            PeopleLastRefreshed = _peopleLastRefreshed,
            ChatsStale = IsStale(CacheScope.Chats),
            TeamsStale = IsStale(CacheScope.Teams),
            PeopleStale = IsStale(CacheScope.People),
            Ttl = _ttl
        };
    }

    public bool IsStale(CacheScope scope)
    {
        var now = DateTimeOffset.UtcNow;
        return scope switch
        {
            CacheScope.Chats => _chatsLastRefreshed is null || now - _chatsLastRefreshed > _ttl,
            CacheScope.Teams => _teamsLastRefreshed is null || now - _teamsLastRefreshed > _ttl,
            CacheScope.People => _peopleLastRefreshed is null || now - _peopleLastRefreshed > _ttl,
            CacheScope.All => IsStale(CacheScope.Chats) || IsStale(CacheScope.Teams) || IsStale(CacheScope.People),
            _ => true
        };
    }

    public void MarkRefreshed(CacheScope scope)
    {
        var now = DateTimeOffset.UtcNow;
        switch (scope)
        {
            case CacheScope.Chats:
                _chatsLastRefreshed = now;
                break;
            case CacheScope.Teams:
                _teamsLastRefreshed = now;
                break;
            case CacheScope.People:
                _peopleLastRefreshed = now;
                break;
            case CacheScope.All:
                _chatsLastRefreshed = _teamsLastRefreshed = _peopleLastRefreshed = now;
                break;
        }
    }

    public void Invalidate(CacheScope scope)
    {
        switch (scope)
        {
            case CacheScope.Chats:
                _chatsById.Clear();
                _chatsLastRefreshed = null;
                break;
            case CacheScope.Teams:
                _teamsById.Clear();
                _channelsById.Clear();
                _channelsByTeam.Clear();
                _teamsLastRefreshed = null;
                break;
            case CacheScope.People:
                _peopleById.Clear();
                _peopleByUpn.Clear();
                _peopleLastRefreshed = null;
                break;
            case CacheScope.All:
                Invalidate(CacheScope.Chats);
                Invalidate(CacheScope.Teams);
                Invalidate(CacheScope.People);
                ClearRecentContacts();
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // List Operations (for cache short-circuit)
    // ═══════════════════════════════════════════════════════════════════════

    public IReadOnlyList<CachedChat>? GetAllChats()
    {
        if (IsStale(CacheScope.Chats) || _chatsById.IsEmpty) return null;
        return [.. _chatsById.Values];
    }

    public IReadOnlyList<CachedTeam>? GetAllTeams()
    {
        if (IsStale(CacheScope.Teams) || _teamsById.IsEmpty) return null;
        return [.. _teamsById.Values];
    }

    public IReadOnlyList<CachedPerson>? GetAllPeople()
    {
        if (IsStale(CacheScope.People) || _peopleById.IsEmpty) return null;
        return [.. _peopleById.Values];
    }

    public IReadOnlyList<CachedChannel>? GetChannelsForTeam(string teamId)
    {
        if (IsStale(CacheScope.Teams)) return null;
        return _channelsByTeam.TryGetValue(teamId, out var channels)
            ? [.. channels]
            : null;
    }

    public IReadOnlyList<CachedPerson>? GetChatMembers(string chatId)
    {
        if (IsStale(CacheScope.Chats)) return null;
        var chat = GetChatById(chatId);
        return chat?.Members as IReadOnlyList<CachedPerson>;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Persistence Operations
    // ═══════════════════════════════════════════════════════════════════════

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cacheData = new CacheData
            {
                People = [.. _peopleById.Values],
                Chats = [.. _chatsById.Values],
                Teams = [.. _teamsById.Values],
                Channels = [.. _channelsById.Values],
                RecentContacts = [.. _recentContacts],
                ChatsLastRefreshed = _chatsLastRefreshed,
                TeamsLastRefreshed = _teamsLastRefreshed,
                PeopleLastRefreshed = _peopleLastRefreshed,
                SavedAt = DateTimeOffset.UtcNow
            };

            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(cacheData, _jsonOptions);
            await File.WriteAllTextAsync(_cacheFilePath, json, cancellationToken).ConfigureAwait(false);

            if (_logger?.IsEnabled(LogLevel.Information) == true)
            {
                _logger.LogInformation(
                    "Teams cache saved to {Path}: {People} people, {Chats} chats, {Teams} teams, {Channels} channels",
                    _cacheFilePath, cacheData.People.Count, cacheData.Chats.Count,
                    cacheData.Teams.Count, cacheData.Channels.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save Teams cache to {Path}", _cacheFilePath);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_cacheFilePath))
        {
            if (_logger?.IsEnabled(LogLevel.Information) == true)
            {
                _logger.LogInformation("No existing Teams cache file found at {Path}", _cacheFilePath);
            }

            return;
        }

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(_cacheFilePath, cancellationToken).ConfigureAwait(false);
            var cacheData = JsonSerializer.Deserialize<CacheData>(json, _jsonOptions);

            if (cacheData is null)
            {
                _logger?.LogWarning("Teams cache file at {Path} was empty or invalid", _cacheFilePath);
                return;
            }

            // Restore data
            foreach (var person in cacheData.People)
            {
                _peopleById[person.UserId] = person;
                _peopleByUpn[person.Upn] = person;
            }

            foreach (var chat in cacheData.Chats)
            {
                _chatsById[chat.Id] = chat;
            }

            foreach (var team in cacheData.Teams)
            {
                _teamsById[team.Id] = team;
            }

            foreach (var channel in cacheData.Channels)
            {
                _channelsById[channel.Id] = channel;
                _channelsByTeam.AddOrUpdate(
                    channel.TeamId,
                    _ => [channel],
                    (_, list) => { list.Add(channel); return list; });
            }

            foreach (var contact in cacheData.RecentContacts)
            {
                _recentContacts.Enqueue(contact);
            }

            _chatsLastRefreshed = cacheData.ChatsLastRefreshed;
            _teamsLastRefreshed = cacheData.TeamsLastRefreshed;
            _peopleLastRefreshed = cacheData.PeopleLastRefreshed;

            if (_logger?.IsEnabled(LogLevel.Information) == true)
            {
                _logger.LogInformation(
                    "Teams cache loaded from {Path}: {People} people, {Chats} chats, {Teams} teams, {Channels} channels",
                    _cacheFilePath, cacheData.People.Count, cacheData.Chats.Count,
                    cacheData.Teams.Count, cacheData.Channels.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load Teams cache from {Path}", _cacheFilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private Helper Methods
    // ═══════════════════════════════════════════════════════════════════════

    private static bool IsSelfChatQuery(string query)
    {
        var selfAliases = new[] { "self", "notes", "my notes", "self-chat", "selfchat", "me", "48:notes" };
        return selfAliases.Any(a => query.Equals(a, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSelfChatId(string chatId) =>
        chatId.Equals("48:notes", StringComparison.OrdinalIgnoreCase);

    private ResolveResult? FindInRecentContacts(string query)
    {
        var recent = _recentContacts.FirstOrDefault(c =>
            c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Id.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            (c.SecondaryId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        if (recent is null) return null;

        return recent.Type switch
        {
            "Person" => new ResolveResult
            {
                Found = true,
                Tier = "contacts",
                Person = GetPersonById(recent.Id)
            },
            "Chat" => new ResolveResult
            {
                Found = true,
                Tier = "contacts",
                Chat = GetChatById(recent.Id)
            },
            "Channel" => new ResolveResult
            {
                Found = true,
                Tier = "contacts",
                Channel = GetChannelById(recent.Id),
                Team = recent.ParentId is not null ? GetTeamById(recent.ParentId) : null
            },
            "Team" => new ResolveResult
            {
                Found = true,
                Tier = "contacts",
                Team = GetTeamById(recent.Id)
            },
            _ => null
        };
    }

    private ResolveResult? FindInCache(string query)
    {
        // Try person first
        var person = ResolvePerson(query);
        if (person is not null)
        {
            return new ResolveResult { Found = true, Tier = "cache", Person = person };
        }

        // Try chat
        var chat = ResolveChat(query);
        if (chat is not null)
        {
            return new ResolveResult { Found = true, Tier = "cache", Chat = chat };
        }

        // Try team
        var team = ResolveTeam(query);
        if (team is not null)
        {
            return new ResolveResult { Found = true, Tier = "cache", Team = team };
        }

        // Try channel
        var channel = ResolveChannel(query);
        if (channel is not null)
        {
            var parentTeam = GetTeamById(channel.TeamId);
            return new ResolveResult { Found = true, Tier = "cache", Channel = channel, Team = parentTeam };
        }

        return null;
    }

    private AllMatches FindAllMatches(string query)
    {
        return new AllMatches
        {
            People = _peopleById.Values
                .Where(p => p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           p.Upn.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList(),
            Chats = _chatsById.Values
                .Where(c => (c.Topic?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                           c.Members.Any(m => m.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Take(5)
                .ToList(),
            Teams = _teamsById.Values
                .Where(t => t.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList(),
            Channels = _channelsById.Values
                .Where(c => c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList()
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _fileLock.Dispose();
    }

    /// <summary>
    /// Internal model for JSON serialization of the cache.
    /// </summary>
    private sealed class CacheData
    {
        public List<CachedPerson> People { get; set; } = [];
        public List<CachedChat> Chats { get; set; } = [];
        public List<CachedTeam> Teams { get; set; } = [];
        public List<CachedChannel> Channels { get; set; } = [];
        public List<RecentContact> RecentContacts { get; set; } = [];
        public DateTimeOffset? ChatsLastRefreshed { get; set; }
        public DateTimeOffset? TeamsLastRefreshed { get; set; }
        public DateTimeOffset? PeopleLastRefreshed { get; set; }
        public DateTimeOffset SavedAt { get; set; }
    }
}
