# Teams Skill

This skill provides guidance for interacting with Microsoft Teams through MCP tools.
The MCP-Proxy layer handles caching, credential scanning, and pagination automatically.

## Important Behaviors

### Before Sending Any Message

1. **Always confirm with the user before sending messages**
   - Show the message content and destination
   - Ask: "Should I send this message to [recipient]?"
   - Only proceed after explicit confirmation

2. **Resolve recipients first**
   - Use `teams_resolve` to find the correct chat/channel ID
   - For ambiguous names, present options to the user
   - Never guess - ask for clarification

### Message Safety

The system automatically scans outbound messages for credentials and will block:
- API keys
- Bearer tokens
- Private keys
- Connection strings
- AWS keys
- Passwords

If a message is blocked, inform the user and help them remove the sensitive data.

### Entity Resolution Strategy

Use this escalation path when resolving people or chats:

1. **Check recent contacts** (fastest)
   - Use `teams_get_recent_contacts` for frequently used contacts

2. **Check local cache** (fast)
   - Use `teams_resolve` with the name/email
   - Cache is refreshed automatically when MCP calls are made

3. **Use external resolver** (if available)
   - If WorkIQ MCP server is connected, use `workiq_resolve_person`
   - This can resolve people not in cached chats

4. **Call MCP directly** (slowest, most accurate)
   - Use `ListChats` or `ListTeams` to refresh cache
   - Results are automatically cached for next time

### Self-Chat (Notes)

To send a message to the user's own self-chat (notes):
- Chat ID: `48:notes`
- Or use `teams_resolve` with query "self" or "notes"

### @Mentions

When mentioning users in messages:
1. Resolve the person to get their MRI (Message Resource Identifier)
2. Format: `8:orgid:{userId}`
3. Include in message body with Teams HTML format

Example:
```html
<at id="8:orgid:abc123">John Smith</at> please review this.
```

### Teams URL Handling

When a user shares a Teams URL:
1. Use `teams_parse_url` to extract IDs
2. The tool returns chatId, channelId, teamId, messageId as applicable
3. Use the IDs to perform actions (reply, lookup, etc.)

### Pagination

List operations are automatically limited to 20 items per request.
- For more results, use the `nextLink` in responses
- Don't try to override pagination limits

### Cache Management

- Cache TTL is 4 hours by default
- Use `teams_cache_status` to check freshness
- Use `teams_cache_refresh` if you know data is stale
- Cache is automatically populated from MCP responses

## Available Virtual Tools

| Tool | Purpose |
|------|---------|
| `teams_resolve` | Find person/chat/team/channel by name |
| `teams_lookup_person` | Get person by ID or UPN |
| `teams_lookup_chat` | Get chat by ID |
| `teams_lookup_team` | Get team by ID |
| `teams_lookup_channel` | Get channel by ID |
| `teams_cache_status` | View cache statistics |
| `teams_cache_refresh` | Force cache invalidation |
| `teams_parse_url` | Extract IDs from Teams URLs |
| `teams_validate_message` | Pre-check message for credentials |
| `teams_add_recent_contact` | Add to fast-lookup list |
| `teams_get_recent_contacts` | View recent contacts |

## Example Workflows

### Send Message to Person

```
User: "Send a message to John saying the meeting is confirmed"

1. teams_resolve(query="John", type="person")
   -> Found John Smith (john@contoso.com) in cache

2. teams_resolve(query="John Smith", type="chat")
   -> Found 1:1 chat with John (19:abc...@thread.v2)

3. Confirm with user:
   "I'll send this message to John Smith:
   'The meeting is confirmed.'
   Should I send this?"

4. After confirmation: SendChatMessage(chatId="19:abc...", content="The meeting is confirmed.")
```

### Handle Teams Link

```
User: "Reply to this message: https://teams.microsoft.com/l/message/..."

1. teams_parse_url(url="https://teams.microsoft.com/...")
   -> { chatId: "19:abc...", messageId: "1234" }

2. teams_lookup_chat(chatId="19:abc...")
   -> { topic: "Project Discussion", members: [...] }

3. Ask user: "What would you like to reply to this message in 'Project Discussion'?"

4. After getting reply content, confirm and send
```

### Channel Message

```
User: "Post to the General channel in Engineering team"

1. teams_resolve(query="Engineering", type="team")
   -> Found Engineering team (id: xyz123)

2. teams_resolve(query="General", type="channel")
   -> Found General channel in Engineering (19:...@thread.tacv2)

3. Confirm message and send to channel
```

## Error Handling

| Error | Action |
|-------|--------|
| "Content blocked by security policy" | Message contains credentials - help user remove them |
| "Person not found in cache" | Try MCP ListChats to refresh, or use external resolver |
| "Chat not found in cache" | Refresh cache with ListChats |
| "Ambiguous query" | Present all matches to user and ask for selection |

## Do NOT

- Send messages without user confirmation
- Guess recipient IDs
- Include credentials in messages
- Override pagination limits
- Assume cache is always fresh for critical operations
