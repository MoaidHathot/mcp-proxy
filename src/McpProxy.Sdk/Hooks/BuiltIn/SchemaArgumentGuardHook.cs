using System.Text.Json;
using McpProxy.Abstractions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Sdk.Hooks.BuiltIn;

/// <summary>
/// Configuration for <see cref="SchemaArgumentGuardHook"/>.
/// </summary>
public sealed class SchemaArgumentGuardConfiguration
{
    /// <summary>
    /// When <see langword="true"/> (default), arguments not declared by the target tool's input
    /// schema are removed before the call is forwarded. When <see langword="false"/>, undeclared
    /// arguments are only logged (left in place).
    /// </summary>
    public bool StripUnknownArguments { get; set; } = true;
}

/// <summary>
/// A pre-invoke hook that removes request arguments the target tool does not declare in its
/// <c>inputSchema</c>, preventing backends from rejecting the entire call with errors such as
/// <c>Unknown argument: 'top'</c>.
/// </summary>
/// <remarks>
/// <para>
/// Requires <see cref="HookContext{TRequest}.ToolInputSchema"/> to be populated; it no-ops when the
/// schema is unavailable. It also no-ops when the schema explicitly permits additional properties
/// (<c>additionalProperties: true</c> or an <c>additionalProperties</c> schema object), since such a
/// tool opts into accepting arguments beyond its declared <c>properties</c>.
/// </para>
/// <para>
/// Note: unlike the permissive JSON Schema default, a schema that declares <c>properties</c> but
/// omits <c>additionalProperties</c> is treated as strict here, because MCP backends commonly reject
/// undeclared arguments. Enable this guard only for backends with that behavior.
/// </para>
/// <para>
/// Runs with a high priority so it executes after argument-injecting hooks (e.g. pagination or
/// message defaults), cleaning up anything they — or the caller — added that the tool will not accept.
/// </para>
/// </remarks>
public sealed partial class SchemaArgumentGuardHook : IPreInvokeHook
{
    private readonly ILogger _logger;
    private readonly SchemaArgumentGuardConfiguration _config;

    /// <summary>
    /// Initializes a new instance of <see cref="SchemaArgumentGuardHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">Optional configuration. Defaults to stripping undeclared arguments.</param>
    public SchemaArgumentGuardHook(ILogger logger, SchemaArgumentGuardConfiguration? config = null)
    {
        _logger = logger;
        _config = config ?? new SchemaArgumentGuardConfiguration();
    }

    /// <inheritdoc />
    public int Priority => 1000; // Run last, after argument-injecting hooks.

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var args = context.Request.Arguments;
        if (context.ToolInputSchema is not { } schema || args is null || args.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        if (!TryGetStrictDeclaredProperties(schema, out var declared))
        {
            // Schema is unreadable, has no `properties`, or opts into additional properties.
            return ValueTask.CompletedTask;
        }

        List<string>? unknown = null;
        foreach (var name in args.Keys)
        {
            if (!declared.Contains(name))
            {
                (unknown ??= []).Add(name);
            }
        }

        if (unknown is null)
        {
            return ValueTask.CompletedTask;
        }

        foreach (var name in unknown)
        {
            LogUnknownArgument(_logger, name, context.ToolName);
        }

        if (_config.StripUnknownArguments)
        {
            var cleaned = new Dictionary<string, JsonElement>(args.Count);
            foreach (var kvp in args)
            {
                if (declared.Contains(kvp.Key))
                {
                    cleaned[kvp.Key] = kvp.Value;
                }
            }

            context.Request = new CallToolRequestParams
            {
                Name = context.Request.Name,
                Arguments = cleaned
            };

            LogStrippedArguments(_logger, unknown.Count, context.ToolName);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Reads the schema's declared property names. Returns <see langword="false"/> (caller should not
    /// strip) when the schema is not a usable object schema or when it permits additional properties.
    /// </summary>
    private static bool TryGetStrictDeclaredProperties(JsonElement schema, out HashSet<string> declared)
    {
        declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // additionalProperties: true / {schema object} -> the tool accepts extras; do not strip.
        if (schema.TryGetProperty("additionalProperties", out var additional) &&
            additional.ValueKind is JsonValueKind.True or JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in properties.EnumerateObject())
        {
            declared.Add(property.Name);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Argument '{Argument}' is not declared by the '{ToolName}' tool input schema")]
    private static partial void LogUnknownArgument(ILogger logger, string argument, string toolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stripped {Count} undeclared argument(s) from '{ToolName}' before forwarding")]
    private static partial void LogStrippedArguments(ILogger logger, int count, string toolName);
}
