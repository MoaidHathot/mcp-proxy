using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using McpProxy.Core.Hooks.BuiltIn;
using Microsoft.Extensions.Logging;

namespace McpProxy.Core.Hooks;

/// <summary>
/// Factory for creating hook instances from configuration definitions.
/// </summary>
public sealed class HookFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, Func<HookDefinition, ILoggerFactory, object>> _hookCreators;

    /// <summary>
    /// Initializes a new instance of <see cref="HookFactory"/>.
    /// </summary>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    public HookFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _hookCreators = new Dictionary<string, Func<HookDefinition, ILoggerFactory, object>>(StringComparer.OrdinalIgnoreCase)
        {
            ["logging"] = CreateLoggingHook,
            ["inputTransform"] = CreateInputTransformHook,
            ["outputTransform"] = CreateOutputTransformHook
        };
    }

    /// <summary>
    /// Registers a custom hook creator for a specific hook type.
    /// </summary>
    /// <param name="typeName">The hook type name.</param>
    /// <param name="creator">The factory function to create the hook.</param>
    public void RegisterHookType(string typeName, Func<HookDefinition, ILoggerFactory, object> creator)
    {
        _hookCreators[typeName] = creator;
    }

    /// <summary>
    /// Creates a pre-invoke hook from a definition.
    /// </summary>
    /// <param name="definition">The hook definition.</param>
    /// <returns>The created hook, or null if the hook type is not supported for pre-invoke.</returns>
    public IPreInvokeHook? CreatePreInvokeHook(HookDefinition definition)
    {
        var hook = CreateHook(definition);
        return hook as IPreInvokeHook;
    }

    /// <summary>
    /// Creates a post-invoke hook from a definition.
    /// </summary>
    /// <param name="definition">The hook definition.</param>
    /// <returns>The created hook, or null if the hook type is not supported for post-invoke.</returns>
    public IPostInvokeHook? CreatePostInvokeHook(HookDefinition definition)
    {
        var hook = CreateHook(definition);
        return hook as IPostInvokeHook;
    }

    /// <summary>
    /// Creates hooks from a configuration and adds them to a pipeline.
    /// </summary>
    /// <param name="configuration">The hooks configuration.</param>
    /// <param name="pipeline">The pipeline to add hooks to.</param>
    public void ConfigurePipeline(HooksConfiguration configuration, HookPipeline pipeline)
    {
        if (configuration.PreInvoke is not null)
        {
            foreach (var definition in configuration.PreInvoke)
            {
                var hook = CreatePreInvokeHook(definition);
                if (hook is not null)
                {
                    pipeline.AddPreInvokeHook(hook);
                }
            }
        }

        if (configuration.PostInvoke is not null)
        {
            foreach (var definition in configuration.PostInvoke)
            {
                var hook = CreatePostInvokeHook(definition);
                if (hook is not null)
                {
                    pipeline.AddPostInvokeHook(hook);
                }
            }
        }
    }

    /// <summary>
    /// Creates a hook instance from a definition.
    /// </summary>
    /// <param name="definition">The hook definition.</param>
    /// <returns>The created hook object.</returns>
    /// <exception cref="ArgumentException">Thrown when the hook type is not recognized.</exception>
    public object CreateHook(HookDefinition definition)
    {
        if (!_hookCreators.TryGetValue(definition.Type, out var creator))
        {
            throw new ArgumentException($"Unknown hook type: '{definition.Type}'. Supported types: {string.Join(", ", _hookCreators.Keys)}", nameof(definition));
        }

        return creator(definition, _loggerFactory);
    }

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateLoggingHook(HookDefinition definition, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<LoggingHook>();
        var level = LogLevel.Information;
        var logArguments = false;
        var logResult = false;

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<string>(definition.Config, "logLevel", out var levelStr))
            {
                level = Enum.TryParse<LogLevel>(levelStr, ignoreCase: true, out var parsed) ? parsed : LogLevel.Information;
            }

            if (TryGetConfigValue<bool>(definition.Config, "logArguments", out var logArgs))
            {
                logArguments = logArgs;
            }

            if (TryGetConfigValue<bool>(definition.Config, "logResult", out var logRes))
            {
                logResult = logRes;
            }
        }

        return new LoggingHook(logger, level, logArguments, logResult);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateInputTransformHook(HookDefinition definition, ILoggerFactory _)
    {
        Dictionary<string, object?>? defaultValues = null;

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<Dictionary<string, object?>>(definition.Config, "defaults", out var defaults))
            {
                defaultValues = defaults;
            }
        }

        // Note: Transformations (functions) cannot be configured via JSON, 
        // so InputTransformHook via config only supports default values
        return new InputTransformHook(defaultValues: defaultValues);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateOutputTransformHook(HookDefinition definition, ILoggerFactory _)
    {
        IEnumerable<string>? redactPatterns = null;
        var redactedValue = "[REDACTED]";

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<string[]>(definition.Config, "redactPatterns", out var patterns))
            {
                redactPatterns = patterns;
            }

            if (TryGetConfigValue<string>(definition.Config, "redactedValue", out var redacted))
            {
                redactedValue = redacted;
            }
        }

        return new OutputTransformHook(redactPatterns, redactedValue);
    }
    #pragma warning restore CA1859

    private static bool TryGetConfigValue<T>(Dictionary<string, object?> config, string key, out T value)
    {
        value = default!;

        if (!config.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return false;
        }

        // Handle JsonElement from deserialization
        if (rawValue is JsonElement element)
        {
            try
            {
                var deserialized = element.Deserialize<T>();
                if (deserialized is not null)
                {
                    value = deserialized;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // Handle direct type matches
        if (rawValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        // Handle string conversions for primitives
        if (typeof(T) == typeof(bool) && rawValue is string boolStr)
        {
            if (bool.TryParse(boolStr, out var boolResult))
            {
                value = (T)(object)boolResult;
                return true;
            }
        }

        return false;
    }
}
