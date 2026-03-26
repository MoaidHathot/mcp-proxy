using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration.Cache;
using McpProxy.Samples.TeamsIntegration.Hooks;
using McpProxy.Samples.TeamsIntegration.Interceptors;
using McpProxy.Samples.TeamsIntegration.VirtualTools;

namespace McpProxy.Samples.TeamsIntegration;

/// <summary>
/// Extension methods for configuring Teams integration with MCP Proxy.
/// </summary>
public static class TeamsIntegrationExtensions
{
    /// <summary>
    /// Adds Teams integration services to the service collection and returns the shared
    /// <see cref="TeamsIntegrationContext"/> so it can be passed to <see cref="WithTeamsIntegration"/>.
    /// This ensures all hooks, interceptors, virtual tools, and DI use the same cache instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The shared integration context containing the cache and options.</returns>
    public static TeamsIntegrationContext AddTeamsIntegration(
        this IServiceCollection services,
        Action<TeamsIntegrationOptions>? configure = null)
    {
        var options = new TeamsIntegrationOptions();
        configure?.Invoke(options);

        // Create the single cache instance eagerly so it can be shared with the proxy builder
        var cacheService = new TeamsCacheService(
            options.CacheFilePath,
            options.CacheTtl,
            options.MaxRecentContacts,
            logger: null); // Logger will be set after DI is built

        var context = new TeamsIntegrationContext(options, cacheService);

        services.AddSingleton(options);
        services.AddSingleton<ITeamsCacheService>(cacheService);
        services.AddSingleton(context);

        return context;
    }

    /// <summary>
    /// Adds Teams integration features to the MCP Proxy builder using the shared context
    /// from <see cref="AddTeamsIntegration"/>. This ensures the same cache instance is used
    /// by DI, hooks, interceptors, and virtual tools.
    /// </summary>
    /// <param name="builder">The MCP Proxy builder.</param>
    /// <param name="context">The shared integration context from <see cref="AddTeamsIntegration"/>.</param>
    /// <param name="configure">Optional configuration override. If provided, overrides the context options.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder WithTeamsIntegration(
        this IMcpProxyBuilder builder,
        TeamsIntegrationContext context,
        Action<TeamsIntegrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;

        // Allow overriding options for the proxy builder (e.g., toggling features)
        configure?.Invoke(options);

        var cacheService = context.CacheService;

        // Register hooks
        if (options.EnableAutoPagination)
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsPaginationHook>.Instance;
            builder.WithGlobalPreInvokeHook(new TeamsPaginationHook(logger, options.DefaultPaginationLimit));
        }

        if (options.EnableCredentialScanning)
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsCredentialScanHook>.Instance;
            builder.WithGlobalPreInvokeHook(new TeamsCredentialScanHook(logger, options.BlockCredentials));
        }

        if (options.EnableMessagePrefix)
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsMessagePrefixHook>.Instance;
            builder.WithGlobalPreInvokeHook(new TeamsMessagePrefixHook(logger, options.MessagePrefix));
        }

        if (options.EnableMessageDefaults)
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsMessageDefaultsHook>.Instance;
            builder.WithGlobalPreInvokeHook(new TeamsMessageDefaultsHook(logger, options.DefaultContentType));
        }

        if (options.EnableCachePopulation)
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsCachePopulateHook>.Instance;
            builder.WithGlobalPostInvokeHook(new TeamsCachePopulateHook(logger, cacheService, options.AutoSaveCache));
        }

        // Register interceptor for cache short-circuit
        if (options.EnableCacheShortCircuit)
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsCacheInterceptor>.Instance;
            builder.WithToolCallInterceptor(new TeamsCacheInterceptor(logger, cacheService));
        }

        // Register tool description modifier
        builder.WithToolInterceptor(new TeamsToolDescriptionInterceptor(options));

        // Register virtual tools
        if (options.RegisterVirtualTools)
        {
            var virtualTools = new TeamsVirtualTools(cacheService);
            foreach (var (tool, handler) in virtualTools.GetTools())
            {
                builder.AddVirtualTool(tool, handler);
            }
        }

        return builder;
    }
}

/// <summary>
/// Shared context between <see cref="TeamsIntegrationExtensions.AddTeamsIntegration"/> and
/// <see cref="TeamsIntegrationExtensions.WithTeamsIntegration"/>. Ensures the same cache
/// instance is used everywhere.
/// </summary>
public sealed class TeamsIntegrationContext
{
    /// <summary>
    /// Gets the Teams integration options.
    /// </summary>
    public TeamsIntegrationOptions Options { get; }

    /// <summary>
    /// Gets the shared cache service instance.
    /// </summary>
    public ITeamsCacheService CacheService { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public TeamsIntegrationContext(TeamsIntegrationOptions options, ITeamsCacheService cacheService)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        CacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }
}
