using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration.Utilities;
using ModelContextProtocol.Protocol;

namespace McpProxy.Samples.TeamsIntegration.Hooks;

/// <summary>
/// A pre-invoke hook that scans outbound Teams messages for credentials and blocks them.
/// Prevents accidental leakage of API keys, tokens, passwords, and other sensitive data.
/// </summary>
public sealed partial class TeamsCredentialScanHook : IPreInvokeHook
{
    private readonly ILogger<TeamsCredentialScanHook> _logger;
    private readonly bool _blockOnDetection;
    private readonly HashSet<string> _messageTools;

    /// <summary>
    /// Initializes a new instance of <see cref="TeamsCredentialScanHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="blockOnDetection">Whether to block messages with credentials. Default is true.</param>
    public TeamsCredentialScanHook(ILogger<TeamsCredentialScanHook> logger, bool blockOnDetection = true)
    {
        _logger = logger;
        _blockOnDetection = blockOnDetection;
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
    public int Priority => 50; // Execute early to block before any processing

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var toolName = context.ToolName;

        // Check if this is a message-sending tool
        if (!IsMessageTool(toolName))
        {
            return ValueTask.CompletedTask;
        }

        // Get message content from various possible parameter names
        var messageContent = GetMessageContent(context);

        if (string.IsNullOrEmpty(messageContent))
        {
            return ValueTask.CompletedTask;
        }

        // Scan for credentials
        var scanResult = CredentialScanner.Scan(messageContent);

        if (!scanResult.HasCredentials)
        {
            return ValueTask.CompletedTask;
        }

        // Credentials detected
        LogCredentialsDetected(_logger, toolName, scanResult.Summary ?? "Unknown credentials");

        if (_blockOnDetection)
        {
            throw new InvalidOperationException(
                $"Message blocked: Credentials detected in outbound message. " +
                $"The message appears to contain sensitive data ({scanResult.Summary}). " +
                $"Please remove any API keys, tokens, passwords, or other secrets before sending.");
        }

        // Just warn, don't block
        LogCredentialsWarning(_logger, toolName, scanResult.Summary ?? "Unknown credentials");
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

    private static string? GetMessageContent(HookContext<CallToolRequestParams> context)
    {
        var args = context.Request.Arguments;
        if (args is null)
        {
            return null;
        }

        // Try common parameter names for message content
        string[] contentParams = ["content", "message", "body", "text", "messageContent", "chatMessage"];

        foreach (var param in contentParams)
        {
            if (args.TryGetValue(param, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credentials detected in {ToolName}, blocking message: {Details}")]
    private static partial void LogCredentialsDetected(ILogger logger, string toolName, string details);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credentials detected in {ToolName} (warning only): {Details}")]
    private static partial void LogCredentialsWarning(ILogger logger, string toolName, string details);
}
