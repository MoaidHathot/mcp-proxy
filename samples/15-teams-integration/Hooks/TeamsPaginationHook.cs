using System.Text.Json;
using McpProxy.Abstractions;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Hooks;

/// <summary>
/// A pre-invoke hook that automatically adds pagination parameters to ListChats calls.
/// Ensures that ListChats never returns more than the configured number of items per request.
/// </summary>
public sealed partial class TeamsPaginationHook : IPreInvokeHook
{
    private readonly ILogger<TeamsPaginationHook> _logger;
    private readonly int _defaultTop;
    private readonly HashSet<string> _paginatedTools;

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
            "ListChannelMessages",
            // Prefixed versions
            "teams_ListChats",
            "teams_ListTeams",
            "teams_ListChannels",
            "teams_ListChatMembers",
            "teams_ListMessages",
            "teams_ListChannelMessages",
            "msgraph_ListChats",
            "msgraph_ListTeams",
            "msgraph_ListChannels",
            "msgraph_ListChatMembers",
            "msgraph_ListMessages",
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
            return ValueTask.CompletedTask;
        }

        // Add default pagination
        LogPaginationAdded(_logger, toolName, _defaultTop);
        SetTopParameter(context, _defaultTop);

        return ValueTask.CompletedTask;
    }

    private bool IsPaginatedTool(string toolName)
    {
        // Check exact match first
        if (_paginatedTools.Contains(toolName))
        {
            return true;
        }

        // Check suffix match (for prefixed tools)
        return _paginatedTools.Any(t =>
            toolName.EndsWith(t, StringComparison.OrdinalIgnoreCase) ||
            toolName.EndsWith($"_{t}", StringComparison.OrdinalIgnoreCase));
    }

    private static void SetTopParameter(HookContext<CallToolRequestParams> context, int top)
    {
        // Create new arguments with 'top' parameter
        var args = context.Request.Arguments is not null
            ? new Dictionary<string, JsonElement>(context.Request.Arguments)
            : new Dictionary<string, JsonElement>();

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
}
