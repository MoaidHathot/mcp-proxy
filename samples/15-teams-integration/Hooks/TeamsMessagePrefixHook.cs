using System.Text.Json;
using McpProxy.Abstractions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Hooks;

/// <summary>
/// A pre-invoke hook that automatically prefixes outbound Teams messages.
/// Useful for adding "[AI]" or "[Agent]" prefixes to indicate AI-generated content.
/// </summary>
public sealed partial class TeamsMessagePrefixHook : IPreInvokeHook
{
    private readonly ILogger<TeamsMessagePrefixHook> _logger;
    private readonly string _prefix;
    private readonly bool _addSeparator;
    private readonly HashSet<string> _messageTools;

    /// <summary>
    /// Initializes a new instance of <see cref="TeamsMessagePrefixHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="prefix">The prefix to add to messages. Default is "[AI]".</param>
    /// <param name="addSeparator">Whether to add a separator (space or newline) after prefix. Default is true.</param>
    public TeamsMessagePrefixHook(
        ILogger<TeamsMessagePrefixHook> logger,
        string prefix = "[AI]",
        bool addSeparator = true)
    {
        _logger = logger;
        _prefix = prefix;
        _addSeparator = addSeparator;
        _messageTools =
        [
            "SendChatMessage",
            "SendChannelMessage",
            "ReplyToMessage",
            "ReplyToChatMessage",
            "ReplyToChannelMessage",
            "CreateChatMessage",
            "CreateChannelMessage",
            // Prefixed versions
            "teams_SendChatMessage",
            "teams_SendChannelMessage",
            "teams_ReplyToMessage",
            "teams_ReplyToChatMessage",
            "teams_ReplyToChannelMessage",
            "teams_CreateChatMessage",
            "teams_CreateChannelMessage",
            "msgraph_SendChatMessage",
            "msgraph_SendChannelMessage"
        ];
    }

    /// <inheritdoc />
    public int Priority => 200; // Execute after credential scanning but before sending

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var toolName = context.ToolName;

        // Check if this is a message-sending tool
        if (!IsMessageTool(toolName))
        {
            return ValueTask.CompletedTask;
        }

        // Find and modify message content
        var modified = TryPrefixMessage(context);

        if (modified)
        {
            LogPrefixAdded(_logger, toolName, _prefix);
        }

        return ValueTask.CompletedTask;
    }

    private bool IsMessageTool(string toolName)
    {
        // Check exact match first
        if (_messageTools.Contains(toolName))
        {
            return true;
        }

        // Check suffix match (for prefixed tools)
        return _messageTools.Any(t =>
            toolName.EndsWith(t, StringComparison.OrdinalIgnoreCase) ||
            toolName.EndsWith($"_{t}", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryPrefixMessage(HookContext<CallToolRequestParams> context)
    {
        var args = context.Request.Arguments;
        if (args is null)
        {
            return false;
        }

        // Try common parameter names for message content
        string[] contentParams = ["content", "message", "body", "text", "messageContent", "chatMessage"];

        foreach (var param in contentParams)
        {
            if (args.TryGetValue(param, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var originalContent = value.GetString();
                if (originalContent is null)
                {
                    continue;
                }

                // Don't double-prefix
                if (originalContent.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Build prefixed content
                var separator = _addSeparator ? " " : "";
                var prefixedContent = $"{_prefix}{separator}{originalContent}";

                // Create new arguments with prefixed content
                var newArgs = new Dictionary<string, JsonElement>(args);
                newArgs[param] = JsonSerializer.SerializeToElement(prefixedContent);

                context.Request = new CallToolRequestParams
                {
                    Name = context.Request.Name,
                    Arguments = newArgs
                };
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Added prefix to {ToolName}: {Prefix}")]
    private static partial void LogPrefixAdded(ILogger logger, string toolName, string prefix);
}
