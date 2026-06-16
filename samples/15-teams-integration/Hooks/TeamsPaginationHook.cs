using System.Collections.Concurrent;
using System.Text.Json;
using McpProxy.Abstractions;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Hooks;

/// <summary>
/// A pre-invoke hook that enforces a safe page size on Teams "list" calls so they don't
/// return unbounded result sets (an unpaginated <c>ListChats</c> times out for users with
/// many chats).
/// </summary>
/// <remarks>
/// The hook is <b>schema-aware</b>: it only ever sets a parameter the target tool actually
/// declares (per <see cref="HookContext{TRequest}.ToolInputSchema"/>). Backends differ in how
/// they paginate — some list tools accept an OData <c>top</c> (page size), others a boolean
/// <c>fetchAllPages</c>. Blindly injecting <c>top</c> into a tool that does not declare it makes
/// the backend reject the entire call (e.g. <c>Unknown argument: 'top' for tool 'ListChats'</c>),
/// which is exactly what broke before this became schema-aware. When the schema is unknown the
/// hook makes no changes rather than risk breaking the call.
/// </remarks>
public sealed partial class TeamsPaginationHook : IPreInvokeHook
{
    private readonly ILogger<TeamsPaginationHook> _logger;
    private readonly int _defaultTop;
    private readonly HashSet<string> _paginatedTools;
    private readonly ConcurrentDictionary<string, byte> _warnedNoPageKnob = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="TeamsPaginationHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="defaultTop">Default number of items per page. Default is 20.</param>
    public TeamsPaginationHook(ILogger<TeamsPaginationHook> logger, int defaultTop = 20)
    {
        _logger = logger;
        _defaultTop = defaultTop;
        _paginatedTools =
        [
            "ListChats",
            "ListTeams",
            "ListChannels",
            "ListChatMembers",
            "ListMessages",
            "ListChatMessages",
            "ListChannelMessages",
            // Prefixed versions
            "teams_ListChats",
            "teams_ListTeams",
            "teams_ListChannels",
            "teams_ListChatMembers",
            "teams_ListMessages",
            "teams_ListChatMessages",
            "teams_ListChannelMessages",
            "msgraph_ListChats",
            "msgraph_ListTeams",
            "msgraph_ListChannels",
            "msgraph_ListChatMembers",
            "msgraph_ListMessages",
            "msgraph_ListChatMessages",
            "msgraph_ListChannelMessages"
        ];
    }

    /// <inheritdoc />
    public int Priority => 100; // Execute early to set params before other processing

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var toolName = context.ToolName;

        // Check if this is a paginated tool
        if (!IsPaginatedTool(toolName))
        {
            return ValueTask.CompletedTask;
        }

        // Only act on a parameter the tool actually declares. Without the schema we cannot
        // tell which pagination knob (if any) the backend supports, so we leave the request
        // untouched rather than inject an argument that could be rejected.
        var schema = context.ToolInputSchema;
        if (schema is null)
        {
            return ValueTask.CompletedTask;
        }

        var supportsTop = SchemaDeclaresProperty(schema.Value, "top");
        var supportsFetchAllPages = SchemaDeclaresProperty(schema.Value, "fetchAllPages");

        if (!supportsTop && !supportsFetchAllPages)
        {
            // Drift signal: a tool we treat as paginated no longer exposes any pagination knob we
            // recognize. Warn once per tool so a future backend change is visible in the logs.
            if (_warnedNoPageKnob.TryAdd(toolName, 0))
            {
                LogNoPaginationKnob(_logger, toolName);
            }
        }

        if (supportsTop)
        {
            // Tool accepts `top` (page size) — ensure it is present and capped.
            EnforceTopLimit(context, toolName);
            return ValueTask.CompletedTask;
        }

        // Tool does NOT accept `top`. Two things to fix:
        //   1. Strip any caller-supplied `top` (e.g. from an LLM following older guidance)
        //      so it doesn't break the call.
        //   2. If the tool exposes a boolean `fetchAllPages`, default it to false so the
        //      call returns a single page instead of crawling every page (which times out).
        var args = CloneArguments(context.Request.Arguments);
        var changed = false;

        if (args.Remove("top"))
        {
            LogUnsupportedTopRemoved(_logger, toolName);
            changed = true;
        }

        if (supportsFetchAllPages && !HasExplicitBoolean(args, "fetchAllPages"))
        {
            args["fetchAllPages"] = JsonSerializer.SerializeToElement(false);
            LogFetchAllPagesDisabled(_logger, toolName);
            changed = true;
        }

        if (changed)
        {
            context.Request = new CallToolRequestParams
            {
                Name = context.Request.Name,
                Arguments = args
            };
        }

        return ValueTask.CompletedTask;
    }

    private void EnforceTopLimit(HookContext<CallToolRequestParams> context, string toolName)
    {
        // Check if 'top' is already specified
        if (context.Request.Arguments is not null &&
            context.Request.Arguments.TryGetValue("top", out var existingTop) &&
            existingTop.ValueKind == JsonValueKind.Number)
        {
            // Enforce maximum
            var topValue = existingTop.TryGetInt32(out var intValue) ? intValue : _defaultTop;
            if (topValue > _defaultTop)
            {
                LogPaginationCapped(_logger, toolName, topValue, _defaultTop);
                SetTopParameter(context, _defaultTop);
            }

            return;
        }

        // Add default pagination
        LogPaginationAdded(_logger, toolName, _defaultTop);
        SetTopParameter(context, _defaultTop);
    }

    private bool IsPaginatedTool(string toolName)
    {
        // Check exact match first
        if (_paginatedTools.Contains(toolName))
        {
            return true;
        }

        // Check suffix match for prefixed tools (e.g. `teams_ListChats`,
        // `microsoft-teams-ListChats`). Require a prefix separator so we don't accidentally
        // match an unrelated tool whose name merely ends with a paginated tool name.
        return _paginatedTools.Any(t =>
            toolName.EndsWith($"_{t}", StringComparison.OrdinalIgnoreCase) ||
            toolName.EndsWith($"-{t}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true when the tool's JSON Schema declares a top-level property with the given
    /// name (case-insensitive). Standard MCP tool input schemas put parameters under
    /// <c>properties</c>.
    /// </summary>
    private static bool SchemaDeclaresProperty(JsonElement schema, string propertyName)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var prop in properties.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasExplicitBoolean(Dictionary<string, JsonElement> args, string name)
        => args.TryGetValue(name, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static Dictionary<string, JsonElement> CloneArguments(IDictionary<string, JsonElement>? args)
        => args is not null
            ? new Dictionary<string, JsonElement>(args)
            : new Dictionary<string, JsonElement>();

    private static void SetTopParameter(HookContext<CallToolRequestParams> context, int top)
    {
        // Create new arguments with 'top' parameter
        var args = CloneArguments(context.Request.Arguments);
        args["top"] = JsonSerializer.SerializeToElement(top);

        context.Request = new CallToolRequestParams
        {
            Name = context.Request.Name,
            Arguments = args
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding pagination to {ToolName}: top={Top}")]
    private static partial void LogPaginationAdded(ILogger logger, string toolName, int top);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Capping pagination for {ToolName}: {Requested} -> {Capped}")]
    private static partial void LogPaginationCapped(ILogger logger, string toolName, int requested, int capped);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removing unsupported 'top' argument from {ToolName} (tool does not declare it)")]
    private static partial void LogUnsupportedTopRemoved(ILogger logger, string toolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Defaulting fetchAllPages=false for {ToolName} to bound result size")]
    private static partial void LogFetchAllPagesDisabled(ILogger logger, string toolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Paginated tool '{ToolName}' declares neither 'top' nor 'fetchAllPages' in its schema - the backend pagination contract may have changed")]
    private static partial void LogNoPaginationKnob(ILogger logger, string toolName);
}
