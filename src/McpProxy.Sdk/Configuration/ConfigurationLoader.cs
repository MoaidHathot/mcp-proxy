using System.Text.Json;
using System.Text.RegularExpressions;

namespace McpProxy.Sdk.Configuration;

/// <summary>
/// Loads and processes proxy configuration from JSON files.
/// </summary>
public static partial class ConfigurationLoader
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Loads configuration from a JSON file.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded configuration.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the config file is not found.</exception>
    /// <exception cref="JsonException">Thrown when the JSON is invalid.</exception>
    public static async Task<ProxyConfiguration> LoadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configPath}", configPath);
        }

        var json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        json = SubstituteEnvironmentVariables(json);

        var config = JsonSerializer.Deserialize<ProxyConfiguration>(json, s_jsonOptions)
            ?? throw new JsonException("Failed to deserialize configuration");

        ValidateConfiguration(config);
        return config;
    }

    /// <summary>
    /// Loads configuration from a JSON string.
    /// </summary>
    /// <param name="json">JSON configuration string.</param>
    /// <returns>The loaded configuration.</returns>
    public static ProxyConfiguration LoadFromString(string json)
    {
        json = SubstituteEnvironmentVariables(json);

        var config = JsonSerializer.Deserialize<ProxyConfiguration>(json, s_jsonOptions)
            ?? throw new JsonException("Failed to deserialize configuration");

        ValidateConfiguration(config);
        return config;
    }

    /// <summary>
    /// Substitutes environment variable references in the format "env:VARIABLE_NAME" or "${VARIABLE_NAME}".
    /// </summary>
    private static string SubstituteEnvironmentVariables(string json)
    {
        // Handle "env:VARIABLE_NAME" format (as string values) - replace entire quoted string
        json = EnvColonPattern().Replace(json, match =>
        {
            var varName = match.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(varName);
            // Keep the quotes and replace value, or keep original if env var not found
            return envValue is not null ? $"\"{envValue}\"" : match.Value;
        });

        // Handle ${VARIABLE_NAME} format (inline substitution)
        json = DollarBracePattern().Replace(json, match =>
        {
            var varName = match.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(varName);
            return envValue ?? match.Value;
        });

        return json;
    }

    /// <summary>
    /// Validates the configuration and throws if invalid.
    /// </summary>
    private static void ValidateConfiguration(ProxyConfiguration config)
    {
        foreach (var (name, server) in config.Mcp)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Server name cannot be empty");
            }

            switch (server.Type)
            {
                case ServerTransportType.Stdio:
                    if (string.IsNullOrWhiteSpace(server.Command))
                    {
                        throw new InvalidOperationException($"Server '{name}': STDIO transport requires a 'command' property");
                    }
                    break;

                case ServerTransportType.Http:
                case ServerTransportType.Sse:
                    if (string.IsNullOrWhiteSpace(server.Url))
                    {
                        throw new InvalidOperationException($"Server '{name}': HTTP/SSE transport requires a 'url' property");
                    }
                    if (!Uri.TryCreate(server.Url, UriKind.Absolute, out _))
                    {
                        throw new InvalidOperationException($"Server '{name}': Invalid URL '{server.Url}'");
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Server '{name}': Unknown transport type '{server.Type}'");
            }
        }

        // Validate authentication configuration
        if (config.Proxy.Authentication.Enabled)
        {
            switch (config.Proxy.Authentication.Type)
            {
                case AuthenticationType.ApiKey:
                    if (string.IsNullOrWhiteSpace(config.Proxy.Authentication.ApiKey.Value))
                    {
                        throw new InvalidOperationException("API key authentication requires a 'value' property");
                    }
                    break;

                case AuthenticationType.Bearer:
                    if (string.IsNullOrWhiteSpace(config.Proxy.Authentication.Bearer.Authority))
                    {
                        throw new InvalidOperationException("Bearer authentication requires an 'authority' property");
                    }
                    break;
            }
        }
    }

    [GeneratedRegex(@"""env:([A-Za-z_][A-Za-z0-9_]*)""")]
    private static partial Regex EnvColonPattern();

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex DollarBracePattern();
}
