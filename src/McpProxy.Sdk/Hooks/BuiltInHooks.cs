using System.Text.Json;
using System.Text.RegularExpressions;
using McpProxy.Abstractions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Sdk.Hooks.BuiltIn;

/// <summary>
/// A hook that logs tool invocations.
/// </summary>
public sealed class LoggingHook : IToolHook
{
    private readonly ILogger _logger;
    private readonly LogLevel _level;
    private readonly bool _logArguments;
    private readonly bool _logResult;

    /// <summary>
    /// Initializes a new instance of <see cref="LoggingHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="level">The log level to use.</param>
    /// <param name="logArguments">Whether to log tool arguments.</param>
    /// <param name="logResult">Whether to log tool results.</param>
    public LoggingHook(ILogger logger, LogLevel level = LogLevel.Information, bool logArguments = false, bool logResult = false)
    {
        _logger = logger;
        _level = level;
        _logArguments = logArguments;
        _logResult = logResult;
    }

    /// <inheritdoc />
    public int Priority => -1000; // Execute first

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        if (_logArguments && context.Request.Arguments is not null)
        {
            if (_logger.IsEnabled(_level))
            {
                _logger.Log(_level, "Invoking tool '{ToolName}' on server '{ServerName}' with arguments: {Arguments}",
                    context.ToolName, context.ServerName, JsonSerializer.Serialize(context.Request.Arguments));
            }
        }
        else
        {
            if (_logger.IsEnabled(_level))
            {
                _logger.Log(_level, "Invoking tool '{ToolName}' on server '{ServerName}'",
                    context.ToolName, context.ServerName);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        if (_logResult)
        {
            if (_logger.IsEnabled(_level))
            {
                _logger.Log(_level, "Tool '{ToolName}' completed with {ContentCount} content items",
                    context.ToolName, result.Content.Count);
            }
        }
        else
        {
            if (_logger.IsEnabled(_level))
            {
                _logger.Log(_level, "Tool '{ToolName}' completed", context.ToolName);
            }
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// A hook that transforms input arguments before tool invocation.
/// </summary>
public sealed class InputTransformHook : IPreInvokeHook
{
    private readonly Dictionary<string, Func<object?, object?>> _transformations;
    private readonly Dictionary<string, object?> _defaultValues;

    /// <summary>
    /// Initializes a new instance of <see cref="InputTransformHook"/>.
    /// </summary>
    /// <param name="transformations">Argument transformations keyed by argument name.</param>
    /// <param name="defaultValues">Default values to set for missing arguments.</param>
    public InputTransformHook(
        Dictionary<string, Func<object?, object?>>? transformations = null,
        Dictionary<string, object?>? defaultValues = null)
    {
        _transformations = transformations ?? [];
        _defaultValues = defaultValues ?? [];
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        if (context.Request.Arguments is null)
        {
            return ValueTask.CompletedTask;
        }

        var args = new Dictionary<string, JsonElement>(context.Request.Arguments);

        // Apply default values for missing arguments
        foreach (var (key, value) in _defaultValues)
        {
            if (!args.ContainsKey(key) && value is not null)
            {
                args[key] = JsonSerializer.SerializeToElement(value);
            }
        }

        // Apply transformations
        foreach (var (key, transform) in _transformations)
        {
            if (args.TryGetValue(key, out var currentValue))
            {
                var transformed = transform(currentValue);
                if (transformed is not null)
                {
                    args[key] = JsonSerializer.SerializeToElement(transformed);
                }
            }
        }

        // Update the request with new arguments
        context.Request = new CallToolRequestParams
        {
            Name = context.Request.Name,
            Arguments = args
        };

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A hook that transforms or redacts output content after tool invocation.
/// </summary>
public sealed class OutputTransformHook : IPostInvokeHook
{
    private readonly HashSet<string> _redactPatterns;
    private readonly string _redactedValue;

    /// <summary>
    /// Initializes a new instance of <see cref="OutputTransformHook"/>.
    /// </summary>
    /// <param name="redactPatterns">Patterns to redact from output (e.g., "password", "secret").</param>
    /// <param name="redactedValue">The value to replace redacted content with.</param>
    public OutputTransformHook(
        IEnumerable<string>? redactPatterns = null,
        string redactedValue = "[REDACTED]")
    {
        _redactPatterns = new HashSet<string>(redactPatterns ?? [], StringComparer.OrdinalIgnoreCase);
        _redactedValue = redactedValue;
    }

    /// <inheritdoc />
    public int Priority => 1000; // Execute last

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        if (_redactPatterns.Count == 0)
        {
            return ValueTask.FromResult(result);
        }

        var newContent = new List<ContentBlock>();
        var modified = false;

        foreach (var content in result.Content)
        {
            if (content is TextContentBlock textContent && textContent.Text is not null)
            {
                var newText = RedactSensitiveContent(textContent.Text);
                if (newText != textContent.Text)
                {
                    modified = true;
                    newContent.Add(new TextContentBlock { Text = newText });
                }
                else
                {
                    newContent.Add(content);
                }
            }
            else
            {
                newContent.Add(content);
            }
        }

        if (modified)
        {
            return ValueTask.FromResult(new CallToolResult { Content = newContent, IsError = result.IsError });
        }

        return ValueTask.FromResult(result);
    }

    private string RedactSensitiveContent(string text)
    {
        foreach (var pattern in _redactPatterns)
        {
            // Simple pattern matching for JSON-like content
            // Matches patterns like: "key": "value" or "key":"value"
            var jsonPattern = $@"""{pattern}""\s*:\s*""[^""]*""";
            text = Regex.Replace(
                text,
                jsonPattern,
                $@"""{pattern}"": ""{_redactedValue}""",
                RegexOptions.IgnoreCase);
        }

        return text;
    }
}
