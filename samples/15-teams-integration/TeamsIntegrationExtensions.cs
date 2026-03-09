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
    /// Adds Teams integration features to the MCP Proxy builder.
    /// Includes caching, credential scanning, pagination, and virtual tools.
    /// </summary>
    /// <param name="builder">The MCP Proxy builder.</param>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder WithTeamsIntegration(
        this IMcpProxyBuilder builder,
        IServiceProvider serviceProvider,
        Action<TeamsIntegrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var options = new TeamsIntegrationOptions();
        configure?.Invoke(options);

        // Create cache service
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var cacheLogger = loggerFactory?.CreateLogger<TeamsCacheService>();

        var cacheService = new TeamsCacheService(
            options.CacheFilePath,
            options.CacheTtl,
            options.MaxRecentContacts,
            cacheLogger);

        // Register hooks
        if (options.EnableAutoPagination)
        {
            var paginationLogger = loggerFactory?.CreateLogger<TeamsPaginationHook>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsPaginationHook>.Instance;
            builder.WithGlobalPreInvokeHook(new TeamsPaginationHook(paginationLogger, options.DefaultPaginationLimit));
        }

        if (options.EnableCredentialScanning)
        {
            var credentialLogger = loggerFactory?.CreateLogger<TeamsCredentialScanHook>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsCredentialScanHook>.Instance;
            builder.WithGlobalPreInvokeHook(new TeamsCredentialScanHook(credentialLogger, options.BlockCredentials));
        }

        if (options.EnableMessagePrefix)
        {
            var prefixLogger = loggerFactory?.CreateLogger<TeamsMessagePrefixHook>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsMessagePrefixHook>.Instance;
            builder.WithGlobalPreInvokeHook(new TeamsMessagePrefixHook(prefixLogger, options.MessagePrefix));
        }

        if (options.EnableCachePopulation)
        {
            var populateLogger = loggerFactory?.CreateLogger<TeamsCachePopulateHook>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsCachePopulateHook>.Instance;
            builder.WithGlobalPostInvokeHook(new TeamsCachePopulateHook(populateLogger, cacheService, options.AutoSaveCache));
        }

        // Register interceptor for cache short-circuit
        if (options.EnableCacheShortCircuit)
        {
            var interceptorLogger = loggerFactory?.CreateLogger<TeamsCacheInterceptor>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TeamsCacheInterceptor>.Instance;
            builder.WithToolCallInterceptor(new TeamsCacheInterceptor(interceptorLogger, cacheService));
        }

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

    /// <summary>
    /// Adds Teams integration services to the service collection.
    /// Use this when you need to access the cache service via DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTeamsIntegration(
        this IServiceCollection services,
        Action<TeamsIntegrationOptions>? configure = null)
    {
        var options = new TeamsIntegrationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ITeamsCacheService>(sp =>
        {
            var logger = sp.GetService<ILogger<TeamsCacheService>>();
            return new TeamsCacheService(
                options.CacheFilePath,
                options.CacheTtl,
                options.MaxRecentContacts,
                logger);
        });

        return services;
    }
}
