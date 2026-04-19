using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace McpProxy.Sdk.Authentication;

/// <summary>
/// Extension methods that wire up CORS for MCP Proxy HTTP endpoints.
/// </summary>
/// <remarks>
/// MCP Streamable HTTP and the legacy SSE endpoints are typically consumed by
/// browser-based clients such as MCP Inspector. Without CORS, the browser
/// blocks all preflight (OPTIONS) requests with 405 Method Not Allowed and
/// the connection fails before any MCP traffic is exchanged.
/// <para>
/// This extension registers a default policy from
/// <see cref="ProxySettings.Cors"/> plus one named policy per server that
/// declares its own <see cref="ServerConfiguration.Cors"/> override. Per-server
/// policies are applied to that server's route via
/// <see cref="CorsEndpointConventionBuilderExtensions.RequireCors(Microsoft.AspNetCore.Builder.IEndpointConventionBuilder, string)"/>.
/// </para>
/// </remarks>
public static class CorsExtensions
{
    /// <summary>
    /// Default CORS policy name applied to all MCP routes that do not declare
    /// a per-server override.
    /// </summary>
    public const string DefaultPolicyName = "McpProxyDefaultCors";

    /// <summary>
    /// Builds a CORS policy name for a per-server override.
    /// </summary>
    /// <param name="serverName">The server name from configuration.</param>
    /// <returns>A unique policy name for the server.</returns>
    public static string GetServerPolicyName(string serverName) =>
        $"McpProxyCors_{serverName}";

    /// <summary>
    /// Registers ASP.NET Core CORS services and configures one policy per
    /// MCP server (plus a default policy from <see cref="ProxySettings.Cors"/>).
    /// </summary>
    /// <remarks>
    /// Call this during service registration, before <c>builder.Build()</c>.
    /// If <see cref="ProxySettings.Cors"/> and all per-server overrides are
    /// disabled, this method is a no-op.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The proxy configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMcpProxyCors(
        this IServiceCollection services,
        ProxyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var globalCors = configuration.Proxy.Cors;
        var serverOverrides = configuration.Mcp
            .Where(kvp => kvp.Value.Enabled && kvp.Value.Cors is { Enabled: true })
            .ToList();

        if (!globalCors.Enabled && serverOverrides.Count == 0)
        {
            return services;
        }

        services.AddCors(options =>
        {
            if (globalCors.Enabled)
            {
                options.AddPolicy(DefaultPolicyName, policy => ConfigurePolicy(policy, globalCors));
                options.DefaultPolicyName = DefaultPolicyName;
            }

            foreach (var (serverName, serverConfig) in serverOverrides)
            {
                options.AddPolicy(
                    GetServerPolicyName(serverName),
                    policy => ConfigurePolicy(policy, serverConfig.Cors!));
            }
        });

        return services;
    }

    /// <summary>
    /// Inserts the CORS middleware into the pipeline using the configured
    /// default policy. Per-server policies are applied at endpoint mapping
    /// time via <see cref="ApplyServerCorsPolicy"/>.
    /// </summary>
    /// <remarks>
    /// CORS must be added <em>before</em> authentication and routing so that
    /// preflight (OPTIONS) requests succeed without an auth challenge.
    /// </remarks>
    /// <param name="app">The web application.</param>
    /// <returns>The application for chaining.</returns>
    public static WebApplication UseMcpProxyCors(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var configuration = app.Services.GetRequiredService<ProxyConfiguration>();
        var globalCors = configuration.Proxy.Cors;

        var anyServerOverrides = configuration.Mcp.Values
            .Any(s => s.Enabled && s.Cors is { Enabled: true });

        if (!globalCors.Enabled && !anyServerOverrides)
        {
            return app;
        }

        var logger = app.Services.GetRequiredService<ILogger<CorsLogCategory>>();

        if (globalCors.Enabled)
        {
            // UseCors() with no policy name picks up DefaultPolicyName set in AddMcpProxyCors.
            app.UseCors();

            var origins = FormatOrigins(globalCors.AllowedOrigins);
            ProxyLogger.CorsEnabled(logger, origins);

            if ((globalCors.AllowedOrigins is null || globalCors.AllowedOrigins.Length == 0)
                && !globalCors.AllowAnyLocalhost)
            {
                ProxyLogger.CorsNoOriginsConfigured(logger, "global");
            }
        }
        else
        {
            // No global policy, but per-server overrides exist. We still need
            // the CORS middleware in the pipeline for RequireCors() to work
            // on individual endpoints.
            app.UseCors();
        }

        foreach (var (serverName, serverConfig) in configuration.Mcp)
        {
            if (!serverConfig.Enabled || serverConfig.Cors is not { Enabled: true })
            {
                continue;
            }

            var origins = FormatOrigins(serverConfig.Cors.AllowedOrigins);
            var policyName = GetServerPolicyName(serverName);
            ProxyLogger.CorsServerOverrideRegistered(
                logger,
                serverName,
                policyName,
                origins);

            if ((serverConfig.Cors.AllowedOrigins is null || serverConfig.Cors.AllowedOrigins.Length == 0)
                && !serverConfig.Cors.AllowAnyLocalhost)
            {
                var scope = "server '" + serverName + "'";
                ProxyLogger.CorsNoOriginsConfigured(logger, scope);
            }
        }

        return app;
    }

    /// <summary>
    /// Applies a per-server CORS policy to an endpoint, falling back to the
    /// default policy when the server has no override.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder returned by <c>MapMcp()</c>.</param>
    /// <param name="configuration">The proxy configuration.</param>
    /// <param name="serverName">
    /// The server name. When <c>null</c>, only the default policy is considered.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder ApplyServerCorsPolicy<TBuilder>(
        this TBuilder builder,
        ProxyConfiguration configuration,
        string? serverName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        // Per-server override wins.
        if (serverName is not null
            && configuration.Mcp.TryGetValue(serverName, out var serverConfig)
            && serverConfig.Cors is { Enabled: true })
        {
            builder.RequireCors(GetServerPolicyName(serverName));
            return builder;
        }

        // Fall back to the global default policy if enabled.
        if (configuration.Proxy.Cors.Enabled)
        {
            builder.RequireCors(DefaultPolicyName);
        }

        return builder;
    }

    private static void ConfigurePolicy(CorsPolicyBuilder policy, CorsConfiguration config)
    {
        var origins = config.AllowedOrigins ?? [];
        var isWildcardOrigin = origins.Length == 1 && origins[0] == "*";

        var staticOrigins = origins.Where(o => !ContainsGlobWildcard(o)).ToArray();
        var patternOrigins = origins.Where(ContainsGlobWildcard).ToArray();
        var usePredicate = !isWildcardOrigin && (config.AllowAnyLocalhost || patternOrigins.Length > 0);

        if (isWildcardOrigin)
        {
            policy.AllowAnyOrigin();
        }
        else if (usePredicate)
        {
            // Compile glob patterns to regexes once. Combine with the localhost
            // shortcut (if enabled) and any literal origins into a single
            // predicate evaluated by ASP.NET Core CORS for each request.
            var compiledPatterns = patternOrigins
                .Select(GlobToRegex)
                .ToArray();
            var literalOrigins = new HashSet<string>(staticOrigins, StringComparer.Ordinal);
            var allowAnyLocalhost = config.AllowAnyLocalhost;

            policy.SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrEmpty(origin))
                {
                    return false;
                }

                if (literalOrigins.Contains(origin))
                {
                    return true;
                }

                if (allowAnyLocalhost && IsLocalhostOrigin(origin))
                {
                    return true;
                }

                foreach (var rx in compiledPatterns)
                {
                    if (rx.IsMatch(origin))
                    {
                        return true;
                    }
                }

                return false;
            });
        }
        else if (staticOrigins.Length > 0)
        {
            policy.WithOrigins(staticOrigins);
        }
        // else: no origins configured -> policy denies all (logged as warning)

        if (config.AllowedMethods is { Length: > 0 } methods)
        {
            policy.WithMethods(methods);
        }
        else
        {
            policy.AllowAnyMethod();
        }

        if (config.AllowedHeaders is { Length: > 0 } headers)
        {
            policy.WithHeaders(headers);
        }
        else
        {
            policy.AllowAnyHeader();
        }

        // Always expose Mcp-Session-Id so browser MCP clients can read it
        // from the initialize response and reuse it on subsequent requests.
        var exposed = (config.ExposedHeaders ?? [])
            .Append("Mcp-Session-Id")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        policy.WithExposedHeaders(exposed);

        // AllowCredentials is incompatible with AllowAnyOrigin per the CORS spec.
        if (config.AllowCredentials && !isWildcardOrigin)
        {
            policy.AllowCredentials();
        }

        if (config.PreflightMaxAgeSeconds is { } maxAge && maxAge > 0)
        {
            policy.SetPreflightMaxAge(TimeSpan.FromSeconds(maxAge));
        }
    }

    private static string FormatOrigins(string[]? origins) =>
        origins is null || origins.Length == 0 ? "<none>" : string.Join(", ", origins);

    private static bool ContainsGlobWildcard(string origin) =>
        !string.IsNullOrEmpty(origin) && origin != "*" && origin.Contains('*', StringComparison.Ordinal);

    private static Regex GlobToRegex(string pattern)
    {
        // Escape every character, then re-introduce '*' as a non-slash matcher
        // so that "http://localhost:*" matches ports but not arbitrary paths,
        // and "https://*.example.com" matches a single subdomain label.
        var escaped = Regex.Escape(pattern).Replace("\\*", "[^/]*", StringComparison.Ordinal);
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsLocalhostOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host == "127.0.0.1"
            || uri.Host == "[::1]";
    }

    /// <summary>
    /// Marker type used solely as the <see cref="ILogger{TCategoryName}"/>
    /// category for CORS log entries.
    /// </summary>
    public sealed class CorsLogCategory
    {
    }
}
