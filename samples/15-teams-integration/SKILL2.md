---
name: teams
description: |
  Use this skill when the user wants to interact with Microsoft Teams — send messages (plain or rich), read chats or channels, manage teams/channels/members, search conversations, send @mentions, Adaptive Cards, or file attachments. Also use when the user asks to "refresh Teams cache", "list my Teams chats", "send a Teams message", "send a formatted message", "mention someone", "send a card", "attach a file in Teams", or any Teams-related operation. This skill wraps the microsoft-teams MCP with local caching for fast lookups, and adds rich messaging via a Playwright browser session.
---

# Teams Skill

Interact with Microsoft Teams: send and read messages, manage chats/channels/teams/members, and search conversations — powered by the `microsoft-teams` MCP server with a local cache layer for fast lookups. Rich messaging (HTML, @mentions, Adaptive Cards, file attachments) is handled via a supplemental Playwright browser session.


## Cache System

The cache has two tiers for fast ID resolution:

### Tier 1: Recent Contacts (`${PLUGIN_ROOT}/cache/recentcontacts.md`)

A lightweight markdown file that stores frequently-used contacts, chat IDs, and channel IDs for instant lookup — **no script execution or JSON parsing needed**. This is always checked **first**, before the JSON cache or any MCP calls.

**Format:**

```markdown
# Recent Contacts

## People
| Name | UPN | User ID |
|------|-----|---------|
| Alice Johnson | alice@contoso.com | aaaaaaaa-1111-2222-3333-444444444444 |
| Bob Smith | bob@contoso.com | bbbbbbbb-1111-2222-3333-444444444444 |

## Chats
| Person/Topic | Chat ID | Chat Type |
|--------------|---------|----------|
| Self (Notes) | 48:notes | Self |
| Alice Johnson | 19:example-chat-id_bbbbbbbb-1111-2222-3333-444444444444@unq.gbl.spaces | OneOnOne |

## Channels
| Team | Channel | Team ID | Channel ID |
|------|---------|---------|------------|
```

**Update rules:**
- **After every successful Teams operation** that resolves a person, chat, or channel, append the entry to `recentcontacts.md` if not already present
- After looking up a person via WorkIQ, add them to the People table
- After creating or finding a chat, add it to the Chats table
- After resolving a team/channel, add it to the Channels table
- Do not duplicate entries — check if the row already exists before appending
- This file persists across sessions and is never auto-cleared

### Tier 2: JSON Cache Files

All JSON cache files live in `${PLUGIN_ROOT}/cache/`:

| File | Contents | Source MCP Tool |
|------|----------|-----------------|
| `chats.json` | All chats (1:1, group, meeting) with member details | `ListChats` + `ListChatMembers` |
| `teams-and-channels.json` | All teams and their channels | `ListTeams` + `ListChannels` |
| `people.json` | Known people (UPN, display name, ID) harvested from chat/channel members | Derived from member lists |

### Cache Lifecycle

**Auto-refresh triggers:**
- First use in a session when cache files don't exist
- Cache file is older than 4 hours (check `lastRefreshed` timestamp in each file)
- User explicitly asks to refresh (`"refresh Teams cache"`, `"update Teams data"`)

**On-demand refresh:**
- `refresh all` — Rebuild all three cache files
- `refresh chats` — Rebuild only chats.json and update people.json
- `refresh teams` — Rebuild only teams-and-channels.json and update people.json
- `refresh people` — Rebuild people.json from existing chat/team member data

**Cache file format:**
```json
{
  "lastRefreshed": "2026-03-01T18:00:00Z",
  "data": [ ... ]
}
```

**Chats cache entry format** (each entry in `data` array):
```json
{
  "id": "19:example-chat-id...@unq.gbl.spaces",
  "topic": "",
  "chatType": "OneOnOne",
  "members": [
    { "displayName": "Alice Johnson", "upn": "alice@contoso.com", "userId": "aaaaaaaa-..." },
    { "displayName": "Bob Smith", "upn": "bob@contoso.com", "userId": "bbbbbbbb-..." }
  ]
}
```

**CRITICAL: Every chat entry MUST include a `members` array** with `displayName`, `upn`, and `userId` for each participant. Without members, `lookup-chat` cannot resolve person → chat from cache, forcing expensive MCP calls. OneOnOne chats have empty `topic` fields — the only way to identify the counterparty is via the `members` array.

### Chat ID Formats

| Chat Type | ID Format | Example |
|-----------|-----------|---------|
| **Self (Notes)** | `48:notes` | `48:notes` — hardcoded, single-member, "Notes to Self" |
| **OneOnOne** | `19:<guid1>_<guid2>@unq.gbl.spaces` | `19:aaaaaaaa-..._bbbbbbbb-...@unq.gbl.spaces` |
| **Group** | `19:<hex>@thread.v2` | `19:225f78ef8c4441f1...@thread.v2` |
| **Meeting** | `19:meeting_<base64>@thread.v2` | `19:meeting_ODg2NzE1...@thread.v2` |
| **Channel** | `19:<id>@thread.tacv2` | `19:9b88c8c8f95e...@thread.tacv2` |

The self-chat is a special case: it uses the fixed ID `48:notes`, has only one member (the signed-in user), and is used for personal notes. The cache script auto-detects it by ID and labels it as `Self (Notes)` with chat type `Self`. The `lookup-chat` command handles queries like `"self"`, `"notes"`, or `"self-chat"` and returns `48:notes` directly.

### Cache Refresh Workflow

Use the Python cache manager script for all cache operations:

```bash
python "${PLUGIN_ROOT}/scripts/cache-manager.py" <command>
```

Commands:
- `status` — Show cache file ages, staleness, and recent contacts counts
- `read chats` — Print cached chats as JSON
- `read teams` — Print cached teams/channels as JSON
- `read people` — Print cached people as JSON
- `write chats <json>` — Write chats data (piped via stdin)
- `write teams <json>` — Write teams/channels data (piped via stdin)
- `write people <json>` — Write people data (piped via stdin)
- `build-teams` — Build teams cache from raw API responses (pipe JSON via stdin, see below)
- `build-chats` — Build chats cache from raw API responses (pipe JSON via stdin, see below)
- `is-stale chats|teams|people` — Exit 0 if stale (>4h), exit 1 if fresh
- `read-contacts` — Print recentcontacts.md as structured JSON
- `add-person <name> <upn> <user_id>` — Add/update a person in recentcontacts.md (deduped by UPN)
- `add-chat <person_or_topic> <chat_id> <chat_type>` — Add/update a chat in recentcontacts.md (deduped by chat ID)
- `add-channel <team> <channel> <team_id> <channel_id>` — Add/update a channel in recentcontacts.md (deduped by channel ID)
- `lookup-person <name>` — Search people cache by display name or UPN substring
- `lookup-chat <query>` — Find a chat by member UPN, display name, topic, or special keywords (`self`, `notes`, `self-chat` → returns `48:notes`)
- `lookup-team <name>` — Find a team by display name substring
- `lookup-channel <team_id> <name>` — Find a channel by name within a specific team

#### Refreshing Chats Cache

Fetch the most recent/active chats using **pagination** and get members for each.

1. Call `microsoft-teams-ListChats` with `userUpns: []` and **`top: 20`** to get the first page of chats
   - **ALWAYS use `top` parameter** — unfiltered ListChats without pagination WILL time out for users with many chats
   - If you need more, make subsequent paginated calls (the response includes a `nextLink` for the next page)
   - Collect up to 50 chats total across pages (2-3 pages of 20)
   - **If any call times out**, reduce `top` to `10` and retry
2. For each collected chat, call `microsoft-teams-ListChatMembers` to get members
   - Process members in batches of 10 chats at a time to avoid overwhelming the API
   - If a member call times out, skip that chat and continue with the next
3. **Build the input JSON** for the cache manager. Construct a single JSON object with two keys:
   - `chats`: the raw array of chat items from `ListChats` response
   - `members`: an object mapping each `chatId` → raw array of member items from `ListChatMembers`
   ```json
   {
     "chats": [
       {"id": "19:...", "topic": "Project Chat", "chatType": "Group", ...},
       {"id": "19:...", "topic": "", "chatType": "OneOnOne", ...}
     ],
     "members": {
       "19:...first_chat_id...": [
         {"displayName": "Alice", "email": "alice@contoso.com", "userId": "aaa-..."},
         {"displayName": "Bob", "email": "bob@contoso.com", "userId": "bbb-..."}
       ],
       "19:...second_chat_id...": [
         {"displayName": "Alice", "email": "alice@contoso.com", "userId": "aaa-..."},
         {"displayName": "Bob Smith", "email": "bob@contoso.com", "userId": "bbbbbbbb-..."}
       ]
     }
   }
   ```
4. **Save to a temp file and pipe to cache manager:**
   ```python
   # Write the JSON to a temp file, then pipe it
   python -c "
   import json, subprocess
   data = {'chats': chats_list, 'members': members_dict}
   proc = subprocess.Popen(['python', 'cache-manager.py', 'build-chats'],
                           stdin=subprocess.PIPE, text=True)
   proc.communicate(json.dumps(data))
   "
   ```
   The `build-chats` command automatically:
   - Merges members into each chat entry
   - Writes the chats cache
   - Derives the people cache from all unique members
   - Populates recentcontacts.md with all chats and people

#### Targeted Chat Lookup (Preferred for Single-Person Queries)

When the user wants to find or message a **specific person**, use the `resolve` command first:

```powershell
python scripts/cache-manager.py resolve "<person name>"
```

**If `found: true`** — the response includes `person` (UPN/userId) and `chat` (chatId/chatType) if available. Proceed directly to Phase 3.

**If `found: false`** — the person is not in any local cache tier. Use this escalation path:

1. **WorkIQ (FAST — ~2-3 seconds)** — `workiq-ask_work_iq` with `"What is <person name>'s email/UPN?"`. **This is faster than any ListChats call.**
2. **Filtered ListChats** — `microsoft-teams-ListChats` with `userUpns: ["resolved-upn"]` and `top: 20` → find the chat ID.
3. **Update cache** — `python cache-manager.py add-person ...` and `add-chat ...` so `resolve` finds them next time.

**NEVER call ListChats just to find a person's UPN** — that's what WorkIQ and the `resolve` command are for.

> **Key insight:** The people cache (`people.json`) contains everyone who has ever appeared as a member of ANY cached chat — including group chats and meeting chats with dozens of members. A person may not have a 1:1 chat but they're likely in at least one group chat. The `lookup-person` command searches across all of these.

#### Refreshing Teams & Channels Cache

1. Call `workiq-ask_work_iq` with `"What is my user ID (GUID)?"` to get the current user's ID
   - If WorkIQ can't return it, check `recentcontacts.md` People table for the current user's ID
2. Call `microsoft-teams-ListTeams` with the user's ID to get all teams
3. For each team, call `microsoft-teams-ListChannels` to get channels (can be done in parallel)
   - If a `ListChannels` call times out, skip that team and continue with the next
4. **Build the input JSON** for the cache manager. Construct a single JSON object with two keys:
   - `teams`: the raw array of team items from `ListTeams` response
   - `channels`: an object mapping each `teamId` → raw array of channel items from `ListChannels`
   ```json
   {
     "teams": [
       {"id": "514d91b7-...", "displayName": "Project Alpha", ...},
       {"id": "ddd9500b-...", "displayName": "Engineering Team", ...}
     ],
     "channels": {
       "514d91b7-...": [
         {"id": "19:9b88c8c8...@thread.tacv2", "displayName": "General", ...},
         {"id": "19:6d33f17e...@thread.tacv2", "displayName": "Program A Integration", ...}
       ],
       "ddd9500b-...": [
         {"id": "19:KS6nGUz7...@thread.tacv2", "displayName": "Main Street", ...}
       ]
     }
   }
   ```
5. **Save to a temp file and pipe to cache manager:**
   ```python
   python -c "
   import json, subprocess
   data = {'teams': teams_list, 'channels': channels_dict}
   proc = subprocess.Popen(['python', 'cache-manager.py', 'build-teams'],
                           stdin=subprocess.PIPE, text=True)
   proc.communicate(json.dumps(data))
   "
   ```
   The `build-teams` command automatically:
   - Transforms raw API data into the cache format (`[{id, displayName, channels: [{id, displayName}]}]`)
   - Writes the teams cache
   - Populates recentcontacts.md with all team/channel entries

#### Refreshing People Cache

People are harvested from chat and channel member lists during chats/teams refresh. The people cache consolidates all known users with:
- `displayName` — Full name
- `upn` — User Principal Name (email)
- `userId` — Azure AD GUID

## Capabilities Reference

### Messaging

**Send priority order:** `scripts.api` CLI commands (fastest, reliable, built-in markdown conversion + validation) → MCP PostMessage with `contentType: 'html'` (formatted, no scripts) → MCP PostMessage plain text → Playwright send_message.py (fallback for @mentions, cards, file attachments)

| Action | Tool | Required Context | Notes |
|--------|------|------------------|-------|
| **Send rich message to chat** | **`python -m scripts.api send-rich --chat ID --body-file body.md`** | chatId | **Preferred.** Markdown → HTML → validate → send via chatsvc. No browser needed. |
| **Send rich message to channel** | **`python -m scripts.api send-rich --team ID --channel ID --body-file body.md`** | teamId, channelId | Posts via Graph API. No browser needed. |
| **Reply to channel thread** | **`python -m scripts.api reply-channel --team ID --channel ID --message MSG_ID --body-file body.md --markdown`** | teamId, channelId, messageId | Via Graph API. Also available in `send-rich --message`. |
| **Post to channel** | **`python -m scripts.api post-channel --team ID --channel ID --body-file body.md --markdown`** | teamId, channelId | Via Graph API. |
| **Send short message to chat** | **`python -m scripts.api send-message --chat ID --body "text" --markdown`** | chatId | Via chatsvc. `--markdown` converts to Teams HTML. |
| **Send formatted message to chat** | `microsoft-teams-PostMessage` with `contentType: 'html'` | chatId, HTML content | MCP. No scripts needed — convert markdown → HTML first (see below). |
| **Send formatted message to channel** | `microsoft-teams-PostChannelMessage` with `contentType: 'html'` | teamId, channelId, HTML content | MCP. Same HTML support as chat. |
| Send plain text to a chat | `microsoft-teams-PostMessage` | chatId, content | MCP fallback for simple messages |
| Send plain text to a channel | `microsoft-teams-PostChannelMessage` | teamId, channelId, content | MCP fallback for simple messages |
| **Send @mention** | **`send_message.py --mention-name --mention-mri`** | MRI from cache | Playwright. Works for both chats and channels |
| **Send Adaptive Card** | **`send_message.py --card <template> --card-data`** | chatId or channelId | Playwright fallback |
| **Attach local file** | **`send_message.py --attach <path>`** | chatId or channelId | Playwright fallback — 4-step SPO+AMS protocol |
| **Attach SharePoint/OneDrive file** | **`send_message.py --attach <url>`** | chatId or channelId | Playwright fallback — reference only |
| **Important/urgent message** | **`send_message.py --importance high\|urgent`** | chatId or channelId | Playwright fallback |
| Reply to a channel message (plain) | `microsoft-teams-ReplyToChannelMessage` | teamId, channelId, messageId, content | MCP fallback, plain text only |
| Read chat messages | `microsoft-teams-ListChatMessages` | chatId | **$top**, **$filter**, **$orderby** |
| Read channel messages | `microsoft-teams-ListChannelMessages` | teamId, channelId | **$top**, **$expand** |
| Get a specific chat message | `microsoft-teams-GetChatMessage` | chatId, messageId | |
| Update a chat message | `microsoft-teams-UpdateChatMessage` | chatId, messageId, content | |
| Delete a chat message | `microsoft-teams-DeleteChatMessage` | chatId, messageId | |
| Search messages | `microsoft-teams-SearchTeamsMessages` | natural language query | |

### Sending Long Rich Messages (File-Based Pattern)

When the message body is too long for inline shell strings (>2K chars — common for announcements, formatted reports, tables), use the **`send-rich` command**. This handles markdown conversion, emoji/link validation, and sending in a single CLI call.

**Send to a chat:**
```powershell
cd skills/teams

# Write your markdown body to a file (with emoji shortcodes, tables, etc.)
# Then send in one command:
python -m scripts.api send-rich --chat "<CHAT_ID>" --body-file body.md
```

**Post to a channel:**
```powershell
python -m scripts.api send-rich --team "<TEAM_GUID>" --channel "<CHANNEL_ID>" --body-file body.md
```

**Reply to a channel thread:**
```powershell
python -m scripts.api send-rich --team "<TEAM_GUID>" --channel "<CHANNEL_ID>" --message "<PARENT_MSG_ID>" --body-file body.md
```

**What `send-rich` does automatically:**
1. Reads markdown from `--body-file` (or `--body` for short text)
2. Converts to Teams HTML via `markdown_to_teams_html()` (emojis, tables, headings, bold/italic, links)
3. Validates all emoji CDN URLs and hyperlinks (skip with `--skip-validate`)
4. Sends via chatsvc (chats) or Graph API (channels) — no Playwright needed

**Lower-level alternatives** (when you need more control):

```powershell
# Send with inline markdown conversion (short messages)
python -m scripts.api send-message --chat "<CHAT_ID>" --body ":rocket: **Hello**" --markdown

# Post to channel with markdown conversion
python -m scripts.api post-channel --team "<TEAM_GUID>" --channel "<CHANNEL_ID>" --body-file body.md --markdown

# Reply to channel thread with markdown conversion
python -m scripts.api reply-channel --team "<TEAM_GUID>" --channel "<CHANNEL_ID>" --message "<MSG_ID>" --body-file body.md --markdown
```

**When to use which:**
- `send-rich` — **Default choice.** Handles validation, conversion, and sending in one step. Use for announcements, reports, any markdown content.
- `send-message --markdown` — Short chat messages where you can inline the body.
- `post-channel --markdown` / `reply-channel --markdown` — When you need separate channel post/reply with explicit control.
- `send_message.py` (Playwright) — **Only** when you need @mentions, Adaptive Cards, or file attachments.

### Chats

| Action | MCP Tool | Required Context | Optional Params |
|--------|----------|------------------|-----------------|
| List chats | `microsoft-teams-ListChats` | userUpns (array) | **topic**, **$top** |
| Get chat details | `microsoft-teams-GetChat` | chatId | |
| Create 1:1 chat | `microsoft-teams-CreateChat` | chatType: "oneOnOne", member UPNs | |
| Create group chat | `microsoft-teams-CreateChat` | chatType: "group", topic, member UPNs | |
| Update chat topic | `microsoft-teams-UpdateChat` | chatId, topic | |
| Delete chat | `microsoft-teams-DeleteChat` | chatId | |

### Teams & Channels

| Action | MCP Tool | Required Context | Optional Params |
|--------|----------|------------------|-----------------|
| List teams | `microsoft-teams-ListTeams` | userId (GUID) | |
| Get team details | `microsoft-teams-GetTeam` | teamId (from ListTeams) | **$select**, **$expand** |
| List channels | `microsoft-teams-ListChannels` | teamId (from ListTeams) | **$select**, **$filter** |
| Get channel details | `microsoft-teams-GetChannel` | teamId, channelId | **$select**, **$filter** |
| Create channel | `microsoft-teams-CreateChannel` | teamId (from ListTeams), displayName | description |
| Create private channel | `microsoft-teams-CreatePrivateChannel` | teamId, displayName | description |
| Update channel | `microsoft-teams-UpdateChannel` | teamId, channelId | displayName, description |

### Members

| Action | MCP Tool | Required Context | Optional Params |
|--------|----------|------------------|-----------------|
| List chat members | `microsoft-teams-ListChatMembers` | chatId | |
| List channel members | `microsoft-teams-ListChannelMembers` | teamId, channelId | **$top**, **$expand** |
| Add member to chat | `microsoft-teams-AddChatMember` | chatId, user reference, roles | |
| Add member to channel | `microsoft-teams-AddChannelMember` | teamId, channelId, userId (GUID) | |
| Update channel member role | `microsoft-teams-UpdateChannelMember` | teamId, channelId, membershipId, role | |

## Query Optimization

### Avoiding Timeouts with ListChats

`ListChats` is the most expensive call — it **will** time out when the user has hundreds of chats. **Always use pagination and prefer targeted queries over full unfiltered calls.**

**CRITICAL: Always use `top` parameter with ListChats:**
```
# GOOD: Paginated — fast, predictable response time
microsoft-teams-ListChats with userUpns: [], top: 20

# BAD: Unpaginated — fetches ALL chats, WILL time out
microsoft-teams-ListChats with userUpns: []
```

**Priority order for finding a chat:**

1. **recentcontacts.md** — Instant lookup, no MCP call needed
2. **JSON cache** — Check `chats.json` if fresh (< 4 hours old)
3. **WorkIQ** — For person UPN resolution (`"What is <name>'s email?"`) — **~2-3 seconds**, much faster than ListChats
4. **Filtered ListChats with pagination** — Use `userUpns` or `topic` AND `top: 20` to narrow results
5. **Unfiltered ListChats with pagination** — Last resort only (cache refresh), always `top: 20`

**When looking for a specific person's chat:**

```
# BEST: Use WorkIQ to get UPN first, then check cache
workiq-ask_work_iq: "What is Alice's email/UPN?"  → alice@contoso.com (2-3 seconds)
python cache-manager.py lookup-chat alice@contoso.com  → chat ID from cache (instant)

# GOOD: Filter by UPN with pagination — if cache misses
microsoft-teams-ListChats with userUpns: ["alice@contoso.com"], top: 20

# BAD: Fetch all chats to find a person — slow, will timeout
microsoft-teams-ListChats with userUpns: []
```

**When looking for a group chat by topic:**

```
# GOOD: Filter by topic with pagination — returns only matching chats
microsoft-teams-ListChats with topic: "Project Standup", top: 20

# BAD: Fetch all and search — slow, unnecessary
microsoft-teams-ListChats with userUpns: []
```

### OData Query Parameters

These parameters are supported by the Microsoft Teams MCP and map to Microsoft Graph OData query options:

| Parameter | Tool Support | Description | Examples |
|-----------|-------------|-------------|----------|
| **$top** | ListChats, ListChatMessages, ListChannelMessages, ListChannelMembers | Limit number of results returned | `top: 20` — return at most 20 items. **ALWAYS use with ListChats** |
| **$filter** | ListChatMessages, ListChannels, GetChannel | OData filter expression | `filter: "createdDateTime ge 2026-03-01T00:00:00Z"` |
| **$orderby** | ListChatMessages | Sort expression | `orderby: "createdDateTime desc"` |
| **$expand** | ListChannelMessages, ListChannelMembers, GetTeam | Include related entities | `expand: "replies"` — include message replies |
| **$select** | ListChannels, GetTeam, GetChannel | Return only specific fields | `select: "displayName,id"` |
| **topic** | ListChats | Filter chats by topic name | `topic: "Weekly Standup"` |
| **userUpns** | ListChats | Filter chats by member UPN(s) | `userUpns: ["user@contoso.com"]` |

### Common Filter Patterns

**Messages from a date range:**
```
microsoft-teams-ListChatMessages with chatId, filter: "createdDateTime ge 2026-03-01T00:00:00Z"
```

**Recent messages only (limit count):**
```
microsoft-teams-ListChatMessages with chatId, top: 10, orderby: "createdDateTime desc"
```

**Channel messages with replies expanded:**
```
microsoft-teams-ListChannelMessages with teamId, channelId, top: 20, expand: "replies"
```

**Filter channels by creation date:**
```
microsoft-teams-ListChannels with teamId, filter: "createdDateTime ge 2026-01-01T00:00:00Z"
```

## Workflow

### Phase 0: URL-Provided Chat Shortcut

**When the user provides a Teams chat or channel URL**, skip the normal resolve flow entirely. Extract the chat ID from the URL and use the direct API to get participants — this is faster than both `resolve` and WorkIQ.

**Extracting the chat ID from a Teams URL:**
- Chat URL: `https://teams.microsoft.com/l/chat/19:abc...@thread.v2/conversations?context=...` → chat ID is `19:abc...@thread.v2`
- 1:1 URL: `https://teams.microsoft.com/l/chat/19:guid1_guid2@unq.gbl.spaces/conversations?context=...` → chat ID is `19:guid1_guid2@unq.gbl.spaces`
- Channel URL: `https://teams.microsoft.com/l/channel/19%3Aabc...@thread.tacv2/...?groupId=GUID` → URL-decode the channel ID, extract groupId as teamId

**Getting participant details (use direct API, not MCP — MCP has auth issues):**
```powershell
cd skills/teams
python -m scripts.api get-members --chat "<extracted-chat-id>"
```

This returns member names, UPNs, and user IDs — everything needed to tailor messages, send @mentions, or cache the participants. **No WorkIQ lookup needed.**

**After getting members, cache them all:**
```powershell
python scripts/cache-manager.py add-chat "<topic or person>" "<chat-id>" "<Group|OneOnOne>"
python scripts/cache-manager.py add-person "<Name>" "<upn>" "<userId>"   # for each member
```

**When to use Phase 0 vs Phase 1:**
- User provides a Teams URL → **Phase 0** (extract ID, get members, cache, proceed to Phase 3)
- User references a person or chat by name → **Phase 1** (resolve from cache)

### Phase 1: Resolve via Script → Execute Immediately

Before any MCP calls, run the **`resolve`** command to search both recentcontacts.md AND JSON caches in one call:

```powershell
cd skills/teams
python scripts/cache-manager.py resolve "<person name, chat topic, team name, or channel name>"
```

**Returns JSON:**
```json
{
  "found": true,
  "tier": "contacts",
  "person": { "name": "Alice Johnson", "upn": "alice@contoso.com", "userId": "aaaaaaaa-..." },
  "chat": { "chatId": "19:88d97a83-...@unq.gbl.spaces", "chatType": "OneOnOne", "personOrTopic": "Alice Johnson" },
  "channel": null,
  "allMatches": { "people": [...], "chats": [...], "channels": [...] }
}
```

**Decision tree:**
- If `found: true` → use the returned IDs and **execute the operation immediately** (Phase 3). Skip Phase 2.
- If `found: false` → proceed to Phase 2 (external lookups).
- Check `tier` field: `"contacts"` = recentcontacts.md hit, `"cache"` = JSON cache hit, `"builtin"` = well-known (e.g., self-chat).
- `allMatches` lists ALL matches if the query is ambiguous (e.g., "General" matches many channels).

**Examples:**
```powershell
# Find a person — returns UPN, userId, and any chat containing them
python scripts/cache-manager.py resolve "Alice"

# Find a chat by topic
python scripts/cache-manager.py resolve "Weekly Standup"

# Find a channel
python scripts/cache-manager.py resolve "General"

# Self-chat (built-in)
python scripts/cache-manager.py resolve "self"
```

### Phase 2: External Lookups (only if resolve found nothing)

Resolve the specific IDs needed — **do not run a full cache bootstrap**. The goal is to execute the user's request as fast as possible.

**Person resolution priority (fastest first):**

1. **WorkIQ** → `workiq-ask_work_iq` with `"What is <name>'s email?"` — **~2-3 seconds**. Use this as the FIRST external call.
2. **Filtered ListChats** → `microsoft-teams-ListChats` with `userUpns: ["resolved-upn"]` and `top: 20` — only for finding a chat ID.
3. **ListTeams + ListChannels** → for team/channel resolution when cache misses.

**After resolving externally, update the cache** so `resolve` will find it next time:
```powershell
python scripts/cache-manager.py add-person "Name" "upn@contoso.com" "user-guid"
python scripts/cache-manager.py add-chat "Name or Topic" "19:...@unq.gbl.spaces" "OneOnOne"
python scripts/cache-manager.py add-channel "Team Name" "Channel Name" "team-guid" "19:...@thread.tacv2"
```

**Additional cache-manager commands (for advanced queries):**
- `python cache-manager.py lookup-chat <upn>` — searches `members` array in cached chats (deeper than resolve)
- `python cache-manager.py lookup-person <name>` — searches people JSON cache
- `python cache-manager.py lookup-team <name>` — searches teams JSON cache
- `python cache-manager.py lookup-channel <team_id> <name>` — searches channels within a specific team
- `python cache-manager.py read chats` — dump all cached chats as JSON
- `python cache-manager.py read teams` — dump all cached teams/channels
- `python cache-manager.py is-stale chats` — exit 0 if stale, 1 if fresh

Once IDs are resolved, proceed to Phase 3.

### Phase 3: Execute Operation

Call the appropriate MCP tool with resolved IDs. Follow MCP tool chain requirements:

**CRITICAL tool chain rules (enforced by MCP server):**
- `ListTeams` **must** be called before `GetTeam`, `ListChannels`, `CreateChannel`, `CreatePrivateChannel`, `UpdateChannel` — teamId must come from ListTeams output
- `ListChats` **must** be called before operations needing chatId (unless chatId was resolved via `resolve` command or `add-chat`)
- Never fabricate or guess IDs — always resolve via `cache-manager.py resolve`, cache lookups, or MCP calls
- User UPNs must come from lookup tools, never guessed

### Phase 4: Update Recent Contacts

**After every successful operation**, update the cache with any newly resolved IDs so `resolve` finds them next time:

- **Person resolved** → `python cache-manager.py add-person <name> <upn> <user_id>`
- **Chat found or created** → `python cache-manager.py add-chat <person_or_topic> <chat_id> <chat_type>`
- **Channel resolved** → `python cache-manager.py add-channel <team> <channel> <team_id> <channel_id>`

If `recentcontacts.md` doesn't exist yet, create it with the markdown header and table structure.

### Phase 5: Backfill Cache (Post-Operation)

**After the user's operation is complete and results reported**, check whether the JSON cache needs populating. This is a background housekeeping step — do it silently after the main task is done.

1. Run `python "${PLUGIN_ROOT}/scripts/cache-manager.py" status`
2. If all caches are fresh, skip — nothing to do
3. If any cache is missing or stale, run the bootstrap sequence below
4. If user explicitly asked to refresh, always refresh regardless of staleness

#### Bootstrap Sequence (first use or stale cache)

Run chats and teams refresh **in sequence** (not parallel) to avoid API rate limits:

**Step A — Teams & Channels** (fast, do first):
1. Resolve current user's ID via `workiq-ask_work_iq` → `"What is my user ID (GUID)?"` (or from recentcontacts.md)
2. `microsoft-teams-ListTeams` with user ID → save response items to a `teams` list
3. For each team → `microsoft-teams-ListChannels` → save response items to a `channels` dict keyed by team ID
4. Pipe to cache manager: `python cache-manager.py build-teams` (handles cache write + recentcontacts)

**Step B — Chats** (expensive, paginated):
1. `microsoft-teams-ListChats` with `userUpns: []`, **`top: 20`** — fetch the first page of chats
2. Make up to 2 more paginated calls (`top: 20` each) to collect ~40-60 chats total
3. **If any call times out**, reduce `top` to `10` and retry
4. For each chat → `microsoft-teams-ListChatMembers` (batch 10 at a time) → save response items to a `members` dict keyed by chat ID
5. Pipe to cache manager: `python cache-manager.py build-chats` (handles cache write + people + recentcontacts)

**Step C — People** (derived, automatic):
People are automatically derived by `build-chats` — no separate step needed. The `build-chats` command extracts all unique people from chat member lists and writes `people.json` + populates recentcontacts.md.

### Phase 6: Report Results

- For **send** operations: Confirm message was sent, show recipient and timestamp
- For **read** operations: Format messages clearly with sender, timestamp, and content
- For **list** operations: Present as a clean table or list
- For **search** operations: Summarize results with links to source messages

## Rich Messaging (Playwright Browser Session)

Rich messaging adds HTML formatting (headings, bold, italic, lists, tables), @mentions, Adaptive Cards, file attachments, and message importance/subject — capabilities not available through the MCP server. Works for both **chats and channels**.

### Architecture

Rich messaging uses a **Playwright browser session** that authenticates via Edge persistent profile (same session as a real user). The browser runs headless (falls back to headed for MFA). Auth tokens are captured from Teams network requests and cached per service endpoint.

| Component | Path | Purpose |
|-----------|------|---------|
| `auth.py` | `scripts/rich/auth.py` | `TeamsSession` class — Playwright browser launch, token capture |
| `api_client.py` | `scripts/rich/api_client.py` | `send_message()`, `attach_and_send()`, `attach_existing_and_send()` |
| `send_message.py` | `scripts/rich/send_message.py` | CLI entry point — `--to`, `--channel`, `--url` destinations |
| `validate_message.py` | `scripts/rich/validate_message.py` | Pre-send validator — checks emoji CDN images and hyperlinks resolve |
| `utils.py` | `scripts/rich/utils.py` | Markdown→HTML (headings, bold, lists, tables, emojis), mention builders, card builders, message body builder |
| Templates | `templates/*.json` | Adaptive Card JSON templates |

**Browser profile:** `~/.teams-agent/browser-profile/` (persistent across sessions, shared with meeting-summary and confluence skills)
**Auth method:** CDP — launches Edge subprocess on port 9223, connects via connect_over_cdp. Works with Edge open.

### When to Use scripts.api vs MCP HTML vs MCP Plain vs Playwright

| Need | Use | Tool |
|------|-----|------|
| Long/complex formatted message (>2K chars, reports, tables) | **scripts.api** (full pipeline) | `send-rich --body-file body.md` |
| Short formatted message (bold, lists, links, headings) | **MCP HTML** (fast, no scripts) | `PostMessage` with `contentType: 'html'` |
| Simple text message | **MCP plain** (fastest) | `PostMessage` / `PostChannelMessage` |
| @mention a person | **Playwright** (required) | `send_message.py --mention-name --mention-mri` |
| Adaptive Card | **Playwright** (required) | `send_message.py --card <template>` |
| Attach local file | **Playwright** (required) | `send_message.py --attach <path>` |
| Attach SPO/OneDrive URL | **Playwright** (required) | `send_message.py --attach <url>` |
| Important/urgent message | **Playwright** (required) | `send_message.py --importance high` |
| Message with subject line | **Playwright** (required) | `send_message.py --subject "Topic"` |
| Read/search/list messages | **MCP** (always) | `ListChatMessages`, `SearchTeamsMessages`, etc. |
| Chat/team/channel management | **MCP** (always) | `ListChats`, `CreateChat`, etc. |

### Markdown → HTML for MCP Messages

When sending formatted messages via MCP `PostMessage` / `PostChannelMessage` with `contentType: 'html'`, convert markdown to Teams-compatible HTML. Teams supports a subset of HTML:

**Supported HTML tags:**
- `<b>`, `<strong>` — bold
- `<i>`, `<em>` — italic
- `<a href="url">text</a>` — links
- `<ul>`, `<ol>`, `<li>` — lists
- `<h1>` through `<h3>` — headings
- `<table>`, `<tr>`, `<th>`, `<td>` — tables
- `<pre><code>` — code blocks
- `<br>` — line breaks
- `<p>` — paragraphs

**Conversion rules:**
```
**bold**        → <b>bold</b>
*italic*        → <i>italic</i>
[text](url)     → <a href="url">text</a>
- item          → <ul><li>item</li></ul>
1. item         → <ol><li>item</li></ol>
## Heading      → <h2>Heading</h2>
`code`          → <code>code</code>
```code```      → <pre><code>code</code></pre>
line1\nline2    → line1<br>line2
```

**Example — sending a formatted MCP message:**
```
PostMessage:
  chatId: "19:abc...@thread.v2"
  content: "<b>Status Update</b><br><br><ul><li>Feature A — complete</li><li>Feature B — in progress</li></ul>"
  contentType: "html"
```

> **Note:** For long messages (>2K chars), complex tables, or messages requiring validation/credential scanning, prefer `scripts.api send-rich` which handles the full pipeline automatically.

### Emoji Shortcodes

The `markdown_to_teams_html()` converter in `utils.py` automatically replaces `:shortcode:` (GitHub/Slack style) and `(shortcode)` (Teams/Skype native) with the proper Teams emoji HTML. Unknown shortcodes are left as-is.

> **Full catalog:** See [TEAMS-EMOJIS.md](TEAMS-EMOJIS.md) for all 691 Teams emoticon IDs and their mappings.

**Frequently-used shortcodes:**

| Category | Shortcodes |
|----------|-----------|
| **Status** | `:rocket:` 🚀  `:check:` ✅  `:x:` ❌  `:warning:` ⚠️  `:construction:` 🚧  `:star:` ⭐  `:stop:` 🛑 |
| **Reactions** | `:thumbsup:` 👍  `:thumbsdown:` 👎  `:clap:` 👏  `:pray:` 🙏  `:muscle:` 💪  `:heart:` ❤️  `:tada:` 🎉 |
| **Celebration** | `:champagne:` 🍾  `:trophy:` 🏆  `:medal:` 🥇  `:sparkles:` ✨  `:cake:` 🎂  `:balloon:` 🎈 |
| **Objects** | `:bulb:` 💡  `:lock:` 🔒  `:key:` 🔑  `:link:` 🔗  `:gear:` ⚙️  `:computer:` 💻  `:target:` 🎯  `:brain:` 🧠 |
| **Docs** | `:memo:` 📝  `:clipboard:` 📋  `:chart:` 📊  `:calendar:` 📅  `:email:` 💌  `:book:` 📓  `:mag:` 🔍 |
| **People** | `:wave:` 👋  `:cool:` 😎  `:thinking:` 🤔  `:shrug:` 🤷  `:facepalm:` 🤦  `:salute:` 🫡  `:robot:` 🤖 |
| **Nature** | `:coffee:` ☕  `:sun:` ☀️  `:rainbow:` 🌈  `:zap:` ⚡  `:bee:` 🐝  `:unicorn:` 🦄  `:globe:` 🌍 |

**Example:**
```
--body ":rocket: **Launch update** :check: All tests passing"
```
Renders as: 🚀 **Launch update** ✅ All tests passing — with proper animated Teams emojis.

### Table Formatting

Markdown tables are automatically converted to Teams-compatible HTML tables. Use standard pipe syntax:

```markdown
| Feature | Status |
|---------|--------|
| Emojis  | Done   |
| Tables  | Done   |
```

This renders as a proper Teams table with bold headers. The HTML format uses `<figure class="table"><table class="copy-paste-table">` (matching the native Teams web client format).

**Notes:**
- The header row (above the `|---|---|` separator) is automatically wrapped in `<strong>` tags
- Tables can contain inline formatting (bold, links, emojis) within cells
- No `<thead>` is used — Teams uses bold `<td>` cells in the first row(s) instead

### Pre-Send Validation

**ALWAYS run the validator before sending rich messages.** The `validate_message.py` script checks that all emoji CDN images and hyperlinks in the generated HTML resolve to valid endpoints. This catches broken emojis (invalid Teams IDs) and dead links before they reach the recipient.

```powershell
cd skills/teams

# Validate a markdown body (converts to HTML, then checks all emojis + links)
python -m scripts.rich.validate_message --body ":rocket: Visit https://github.com/org/repo"

# Validate raw HTML directly
python -m scripts.rich.validate_message --html "<p><a href='https://example.com'>link</a></p>"

# Audit the entire EMOJI_MAP against the CDN (periodic health check)
python -m scripts.rich.validate_message --audit-map
```

**Output format (JSON):**
```json
{
  "ok": true,
  "emoji_count": 3,
  "link_count": 2,
  "issues": []
}
```

When issues are found, `ok` is `false` and each issue includes `kind` (emoji/link), `url`, `identifier`, and HTTP `status`:
```json
{
  "ok": false,
  "emoji_count": 1,
  "link_count": 0,
  "issues": [
    {"kind": "emoji", "url": "https://...cdn.../badid/default/20_f.png", "identifier": "badid", "status": 404}
  ]
}
```

**Exit codes:** `0` = all valid, `1` = one or more broken items.

**Workflow:** Before every `send_message.py` call with `--body`, run the validator first:
1. `python -m scripts.rich.validate_message --body "<same body text>"` → check exit code
2. If exit code 0 → proceed with `python -m scripts.rich.send_message --to ... --body ...`
3. If exit code 1 → fix broken emoji shortcodes or URLs, then re-validate

### Credential Guard (Outbound Security)

**ALL outbound messages are scanned for credentials before sending.** The `credential_scanner.py` module detects API keys, JWT tokens, connection strings, SAS tokens, passwords, private keys, and other secrets. If credentials are found, the send is **blocked**, the incident is logged, and the owner is notified.

**Automatic enforcement (rich messaging):** The credential guard is integrated directly into `send_message.py`. Any message containing detected credentials will be blocked with exit code 2. No manual step needed — it runs automatically.

**Manual enforcement (MCP plain text):** Before calling `PostMessage` or `PostChannelMessage`, scan the content:

```powershell
cd skills/teams

# Scan message text for credentials
python -m scripts.rich.credential_scanner --text "<message content>"

# Scan from a file
python -m scripts.rich.credential_scanner --file draft.txt
```

**Exit codes:** `0` = clean, `1` = credentials detected (prints findings), `2` = usage error.

**Detected patterns:**
- Private keys (PEM headers)
- JWT tokens (`eyJ...`)
- Azure SAS tokens, Storage keys
- AWS access keys, secret keys
- GitHub tokens (`ghp_`, `gho_`, etc.)
- Azure AD client secrets
- Generic API keys, Bearer tokens
- Database connection strings with passwords
- Password/secret assignments
- Well-known environment variable secrets

**On detection:**
- Send is blocked (exit code 2 for rich, exit code 1 for CLI scanner)
- Incident logged to `logs/credential-guard.log`
- Redacted message saved to `logs/blocked-messages/` for review
- Owner should review and sanitize before retrying

### CLI Reference

All rich messaging goes through `send_message.py`:

```powershell
# Base path — run as module from skills/teams/
cd skills/teams
$rich = "scripts.rich.send_message"

# ── Sending to Chats ──────────────────────────────────────────────

# Simple rich HTML message
python -m $rich --to "19:abc...@thread.v2" --body "**Hello** from Agency Cowork"

# @mention someone (single)
python -m $rich --to "19:abc...@thread.v2" --body "Hey {{mention}}, please review" `
  --mention-name "Alice Johnson" --mention-mri "8:orgid:aaaaaaaa-1111-2222-3333-444444444444"

# @mention multiple people
python -m $rich --to "19:abc...@thread.v2" `
  --body "{{mention0}} and {{mention1}}, please review" `
  --mention-name "Bob Smith" --mention-mri "8:orgid:bbbbbbbb-1111-2222-3333-..." `
  --mention-name "Alice Johnson" --mention-mri "8:orgid:aaaaaaaa-1111-2222-3333-..."

# ── Sending to Channels ───────────────────────────────────────────

# Channel by ID (use --channel instead of --to)
python -m $rich --channel "19:KS6nGUz7cs...@thread.tacv2" `
  --body "## Status Update\n### :check: All clear" --subject "Weekly Update"

# Channel by Teams URL (auto-extracts channel ID and team ID)
python -m $rich --url "https://teams.microsoft.com/l/channel/19%3A...@thread.tacv2/General?groupId=..." `
  --body ":rocket: Deployment complete" --subject "Release v2.1"

# Channel with @mentions
python -m $rich --channel "19:KS6nGUz7cs...@thread.tacv2" `
  --body "{mention0} {mention1} — please review the update" `
  --mention-name "Andrew Wall" --mention-mri "8:orgid:a06b60c9-..." `
  --mention-name "Adam Mahood" --mention-mri "8:orgid:19d8df36-..." `
  --subject "Review Request"

# ── Cards, Attachments, Importance ────────────────────────────────

# Send an Adaptive Card from template
python -m $rich --to "19:abc...@thread.v2" `
  --card info-card --card-data '{"title": "Status Update", "body": "All systems go"}'

# Send inline Adaptive Card JSON
python -m $rich --to "19:abc...@thread.v2" --card-json '{"type":"AdaptiveCard","version":"1.4","body":[...]}'

# Attach a local file
python -m $rich --to "19:abc...@thread.v2" --body "Here is the report" --attach "C:\path\to\report.pdf"

# Attach a SharePoint/OneDrive file by URL
python -m $rich --to "19:abc...@thread.v2" --body "See attached" `
  --attach "https://microsoft.sharepoint.com/:x:/t/team/Eabc..."

# Important message with subject
python -m $rich --to "19:abc...@thread.v2" --body "Action needed" `
  --importance high --subject "Urgent: Review Required"
```

**Destination flags (mutually exclusive — pick one):**

| Flag | Description | Example |
|------|-------------|---------|
| `--to ID` | Any conversation ID (chat or channel) | `--to "19:abc...@unq.gbl.spaces"` |
| `--channel ID` | Channel ID (explicit, self-documenting) | `--channel "19:abc...@thread.tacv2"` |
| `--url URL` | Teams channel URL (auto-extracts IDs) | `--url "https://teams.microsoft.com/l/channel/..."` |
| `--team ID` | Optional team GUID (metadata, not required for send) | `--team "ddd9500b-..."` |

### @Mention Workflow

1. **Resolve the person(s)** — look up in `recentcontacts.md` or JSON cache to get their **MRI** (Message Resource Identifier, format: `8:orgid:<GUID>`)
2. **Build the mention(s)** — pass `--mention-name` and `--mention-mri` pairs to `send_message.py` (repeat for multiple mentions)
3. **Use placeholders in `--body`**:
   - Single mention: `{mention}` (legacy) or `{mention0}`
   - Multiple mentions: `{mention0}`, `{mention1}`, `{mention2}`, etc.
   - If no placeholders present, all mentions are auto-prepended to the message

**Examples:**

```powershell
# Single @mention
python -m $rich --to "19:abc...@thread.v2" --body "Hey {mention}, please review" `
  --mention-name "Alice Johnson" --mention-mri "8:orgid:aaaaaaaa-1111-2222-3333-444444444444"

# Multiple @mentions
python -m $rich --to "19:abc...@thread.v2" `
  --body "{mention0} and {mention1}, please review the PR" `
  --mention-name "Bob Smith" --mention-mri "8:orgid:bbbbbbbb-1111-2222-3333-..." `
  --mention-name "Alice Johnson" --mention-mri "8:orgid:aaaaaaaa-1111-2222-3333-..."
```

**MRI sources:**
- `recentcontacts.md` → `userId` field (prefix with `8:orgid:`)
- JSON chat cache → `members[].userId` (prefix with `8:orgid:`)
- Self → `8:orgid:<your-user-id-guid>`

**HTML format:** Mentions are wrapped in `<readonly class="skipProofing">` around a `<span>` — this matches the exact structure the Teams web client emits. Without the `<readonly>` wrapper, Teams renders plain text instead of a clickable mention pill.

### Reply / Threading Workflow

Use `--reply-to <messageId>` to reply to a specific message with a visible quote of the original. Works in **all conversation types** (1:1 chats, group chats, meeting chats, channels).

**How it works:**

1. Pass `--reply-to <messageId>` where `messageId` is the server-assigned ID of the message to reply to
2. The script fetches the parent message via the chatsvc API
3. A `<blockquote>` quote block is built from the parent's sender, timestamp, and content
4. The quote is prepended to your reply content
5. A `replyChainMessageId` is set in the message body to link the reply in Teams' thread UI

**Getting the message ID:**
- From `microsoft-teams-ListChatMessages` or `microsoft-teams-ListChannelMessages` responses — each message has an `id` field
- From `microsoft-teams-GetChatMessage` response
- Message IDs are numeric timestamp strings (e.g. `"1709234567890"`)

**Combinable with all other features:** `--reply-to` works alongside `--mention-name`, `--card`, `--attach`, `--importance`, etc. For example, you can reply to a message with an @mention and a file attachment.

**Quote format:** The quote uses the exact `<blockquote itemscope itemtype="http://schema.skype.com/Reply">` structure the Teams web client emits, including the original sender's MRI, timestamp, and a truncated preview (max 200 chars).

### Read Channel Thread Messages

Read all messages in a channel thread (root message + replies). Uses the Teams Chat Service v2 (chatsvc) API — the only reliable method for thread retrieval without elevated Graph API permissions.

**Why chatsvc?** The standard methods don't work for thread replies:
- MCP `ListChannelMessages` with `expand=replies` — silently ignores the expand parameter
- Graph API `/messages/{id}/replies` — returns 403 (requires `ChannelMessage.Read.All` app permission)
- WorkIQ `SearchTeamsMessages` — semantic search only, cannot target specific threads by ID

**CLI command:**

```powershell
cd skills/teams
python -m scripts.api get-thread \
  --channel "19:abc...@thread.tacv2" \
  --message "1771873567092" \
  --top 200
```

| Flag | Required | Description |
|------|----------|-------------|
| `--channel` | Yes | Channel ID (`19:...@thread.tacv2` format) |
| `--message` | Yes | Root message ID of the thread (numeric timestamp) |
| `--top` | No | Max messages to return (default: 200) |

**Parsing a Teams channel message URL:**

Given a URL like:
```
https://teams.microsoft.com/l/message/19:abc...@thread.tacv2/1771873567092?...&groupId=GUID&...
```

| Component | Location in URL | Needed for |
|-----------|----------------|------------|
| Channel ID | Path segment before `/` (URL-decoded) | `--channel` |
| Message ID | Path segment after `/` | `--message` |
| Team ID (groupId) | Query parameter | MCP channel operations (not needed for `get-thread`) |

**Authentication:** Uses Azure CLI token for `https://ic3.teams.office.com` — no Playwright browser session needed. Fast (~200ms).

**Output format (JSON):**

```json
{
  "count": 7,
  "channelId": "19:abc...@thread.tacv2",
  "threadMessageId": "1771873567092",
  "messages": [
    {
      "id": "1771873567092",
      "messageType": "RichText/Html",
      "content": "<p>Root message content...</p>",
      "senderName": "John Doe",
      "senderId": "8:orgid:...",
      "timestamp": "2025-06-24T..."
    }
  ]
}
```

**API details (internal):**
- Endpoint: `GET {CHATSVC_BASE}/users/ME/conversations/{channelId};messageid={messageId}/messages`
- The `;messageid={messageId}` suffix on the channelId scopes to a specific thread
- `startTime=1` retrieves all messages from the beginning
- Messages returned in chronological order (oldest first)
- Content is HTML — strip tags for plain-text summarization

**Agent workflow — thread summarization:**
1. Parse the Teams message URL to extract `channelId` and `messageId`
2. Run `get-thread` CLI to retrieve all thread messages
3. Strip HTML tags from message content
4. Summarize the conversation

### Adaptive Card Workflow

**Templates** are in `skills/teams/templates/`:

| Template | File | Variables |
|----------|------|-----------|
| Info Card | `info-card.json` | `title`, `body` |
| Action Card | `action-card.json` | `title`, `body`, `action_label`, `action_url` |
| Table Card | `table-card.json` | `title`, `col1_header`, `col2_header`, `col1_row1`–`col1_row3`, `col2_row1`–`col2_row3` |

**Usage:** `--card <template-name> --card-data '{"title": "...", "body": "..."}'`

For fully custom cards, use `--card-json '<full JSON>'` instead.

### File Attachment Workflow

**Local file** (4-step protocol):
1. Upload file to SharePoint "Microsoft Teams Chat Files" folder via classic SP REST API
2. Create AMS (Azure Media Services) object reference
3. Upload file content to AMS endpoint
4. Send message with `amsreferences` and file properties

**SharePoint/OneDrive URL reference** (no upload):
- Pass the SPO URL as `--attach`; file is displayed as a clickable chiclet
- The file is not re-uploaded — recipient must have access to the original

### Authentication

The Playwright browser session uses **CDP (Chrome DevTools Protocol)** to connect to a standalone Edge process with a persistent profile at `~/.teams-agent/browser-profile/`.

**Auth cascade:**
1. **CDP (primary)** — launches Edge with `--remote-debugging-port=9223` and standalone profile, connects via `connect_over_cdp`. Works with Edge open.
2. **Real Edge profile** — uses `launch_persistent_context` with real Edge User Data (only if Edge fully closed).
3. **Standalone profile, headed** — fallback for interactive MFA.

**On successful connect:**
1. Navigates to `teams.cloud.microsoft/`
2. Captures Bearer tokens from network requests per service endpoint
3. Tokens are cached in memory for the session duration

**Token services captured:** `chatsvc`, `mt`, `csa`, `authsvc`, `skype`

**Prerequisites:**
- `pip install playwright>=1.40 python-dotenv>=1.0` (or `pip install -r skills/teams/requirements.txt`)
- Microsoft Edge browser installed
- Valid Microsoft 365 authentication (SSO or interactive login)

### Key Engineering Notes

These are critical implementation details discovered during development:

1. **`<readonly>` wrapper required for @mentions.** Without `<readonly class="skipProofing">`, Teams renders plain text instead of a clickable mention pill. The `itemscope` attribute must be bare (`itemscope`), NOT `itemscope=""`.
2. **Classic REST API for SPO uploads, NOT Graph v2.0.** SharePoint file upload via `/_api/v2.0/drive/root:/...:/content` returns 403 Forbidden. Must use classic REST: `/_api/web/GetFolderByServerRelativeUrl(...)/Files/add(...)` with `X-RequestDigest`.
3. **AMS uses `skype_token`, not Bearer.** AMS (`us-api.asm.skype.com`) requires `Authorization: skype_token {token}` from the `x-skypetoken` header captured during Teams page load.
4. **Cross-origin requires temp browser pages.** Cannot `fetch()` SharePoint or AMS from the `teams.cloud.microsoft` page (CORS). Solution: open temp page on target origin, fetch there (same-origin), close.
5. **`properties` fields are double-encoded JSON.** The `mentions`, `cards`, `files`, `links` fields in message properties are JSON strings inside JSON — must be `json.dumps()`'d before placement in the outer body.
6. **AMS rejects automation User-Agents.** The browser must be launched with a realistic Chrome/Edge UA string; Playwright's default UA causes 401.

For full architectural documentation, see the Key Engineering Notes above and the inline code comments in `scripts/rich/`.

## Direct API Client

The Teams skill includes a **direct API client** (`scripts/api/`) that bypasses the Teams MCP server for significantly faster operations. It uses the same REST endpoints as the Teams web client (discovered via HAR trace analysis).

### Architecture

The client uses **Azure CLI tokens for Graph API** (channel operations) and **chatsvc tokens** (chat operations), with optional **Playwright browser session** for read operations:

| Operation | Auth Method | API | Speed |
|-----------|-------------|-----|-------|
| Send to chat | chatsvc token (CLI) | chatsvc REST | ~0.3s |
| Post to channel | Graph token (CLI) | Microsoft Graph | ~0.5s |
| Reply to channel thread | Graph token (CLI) | Microsoft Graph | ~0.5s |
| **send-rich** (markdown→HTML→send) | CLI tokens (auto) | chatsvc or Graph | ~1s |
| List chats (CSA) | Playwright browser | CSA REST | ~1-3s |
| List messages | Playwright browser | chatsvc REST | ~0.5-1s |
| List teams/channels | Playwright browser | CSA REST | ~1s |
| **Sync cache** | Playwright or stdin | CSA REST | Bulk |

### CLI Reference

```powershell
cd skills/teams

# ── Send Operations (no Playwright needed) ────────────────────────

# High-level: markdown file → Teams HTML → validate → send to chat
python -m scripts.api send-rich --chat <ID> --body-file body.md

# High-level: markdown file → Teams HTML → validate → post to channel
python -m scripts.api send-rich --team <TEAM_GUID> --channel <CHANNEL_ID> --body-file body.md

# High-level: markdown file → Teams HTML → validate → reply to channel thread
python -m scripts.api send-rich --team <TEAM_GUID> --channel <CHANNEL_ID> --message <MSG_ID> --body-file body.md

# Send short message to chat (with optional markdown conversion)
python -m scripts.api send-message --chat <ID> --body ":rocket: **Hello**" --markdown

# Post to channel (Graph API, with optional markdown conversion)
python -m scripts.api post-channel --team <TEAM_GUID> --channel <CHANNEL_ID> --body "text" [--markdown] [--body-file FILE]

# Reply to channel thread (Graph API, with optional markdown conversion)
python -m scripts.api reply-channel --team <TEAM_GUID> --channel <CHANNEL_ID> --message <MSG_ID> --body "reply" [--markdown] [--body-file FILE]

# ── Read Operations ────────────────────────────────────────────────

# List chats (via CSA /v3/teams/users/me/updates)
python -m scripts.api list-chats [--filter TOPIC] [--top N]

# List messages in a chat
python -m scripts.api list-messages --chat <ID> [--top 50]

# List teams and channels
python -m scripts.api list-teams

# List channels (optionally by team)
python -m scripts.api list-channels [--team <TEAM_ID>]

# Get chat details
python -m scripts.api get-chat --chat <ID>

# Get chat members
python -m scripts.api get-members --chat <ID>

# Get channel members (Graph API — requires ChannelMember.Read.All)
python -m scripts.api channel-members --team <TEAM_GUID> --channel <CHANNEL_ID>
```

### Cache Sync (Key Feature)

The `sync-cache` command fetches Teams data and writes **directly** to cache files — no manual JSON processing needed:

```powershell
cd skills/teams

# Sync all caches via browser session (requires Playwright + Edge)
python -m scripts.api sync-cache

# Sync only chats
python -m scripts.api sync-cache --chats

# Sync only teams/channels
python -m scripts.api sync-cache --teams

# Also enrich people with UPNs (slower — queries MT per user)
python -m scripts.api sync-cache --enrich-upns

# MCP fallback: pipe raw JSON from stdin (no browser needed)
echo '{"chats": [...]}' | python -m scripts.api sync-cache --from-stdin --chats
echo '{"channels": [...]}' | python -m scripts.api sync-cache --from-stdin --teams
```

**What sync-cache does:**
1. Connects to Teams via Playwright (or reads stdin JSON with `--from-stdin`)
2. Fetches chats + members from CSA updates endpoint
3. Fetches teams + channels from CSA updates endpoint
4. Writes `chats.json`, `teams-and-channels.json`, `people.json` directly
5. Auto-populates `recentcontacts.md` with all people, chats, and channels
6. Reports a summary: `{"status": "synced", "totalChats": N, "totalTeams": N, ...}`

**When to use sync-cache:**
- After initial setup (empty cache)
- When cache is stale (> 4 hours)
- When user says "refresh Teams cache"
- Prefer `--from-stdin` mode when browser auth is unavailable

### API Modules

| Module | Path | Purpose |
|--------|------|---------|
| `client.py` | `scripts/api/client.py` | `TeamsApiClient` — Playwright browser for reads, direct HTTP for writes. Includes `graph_post()` and `graph_get()` for Graph API. |
| `auth.py` | `scripts/api/auth.py` | Token managers for both `ic3.teams.office.com` (chatsvc) and `graph.microsoft.com` (Graph API). `get_token_manager()` and `get_graph_token_manager()`. |
| `chats.py` | `scripts/api/chats.py` | `list_chats()`, `get_chat()`, `get_chat_members()` |
| `messages.py` | `scripts/api/messages.py` | `list_messages()`, `send_message()`, `post_channel_message()`, `reply_to_channel_message()`, `get_channel_members()` |
| `teams_channels.py` | `scripts/api/teams_channels.py` | `list_teams_and_channels()`, `list_channels()` |
| `sync_cache.py` | `scripts/api/sync_cache.py` | Cache sync — `sync_all_from_api()`, `sync_chats_from_json()` |
| `models.py` | `scripts/api/models.py` | `Chat`, `Message`, `Member`, `Team`, `Channel` dataclasses |

## Chat Monitoring

The Teams skill includes a **real-time monitor service** that listens for @mentions in specified conversations and dispatches them as agent prompts. Configuration lives in `${PLUGIN_ROOT}/monitor/monitor-config.json`.

### Monitor Config Structure

```json
{
  "enabled": false,
  "keyword": "@agent",
  "authorized_sender": { "mri": "...", "displayName": "...", "upn": "..." },
  "monitored_conversations": [
    { "id": "<chat-id>", "name": "Display Name", "type": "Self|OneOnOne|Group|Meeting|Channel", "added": "ISO-8601" }
  ],
  "dispatch": { "command": "agency copilot -p", "working_directory": "...", "timeout_minutes": 30 },
  "connection": { "trouter_gateway": "...", "token_refresh_minutes": 50, "heartbeat_interval_seconds": 30 }
}
```

### Managing Monitored Conversations

When the user asks to **"monitor"** a chat or channel, resolve its ID (via recentcontacts.md or cache) and add it to `monitor-config.json`:

1. **Resolve the chat/channel ID** — follow the standard Phase 1/2 lookup (recentcontacts.md → cache → MCP)
2. **Edit `${PLUGIN_ROOT}/monitor/monitor-config.json`** — append a new entry to `monitored_conversations` with `id`, `name`, `type`, and `added` (current ISO-8601 timestamp)
3. **Confirm** — report the addition to the user

To **stop monitoring**, remove the entry from `monitored_conversations`.

**Programmatic access** (from Python):
```python
from scripts.monitor.config import load_config, save_config
cfg = load_config()
cfg.add_conversation("<chat-id>", "Chat Name", "Meeting")
save_config(cfg)
```

### Monitor Service Commands

The service runs as a long-lived WebSocket listener via `scripts/monitor/service.py`:

```powershell
cd skills/teams

# Start the monitor daemon (detached background process)
python -m scripts.monitor.service start

# Check if the service is running
python -m scripts.monitor.service status

# Stop the service
python -m scripts.monitor.service stop

# Enable monitoring (sets enabled=true in agentconfig.json)
python -m scripts.monitor.service enable
```

**Key components:**
| File | Purpose |
|------|---------|
| `monitor/monitor-config.json` | Monitored conversations, dispatch settings, connection params |
| `scripts/monitor/service.py` | Main daemon — WebSocket listener, token refresh, reconnection |
| `scripts/monitor/config.py` | Config load/save, conversation add/remove helpers |
| `scripts/monitor/trouter_client.py` | Teams Trouter WebSocket client (real-time push) |
| `scripts/monitor/message_handler.py` | Event filter (keyword, sender, conversation) → agent dispatch |
| `monitor/monitor.pid` | PID file for the running service |

### How It Works

1. Service connects to Teams **Trouter** (WebSocket push gateway) using captured auth tokens
2. Incoming messages are filtered by: **keyword** (`@agent`), **authorized sender**, and **conversation ID** (must be in `monitored_conversations`)
3. Matching messages are dispatched as prompts to `agency copilot -p "<extracted prompt>"`
4. Optional receipt/summary is sent back to the originating conversation
5. Prompt injection guard (`scripts/prompt_guard.py`) scans all inbound prompts before dispatch

## Known Limitations

- **Playwright is optional for most sends.** Direct chatsvc HTTP POST with az CLI token handles rich HTML messages without a browser. Playwright is only needed for @mentions, Adaptive Cards, and file attachments.
- **ASCII emojis only for MCP messages.** Use standard ASCII emoticons (`:)`, `:D`, `:(`), `;)`, etc.) instead of Unicode emoji characters in MCP plain-text messages. Rich messages support Unicode emojis.
- **File attachments require SharePoint access + Playwright.** The 4-step SPO+AMS protocol auto-discovers the SharePoint site from JWT claims. If the user lacks SP access, local file attachments will fail.
- **Rich send is one-way only.** Rich messaging handles sends. All reads, searches, and management use MCP tools or the direct API client.

## Gotchas

These are real failure modes that have caused bugs — read before operating.

### API Behavior

- **Teams API returns 200 (not 404) for requests to the wrong region.** Only treat 201/202 as confirmed success. A 200 response may contain region auto-correction hints in the body — parse it before assuming the operation succeeded.
- **`set_reply_prefix()` must be called during init, not just imported.** Importing a setter function is not the same as invoking it. Always verify setter functions are invoked, not just imported, or messages will be sent without the `Agency Cowork:` prefix.

### Cache System

- **Cache staleness: the 4-hour TTL means recently created chats/channels won't appear** until cache refresh. If a lookup fails unexpectedly for a chat/channel you know exists, run `refresh all` first.
- **`recentcontacts.md` name matching is case-sensitive.** Normalize casing when adding entries. "alice johnson" won't match "Alice Johnson".
- **Never call `ListChats` without `top: 20` pagination.** Unpaginated calls WILL time out for users with many chats — this is not a theoretical risk, it happens regularly.

### Rich Messaging (Playwright)

- **Playwright rich messaging requires an active browser session.** If the session expires or the browser context is stale, messages will silently fail to send — no error, just no delivery.
- **Adaptive Card payloads have a ~28KB limit in Teams.** Cards exceeding this are silently truncated or rejected — no error message.
- **Rich send is one-way only.** Playwright handles sends; all reads, searches, and management must use MCP tools.

### Platform

- **On macOS, Playwright rich messaging may require additional browser setup** (chromium install via `playwright install chromium`). If rich messaging fails silently, check browser installation first.
- **Cache file paths use forward slashes** — they are cross-platform. Do not hardcode Windows backslashes.

## Composes With

- **send-email** — Cross-reference email threads when composing Teams follow-ups
- **weekly-report** — Pull channel activity and blocker discussions for weekly reports
- **workstreams** — Post meeting summaries and action items to team channels
- **email-triage** — Surface urgent items to the user's Teams self-chat
- **calendar** — Reference meeting context when composing channel updates
- **ado** — Pull work item status for sprint standup messages

## Rules

- **ALWAYS** prefix outgoing messages with `Agency Cowork:` — every message sent via `PostMessage`, `PostChannelMessage`, `ReplyToChannelMessage`, or `send_message.py` must begin with `Agency Cowork: ` followed by the message content
- **ALWAYS** run `python scripts/cache-manager.py resolve "<query>"` as the FIRST step when resolving a person, chat, or channel — this searches recentcontacts.md + JSON caches in one call and returns JSON. Only proceed to MCP/WorkIQ if `found: false`.
- **NEVER** read `recentcontacts.md` as a raw file — always use the `resolve` command or `cache-manager.py` scripts for lookups
- **NEVER** call `ListChats` or `ListTeams` when `resolve` or `lookup-*` returns results. Exhausting the local cache before MCP calls is mandatory.
- **ALWAYS** use `workiq-ask_work_iq` for person UPN resolution before calling ListChats — WorkIQ returns in ~2-3 seconds vs 10-30+ seconds for ListChats. **NEVER call ListChats just to find a person's UPN.**
- **ALWAYS** use `top: 20` pagination with `ListChats` — **NEVER call ListChats without the `top` parameter**. Unfiltered, unpaginated ListChats WILL time out for users with many chats.
- **ALWAYS** use filtered `ListChats` (with `userUpns` or `topic`) when looking for a specific chat — combine with `top: 20` for safety
- **ALWAYS** include `members` array (with `displayName`, `upn`, `userId`) in every chat entry when writing to the chats cache — without members, `lookup-chat` cannot resolve person → chat
- **ALWAYS** use `$top` to limit results when you only need recent messages (e.g., `top: 10` for the latest messages)
- **ALWAYS** update the cache after resolving any new person, chat, or channel via `cache-manager.py add-person`, `add-chat`, `add-channel` — this builds the fast-lookup index over time
- **ALWAYS** use cache for ID resolution before making MCP calls — avoid redundant ListChats/ListTeams calls
- **ALWAYS** confirm with the user before sending messages (same as send-email skill)
- **NEVER** fabricate UPNs, team IDs, channel IDs, or chat IDs
- **NEVER** send messages without explicit user approval
- Cache refresh should be silent/background — don't overwhelm the user with cache status unless they ask
- When cache is stale, refresh it transparently and continue with the operation
- For person lookup failures, use `workiq-ask_work_iq` as the **first** external fallback, not ListChats
- **PREFER** MCP `PostMessage` with `contentType: 'html'` for formatted messages — supports `<b>`, `<i>`, `<a>`, `<ul>/<li>`, `<h1>`–`<h3>`, `<table>`, `<pre><code>`. No Playwright or scripts needed. Use `contentType: 'text'` only when no formatting is needed
- **ALWAYS** use standard ASCII emojis in MCP messages (e.g., `:)` `:D` `:P` `:(` `;)` `<3` `:O` `:/` `B)` `XD`) — rich messages may use Unicode emojis
- **ALWAYS** format messages for readability — use line breaks (`\n`) to separate sections, bullet points (`-` or `*`) for lists, and avoid long walls of text. Messages can be multiline.
- Include the current user's UPN when creating chats (look up via WorkIQ if not cached)
- **PREFER** MCP `PostMessage` with `contentType: 'html'` for short formatted messages — faster and simpler than `scripts.api` or Playwright
- **PREFER** `scripts.api send-rich` for long/complex messages needing validation, credential scanning, and emoji conversion
- **USE** `send_message.py` (Playwright) only when the message needs @mentions, Adaptive Cards, file attachments, or importance/subject
- **ALWAYS** resolve MRI (Message Resource Identifier) from cache before sending @mentions — format: `8:orgid:<GUID>`
- **ALWAYS** run `python -m scripts.rich.validate_message --body "<body>"` before every `send_message.py` call — if validation fails (exit code 1), fix the broken emojis or links before sending
- **ALWAYS** run `python -m scripts.rich.credential_scanner --text "<message content>"` before calling `PostMessage` or `PostChannelMessage` — if exit code 1, **DO NOT SEND**. Alert the user with the findings. Rich messages via `send_message.py` are auto-scanned (no manual step needed)
- **WHEN user provides a Teams URL**, extract the chat ID directly from the URL and use `python -m scripts.api get-members --chat <id>` to get participants — skip resolve and WorkIQ lookups entirely. Cache all discovered members via `add-person` and `add-chat`.
- **FOR long messages** (>2K chars, announcements, tables, reports), use the file-based send pattern: write markdown to temp file → convert via `markdown_to_teams_html()` → validate → send via direct chatsvc HTTP POST with az CLI token → clean up. Never attempt to inline long messages as shell strings.
- **PREFER direct chatsvc HTTP POST** over Playwright `send_message.py` for rich HTML sends — faster (~1s vs ~5s), more reliable (no browser dependency), same HTML rendering. Use Playwright only when @mentions, Adaptive Cards, or file attachments are required.
