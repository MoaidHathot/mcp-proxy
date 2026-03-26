using System.Text.Json;
using McpProxy.Abstractions;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Hooks;

/// <summary>
/// A pre-invoke hook that automatically adds default parameters to message-posting tools.
/// For example, sets <c>contentType</c> to <c>"html"</c> when not specified, so messages
/// support rich formatting (bold, lists, tables, links) by default.
/// </summary>
public sealed partial class TeamsMessageDefaultsHook : IPreInvokeHook
{
    private readonly ILogger<TeamsMessageDefaultsHook> _logger;
    private readonly string _defaultContentType;

    private static readonly HashSet<string> s_messageTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "PostMessage",
        "SendChatMessage",
        "PostChannelMessage",
        "SendChannelMessage",
        "ReplyToMessage",
        "ReplyToChannelMessage",
        // Prefixed variants
        "teams_PostMessage",
        "teams_SendChatMessage",
        "teams_PostChannelMessage",
        "teams_SendChannelMessage",
        "teams_ReplyToMessage",
        "teams_ReplyToChannelMessage",
    };

    /// <summary>
    /// Initializes a new instance of <see cref="TeamsMessageDefaultsHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="defaultContentType">Default content type for messages. Default is "html".</param>
    public TeamsMessageDefaultsHook(ILogger<TeamsMessageDefaultsHook> logger, string defaultContentType = "html")
    {
        _logger = logger;
        _defaultContentType = defaultContentType;
    }

    /// <inheritdoc />
    public int Priority => 200; // Execute after pagination hook but before credential scanning

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        if (!IsMessageTool(context.ToolName))
        {
            return ValueTask.CompletedTask;
        }

        // Check if contentType is already specified
        if (context.Request.Arguments is not null &&
            context.Request.Arguments.TryGetValue("contentType", out var existing) &&
            existing.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(existing.GetString()))
        {
            return ValueTask.CompletedTask;
        }

        // Also check content_type (snake_case variant)
        if (context.Request.Arguments is not null &&
            context.Request.Arguments.TryGetValue("content_type", out var snakeExisting) &&
            snakeExisting.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(snakeExisting.GetString()))
        {
            return ValueTask.CompletedTask;
        }

        // Add default contentType
        LogContentTypeAdded(_logger, context.ToolName, _defaultContentType);

        var args = context.Request.Arguments is not null
            ? new Dictionary<string, JsonElement>(context.Request.Arguments)
            : new Dictionary<string, JsonElement>();

        args["contentType"] = JsonSerializer.SerializeToElement(_defaultContentType);

        context.Request = new CallToolRequestParams
        {
            Name = context.Request.Name,
            Arguments = args
        };

        return ValueTask.CompletedTask;
    }

    private static bool IsMessageTool(string toolName)
    {
        if (s_messageTools.Contains(toolName))
        {
            return true;
        }

        // Fallback suffix match for other prefixes (msgraph_, graph_, etc.)
        return s_messageTools.Any(t =>
            toolName.EndsWith(t, StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding default contentType='{ContentType}' to {ToolName}")]
    private static partial void LogContentTypeAdded(ILogger logger, string toolName, string contentType);
}
