using System.Text.Json;
using McpProxy.Samples.TeamsIntegration.Cache;
using McpProxy.Samples.TeamsIntegration.Cache.Models;
using McpProxy.Samples.TeamsIntegration.Utilities;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.VirtualTools;

/// <summary>
/// Provides virtual tools for Teams integration that execute locally without MCP calls.
/// </summary>
public sealed class TeamsVirtualTools
{
    private readonly ITeamsCacheService _cacheService;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="TeamsVirtualTools"/>.
    /// </summary>
    /// <param name="cacheService">The cache service to use.</param>
    public TeamsVirtualTools(ITeamsCacheService cacheService)
    {
        _cacheService = cacheService;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tool Definitions
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets all virtual tool definitions.
    /// </summary>
    public IEnumerable<(Tool Tool, Func<CallToolRequestParams, CancellationToken, ValueTask<CallToolResult>> Handler)> GetTools()
    {
        yield return (s_teamsResolveTool, HandleTeamsResolve);
        yield return (s_teamsLookupPersonTool, HandleTeamsLookupPerson);
        yield return (s_teamsLookupChatTool, HandleTeamsLookupChat);
        yield return (s_teamsLookupTeamTool, HandleTeamsLookupTeam);
        yield return (s_teamsLookupChannelTool, HandleTeamsLookupChannel);
        yield return (s_teamsCacheStatusTool, HandleTeamsCacheStatus);
        yield return (s_teamsCacheRefreshTool, HandleTeamsCacheRefresh);
        yield return (s_teamsParseUrlTool, HandleTeamsParseUrl);
        yield return (s_teamsValidateMessageTool, HandleTeamsValidateMessage);
        yield return (s_teamsAddRecentContactTool, HandleTeamsAddRecentContact);
        yield return (s_teamsGetRecentContactsTool, HandleTeamsGetRecentContacts);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper method to create InputSchema as JsonElement
    // ═══════════════════════════════════════════════════════════════════════

    private static JsonElement CreateSchema(object schemaObject)
    {
        return JsonSerializer.SerializeToElement(schemaObject);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper method to get string argument from request
    // ═══════════════════════════════════════════════════════════════════════

    private static string? GetStringArg(CallToolRequestParams request, string key)
    {
        if (request.Arguments is null)
        {
            return null;
        }

        if (request.Arguments.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }

    private static string GetStringArgOrDefault(CallToolRequestParams request, string key, string defaultValue)
    {
        return GetStringArg(request, key) ?? defaultValue;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_resolve - Unified resolver for people, chats, teams, channels
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsResolveTool = new()
    {
        Name = "teams_resolve",
        Description = "Resolve a name, email, or ID to a Teams entity (person, chat, team, or channel). " +
                     "Uses tiered lookup: recent contacts → cache → built-in (self-chat). " +
                     "Use this before sending messages to find the correct chat/channel ID.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "The name, email, UPN, or ID to search for. " +
                                 "Examples: 'John Smith', 'john@contoso.com', 'self', 'Engineering Team'"
                },
                type = new
                {
                    type = "string",
                    @enum = new[] { "any", "person", "chat", "team", "channel" },
                    description = "Restrict search to a specific entity type. Default is 'any'."
                }
            },
            required = new[] { "query" }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsResolve(CallToolRequestParams request, CancellationToken ct)
    {
        var query = GetStringArgOrDefault(request, "query", "");
        var typeFilter = GetStringArgOrDefault(request, "type", "any");

        var result = _cacheService.Resolve(query);

        // Apply type filter if specified
        if (typeFilter != "any" && result.Found)
        {
            var matches = typeFilter switch
            {
                "person" => result.Person is not null,
                "chat" => result.Chat is not null,
                "team" => result.Team is not null,
                "channel" => result.Channel is not null,
                _ => true
            };

            if (!matches)
            {
                result = new ResolveResult { Found = false, AllMatches = result.AllMatches };
            }
        }

        return ValueTask.FromResult(CreateJsonResult(result));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_lookup_person - Direct person lookup by ID or UPN
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsLookupPersonTool = new()
    {
        Name = "teams_lookup_person",
        Description = "Look up a person by user ID or UPN (email) from the local cache.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                userId = new
                {
                    type = "string",
                    description = "The Azure AD user ID (GUID)"
                },
                upn = new
                {
                    type = "string",
                    description = "The user principal name (email address)"
                }
            }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsLookupPerson(CallToolRequestParams request, CancellationToken ct)
    {
        var userId = GetStringArg(request, "userId");
        var upn = GetStringArg(request, "upn");

        CachedPerson? person = null;

        if (!string.IsNullOrEmpty(userId))
        {
            person = _cacheService.GetPersonById(userId);
        }
        else if (!string.IsNullOrEmpty(upn))
        {
            person = _cacheService.GetPersonByUpn(upn);
        }

        if (person is null)
        {
            return ValueTask.FromResult(CreateJsonResult(new { found = false, message = "Person not found in cache" }));
        }

        return ValueTask.FromResult(CreateJsonResult(new
        {
            found = true,
            person = new
            {
                userId = person.UserId,
                displayName = person.DisplayName,
                upn = person.Upn,
                mri = person.Mri,
                cachedAt = person.CachedAt
            }
        }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_lookup_chat - Direct chat lookup by ID
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsLookupChatTool = new()
    {
        Name = "teams_lookup_chat",
        Description = "Look up a chat by ID from the local cache.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                chatId = new
                {
                    type = "string",
                    description = "The chat ID (e.g., '19:abc...@thread.v2' or '48:notes' for self-chat)"
                }
            },
            required = new[] { "chatId" }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsLookupChat(CallToolRequestParams request, CancellationToken ct)
    {
        var chatId = GetStringArgOrDefault(request, "chatId", "");
        var chat = _cacheService.GetChatById(chatId);

        if (chat is null)
        {
            return ValueTask.FromResult(CreateJsonResult(new { found = false, message = "Chat not found in cache" }));
        }

        return ValueTask.FromResult(CreateJsonResult(new
        {
            found = true,
            chat = new
            {
                id = chat.Id,
                topic = chat.Topic,
                chatType = chat.ChatType,
                isSelfChat = chat.IsSelfChat,
                memberCount = chat.Members.Count,
                members = chat.Members.Select(m => new { m.DisplayName, m.Upn }),
                cachedAt = chat.CachedAt
            }
        }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_lookup_team - Direct team lookup by ID
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsLookupTeamTool = new()
    {
        Name = "teams_lookup_team",
        Description = "Look up a team by ID from the local cache.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                teamId = new
                {
                    type = "string",
                    description = "The team ID (GUID)"
                }
            },
            required = new[] { "teamId" }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsLookupTeam(CallToolRequestParams request, CancellationToken ct)
    {
        var teamId = GetStringArgOrDefault(request, "teamId", "");
        var team = _cacheService.GetTeamById(teamId);

        if (team is null)
        {
            return ValueTask.FromResult(CreateJsonResult(new { found = false, message = "Team not found in cache" }));
        }

        return ValueTask.FromResult(CreateJsonResult(new
        {
            found = true,
            team = new
            {
                id = team.Id,
                displayName = team.DisplayName,
                channelCount = team.Channels.Count,
                channels = team.Channels.Select(c => new { c.Id, c.DisplayName }),
                cachedAt = team.CachedAt
            }
        }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_lookup_channel - Direct channel lookup by ID
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsLookupChannelTool = new()
    {
        Name = "teams_lookup_channel",
        Description = "Look up a channel by ID from the local cache.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                channelId = new
                {
                    type = "string",
                    description = "The channel ID (e.g., '19:abc...@thread.tacv2')"
                }
            },
            required = new[] { "channelId" }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsLookupChannel(CallToolRequestParams request, CancellationToken ct)
    {
        var channelId = GetStringArgOrDefault(request, "channelId", "");
        var channel = _cacheService.GetChannelById(channelId);

        if (channel is null)
        {
            return ValueTask.FromResult(CreateJsonResult(new { found = false, message = "Channel not found in cache" }));
        }

        var team = _cacheService.GetTeamById(channel.TeamId);

        return ValueTask.FromResult(CreateJsonResult(new
        {
            found = true,
            channel = new
            {
                id = channel.Id,
                displayName = channel.DisplayName,
                teamId = channel.TeamId,
                teamName = team?.DisplayName
            }
        }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_cache_status - Get cache status and statistics
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsCacheStatusTool = new()
    {
        Name = "teams_cache_status",
        Description = "Get the current status of the Teams cache including counts, freshness, and TTL information.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new { }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsCacheStatus(CallToolRequestParams request, CancellationToken ct)
    {
        var status = _cacheService.GetStatus();
        return ValueTask.FromResult(CreateJsonResult(status));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_cache_refresh - Invalidate cache to force refresh
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsCacheRefreshTool = new()
    {
        Name = "teams_cache_refresh",
        Description = "Invalidate the Teams cache to force a refresh on the next lookup. " +
                     "Use this if you know the cache is stale or need fresh data.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                scope = new
                {
                    type = "string",
                    @enum = new[] { "all", "chats", "teams", "people" },
                    description = "Which part of the cache to invalidate. Default is 'all'."
                }
            }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsCacheRefresh(CallToolRequestParams request, CancellationToken ct)
    {
        var scopeStr = GetStringArgOrDefault(request, "scope", "all");

        var scope = scopeStr.ToLowerInvariant() switch
        {
            "chats" => CacheScope.Chats,
            "teams" => CacheScope.Teams,
            "people" => CacheScope.People,
            _ => CacheScope.All
        };

        _cacheService.Invalidate(scope);

        return ValueTask.FromResult(CreateJsonResult(new
        {
            success = true,
            message = $"Cache invalidated for scope: {scope}",
            newStatus = _cacheService.GetStatus()
        }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_parse_url - Parse Teams URLs to extract IDs
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsParseUrlTool = new()
    {
        Name = "teams_parse_url",
        Description = "Parse a Microsoft Teams URL to extract chat ID, channel ID, team ID, message ID, etc. " +
                     "Use this when a user shares a Teams link and you need to identify the target.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                url = new
                {
                    type = "string",
                    description = "The Teams URL to parse"
                }
            },
            required = new[] { "url" }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsParseUrl(CallToolRequestParams request, CancellationToken ct)
    {
        var url = GetStringArg(request, "url");
        var result = TeamsUrlParser.Parse(url);
        return ValueTask.FromResult(CreateJsonResult(result));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_validate_message - Check message for credentials before sending
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsValidateMessageTool = new()
    {
        Name = "teams_validate_message",
        Description = "Validate a message for credentials and sensitive data before sending. " +
                     "Returns whether the message is safe to send.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                message = new
                {
                    type = "string",
                    description = "The message content to validate"
                }
            },
            required = new[] { "message" }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsValidateMessage(CallToolRequestParams request, CancellationToken ct)
    {
        var message = GetStringArg(request, "message");
        var result = CredentialScanner.Scan(message);

        return ValueTask.FromResult(CreateJsonResult(new
        {
            safe = !result.HasCredentials,
            hasCredentials = result.HasCredentials,
            detectedTypes = result.DetectedTypes,
            summary = result.Summary,
            recommendation = result.HasCredentials
                ? "Do not send this message. Remove sensitive data first."
                : "Message is safe to send."
        }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_add_recent_contact - Add to recent contacts for faster lookup
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsAddRecentContactTool = new()
    {
        Name = "teams_add_recent_contact",
        Description = "Add a person, chat, or channel to the recent contacts list for faster future lookup.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new
            {
                type = new
                {
                    type = "string",
                    @enum = new[] { "Person", "Chat", "Channel", "Team" },
                    description = "The type of contact"
                },
                name = new
                {
                    type = "string",
                    description = "Display name of the contact"
                },
                id = new
                {
                    type = "string",
                    description = "The ID (userId, chatId, channelId, or teamId)"
                },
                parentId = new
                {
                    type = "string",
                    description = "Parent ID (teamId for channels)"
                },
                secondaryId = new
                {
                    type = "string",
                    description = "Secondary identifier (UPN for person, chatType for chat)"
                }
            },
            required = new[] { "type", "name", "id" }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsAddRecentContact(CallToolRequestParams request, CancellationToken ct)
    {
        var contact = new RecentContact
        {
            Type = GetStringArgOrDefault(request, "type", "Unknown"),
            Name = GetStringArgOrDefault(request, "name", ""),
            Id = GetStringArgOrDefault(request, "id", ""),
            ParentId = GetStringArg(request, "parentId"),
            SecondaryId = GetStringArg(request, "secondaryId")
        };

        _cacheService.AddRecentContact(contact);

        return ValueTask.FromResult(CreateJsonResult(new
        {
            success = true,
            message = $"Added {contact.Type} '{contact.Name}' to recent contacts"
        }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // teams_get_recent_contacts - Get recent contacts list
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Tool s_teamsGetRecentContactsTool = new()
    {
        Name = "teams_get_recent_contacts",
        Description = "Get the list of recent contacts for quick lookup.",
        InputSchema = CreateSchema(new
        {
            type = "object",
            properties = new { }
        })
    };

    private ValueTask<CallToolResult> HandleTeamsGetRecentContacts(CallToolRequestParams request, CancellationToken ct)
    {
        var contacts = _cacheService.GetRecentContacts();
        return ValueTask.FromResult(CreateJsonResult(new
        {
            count = contacts.Count,
            contacts = contacts.Select(c => new
            {
                type = c.Type,
                name = c.Name,
                id = c.Id,
                parentId = c.ParentId,
                secondaryId = c.SecondaryId,
                addedAt = c.AddedAt
            })
        }));
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
}
