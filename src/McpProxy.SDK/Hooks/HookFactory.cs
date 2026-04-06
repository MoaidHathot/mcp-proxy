using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using McpProxy.SDK.Debugging;
using McpProxy.SDK.Hooks.BuiltIn;
using McpProxy.SDK.Telemetry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace McpProxy.SDK.Hooks;

/// <summary>
/// Factory for creating hook instances from configuration definitions.
/// </summary>
public sealed class HookFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMemoryCache? _memoryCache;
    private readonly ProxyMetrics? _metrics;
    private readonly IRequestDumper? _requestDumper;
    private readonly Dictionary<string, Func<HookDefinition, HookFactory, object>> _hookCreators;

    /// <summary>
    /// Initializes a new instance of <see cref="HookFactory"/>.
    /// </summary>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    /// <param name="memoryCache">Optional memory cache for rate limiting.</param>
    /// <param name="metrics">Optional proxy metrics for telemetry.</param>
    /// <param name="requestDumper">Optional request dumper for debugging.</param>
    public HookFactory(
        ILoggerFactory loggerFactory,
        IMemoryCache? memoryCache = null,
        ProxyMetrics? metrics = null,
        IRequestDumper? requestDumper = null)
    {
        _loggerFactory = loggerFactory;
        _memoryCache = memoryCache;
        _metrics = metrics;
        _requestDumper = requestDumper;
        _hookCreators = new Dictionary<string, Func<HookDefinition, HookFactory, object>>(StringComparer.OrdinalIgnoreCase)
        {
            ["logging"] = CreateLoggingHook,
            ["inputTransform"] = CreateInputTransformHook,
            ["outputTransform"] = CreateOutputTransformHook,
            ["rateLimit"] = CreateRateLimitingHook,
            ["timeout"] = CreateTimeoutHook,
            ["authorization"] = CreateAuthorizationHook,
            ["retry"] = CreateRetryHook,
            ["metrics"] = CreateMetricsHook,
            ["audit"] = CreateAuditHook,
            ["contentFilter"] = CreateContentFilterHook,
            ["dump"] = CreateDumpHook
        };
    }

    /// <summary>
    /// Registers a custom hook creator for a specific hook type.
    /// </summary>
    /// <param name="typeName">The hook type name.</param>
    /// <param name="creator">The factory function to create the hook.</param>
    public void RegisterHookType(string typeName, Func<HookDefinition, HookFactory, object> creator)
    {
        _hookCreators[typeName] = creator;
    }

    /// <summary>
    /// Gets the logger factory.
    /// </summary>
    internal ILoggerFactory LoggerFactory => _loggerFactory;

    /// <summary>
    /// Gets the memory cache.
    /// </summary>
    internal IMemoryCache? MemoryCache => _memoryCache;

    /// <summary>
    /// Gets the proxy metrics.
    /// </summary>
    internal ProxyMetrics? Metrics => _metrics;

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

        return creator(definition, this);
    }

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateLoggingHook(HookDefinition definition, HookFactory factory)
    {
        var logger = factory._loggerFactory.CreateLogger<LoggingHook>();
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
    private static object CreateInputTransformHook(HookDefinition definition, HookFactory _)
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
    private static object CreateOutputTransformHook(HookDefinition definition, HookFactory _)
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

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateRateLimitingHook(HookDefinition definition, HookFactory factory)
    {
        if (factory._memoryCache is null)
        {
            throw new InvalidOperationException("RateLimitingHook requires IMemoryCache. Ensure memory caching is configured.");
        }

        var logger = factory._loggerFactory.CreateLogger<RateLimitingHook>();
        var config = new RateLimitingConfiguration();

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<int>(definition.Config, "maxRequests", out var maxRequests))
            {
                config.MaxRequests = maxRequests;
            }

            if (TryGetConfigValue<int>(definition.Config, "windowSeconds", out var windowSeconds))
            {
                config.WindowSeconds = windowSeconds;
            }

            if (TryGetConfigValue<string>(definition.Config, "keyType", out var keyTypeStr) &&
                Enum.TryParse<RateLimitKeyType>(keyTypeStr, ignoreCase: true, out var keyType))
            {
                config.KeyType = keyType;
            }

            if (TryGetConfigValue<string>(definition.Config, "errorMessage", out var errorMessage))
            {
                config.ErrorMessage = errorMessage;
            }
        }

        return new RateLimitingHook(factory._memoryCache, logger, config, factory._metrics);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateTimeoutHook(HookDefinition definition, HookFactory factory)
    {
        var logger = factory._loggerFactory.CreateLogger<TimeoutHook>();
        var config = new TimeoutConfiguration();

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<int>(definition.Config, "defaultTimeoutSeconds", out var defaultTimeout))
            {
                config.DefaultTimeoutSeconds = defaultTimeout;
            }

            if (TryGetConfigValue<Dictionary<string, int>>(definition.Config, "perTool", out var perTool))
            {
                config.PerTool = perTool;
            }
        }

        return new TimeoutHook(logger, config);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateAuthorizationHook(HookDefinition definition, HookFactory factory)
    {
        var logger = factory._loggerFactory.CreateLogger<AuthorizationHook>();
        var config = new AuthorizationConfiguration();

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<bool>(definition.Config, "defaultAllow", out var defaultAllow))
            {
                config.DefaultAllow = defaultAllow;
            }

            if (TryGetConfigValue<string>(definition.Config, "mode", out var modeStr) &&
                Enum.TryParse<AuthorizationMode>(modeStr, ignoreCase: true, out var mode))
            {
                config.Mode = mode;
            }

            if (TryGetConfigValue<bool>(definition.Config, "requireAuthentication", out var requireAuth))
            {
                config.RequireAuthentication = requireAuth;
            }

            if (TryGetConfigValue<string>(definition.Config, "deniedMessage", out var deniedMessage))
            {
                config.DeniedMessage = deniedMessage;
            }

            if (TryGetConfigValue<List<AuthorizationRule>>(definition.Config, "rules", out var rules))
            {
                config.Rules = rules;
            }
        }

        return new AuthorizationHook(logger, config, factory._metrics);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateRetryHook(HookDefinition definition, HookFactory factory)
    {
        var logger = factory._loggerFactory.CreateLogger<RetryHook>();
        var config = new RetryConfiguration();

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<int>(definition.Config, "maxRetries", out var maxRetries))
            {
                config.MaxRetries = maxRetries;
            }

            if (TryGetConfigValue<int>(definition.Config, "initialDelayMs", out var initialDelay))
            {
                config.InitialDelayMs = initialDelay;
            }

            if (TryGetConfigValue<int>(definition.Config, "maxDelayMs", out var maxDelay))
            {
                config.MaxDelayMs = maxDelay;
            }

            if (TryGetConfigValue<double>(definition.Config, "backoffMultiplier", out var multiplier))
            {
                config.BackoffMultiplier = multiplier;
            }

            if (TryGetConfigValue<bool>(definition.Config, "useJitter", out var useJitter))
            {
                config.UseJitter = useJitter;
            }

            if (TryGetConfigValue<List<string>>(definition.Config, "retryablePatterns", out var retryable))
            {
                config.RetryablePatterns = retryable;
            }

            if (TryGetConfigValue<List<string>>(definition.Config, "nonRetryablePatterns", out var nonRetryable))
            {
                config.NonRetryablePatterns = nonRetryable;
            }
        }

        return new RetryHook(logger, config, factory._metrics);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateMetricsHook(HookDefinition definition, HookFactory factory)
    {
        if (factory._metrics is null)
        {
            throw new InvalidOperationException("MetricsHook requires ProxyMetrics. Ensure metrics are configured.");
        }

        var logger = factory._loggerFactory.CreateLogger<MetricsHook>();
        var config = new MetricsHookConfiguration();

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<bool>(definition.Config, "recordTiming", out var recordTiming))
            {
                config.RecordTiming = recordTiming;
            }

            if (TryGetConfigValue<bool>(definition.Config, "recordSizes", out var recordSizes))
            {
                config.RecordSizes = recordSizes;
            }

            if (TryGetConfigValue<bool>(definition.Config, "includeArguments", out var includeArgs))
            {
                config.IncludeArguments = includeArgs;
            }

            if (TryGetConfigValue<bool>(definition.Config, "includePrincipalId", out var includePrincipal))
            {
                config.IncludePrincipalId = includePrincipal;
            }

            if (TryGetConfigValue<Dictionary<string, string>>(definition.Config, "customTags", out var customTags))
            {
                config.CustomTags = customTags;
            }
        }

        return new MetricsHook(logger, factory._metrics, config);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateAuditHook(HookDefinition definition, HookFactory factory)
    {
        var logger = factory._loggerFactory.CreateLogger<AuditHook>();
        var config = new AuditConfiguration();

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<string>(definition.Config, "level", out var levelStr) &&
                Enum.TryParse<AuditLevel>(levelStr, ignoreCase: true, out var level))
            {
                config.Level = level;
            }

            if (TryGetConfigValue<bool>(definition.Config, "includeSensitiveData", out var includeSensitive))
            {
                config.IncludeSensitiveData = includeSensitive;
            }

            if (TryGetConfigValue<List<string>>(definition.Config, "sensitiveArguments", out var sensitiveArgs))
            {
                config.SensitiveArguments = sensitiveArgs;
            }

            if (TryGetConfigValue<bool>(definition.Config, "includeCorrelationId", out var includeCorrId))
            {
                config.IncludeCorrelationId = includeCorrId;
            }

            if (TryGetConfigValue<int>(definition.Config, "maxValueLength", out var maxValueLen))
            {
                config.MaxValueLength = maxValueLen;
            }

            if (TryGetConfigValue<List<string>>(definition.Config, "excludeTools", out var excludeTools))
            {
                config.ExcludeTools = excludeTools;
            }

            if (TryGetConfigValue<List<string>>(definition.Config, "includeTools", out var includeTools))
            {
                config.IncludeTools = includeTools;
            }
        }

        return new AuditHook(logger, config);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateContentFilterHook(HookDefinition definition, HookFactory factory)
    {
        var logger = factory._loggerFactory.CreateLogger<ContentFilterHook>();
        var config = new ContentFilterConfiguration();

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<bool>(definition.Config, "useDefaultPatterns", out var useDefaults))
            {
                config.UseDefaultPatterns = useDefaults;
            }

            if (TryGetConfigValue<bool>(definition.Config, "scanRequests", out var scanRequests))
            {
                config.ScanRequests = scanRequests;
            }

            if (TryGetConfigValue<List<ContentFilterPattern>>(definition.Config, "patterns", out var patterns))
            {
                config.Patterns = patterns;
            }
        }

        return new ContentFilterHook(logger, config, factory._metrics);
    }
    #pragma warning restore CA1859

    #pragma warning disable CA1859 // Use concrete types when possible for improved performance - required for dictionary storage
    private static object CreateDumpHook(HookDefinition definition, HookFactory factory)
    {
        var dumper = factory._requestDumper;
        if (dumper is null)
        {
            throw new InvalidOperationException("DumpHook requires IRequestDumper. Ensure request dumping is configured in debug settings.");
        }

        var dumpRequests = true;
        var dumpResponses = true;

        if (definition.Config is not null)
        {
            if (TryGetConfigValue<bool>(definition.Config, "dumpRequests", out var dumpReq))
            {
                dumpRequests = dumpReq;
            }

            if (TryGetConfigValue<bool>(definition.Config, "dumpResponses", out var dumpResp))
            {
                dumpResponses = dumpResp;
            }
        }

        return new DumpHook(dumper, dumpRequests, dumpResponses);
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

        if (typeof(T) == typeof(int) && rawValue is string intStr)
        {
            if (int.TryParse(intStr, out var intResult))
            {
                value = (T)(object)intResult;
                return true;
            }
        }

        if (typeof(T) == typeof(double) && rawValue is string doubleStr)
        {
            if (double.TryParse(doubleStr, out var doubleResult))
            {
                value = (T)(object)doubleResult;
                return true;
            }
        }

        return false;
    }
}
