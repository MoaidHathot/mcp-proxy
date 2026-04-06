using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.AppConfig;
using System.Security.Cryptography.X509Certificates;

namespace McpProxy.Sdk.Authentication;

/// <summary>
/// Provides Azure AD access tokens for outbound requests to backend MCP servers.
/// Supports client credentials, on-behalf-of, and managed identity flows.
/// </summary>
public sealed class AzureAdCredentialProvider : IDisposable
{
    private readonly BackendAzureAdConfiguration _config;
    private readonly BackendAuthType _authType;
    private readonly string _authTypeName;
    private readonly ILogger<AzureAdCredentialProvider> _logger;
    private readonly IConfidentialClientApplication? _confidentialClient;
    private readonly Lazy<ManagedIdentityApplication>? _managedIdentityClient;
    private readonly string[] _scopes;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AzureAdCredentialProvider"/>.
    /// </summary>
    /// <param name="config">The backend Azure AD configuration.</param>
    /// <param name="authType">The authentication type to use.</param>
    /// <param name="logger">The logger instance.</param>
    public AzureAdCredentialProvider(
        BackendAzureAdConfiguration config,
        BackendAuthType authType,
        ILogger<AzureAdCredentialProvider> logger)
    {
        _config = config;
        _authType = authType;
        _authTypeName = authType.ToString();
        _logger = logger;
        _scopes = config.Scopes ?? [".default"];

        switch (authType)
        {
            case BackendAuthType.AzureAdClientCredentials:
            case BackendAuthType.AzureAdOnBehalfOf:
                _confidentialClient = CreateConfidentialClient(config);
                break;

            case BackendAuthType.AzureAdManagedIdentity:
                _managedIdentityClient = new Lazy<ManagedIdentityApplication>(() =>
                    CreateManagedIdentityClient(config));
                break;
        }
    }

    /// <summary>
    /// Acquires an access token for the configured backend server.
    /// </summary>
    /// <param name="userAssertion">The user assertion token for on-behalf-of flow. Required when using OBO.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The access token.</returns>
    public async Task<string> AcquireTokenAsync(
        string? userAssertion = null,
        CancellationToken cancellationToken = default)
    {
        ProxyLogger.AcquiringToken(_logger, _authTypeName, _config.Authority);

        try
        {
            var result = _authType switch
            {
                BackendAuthType.AzureAdClientCredentials => await AcquireTokenWithClientCredentialsAsync(cancellationToken).ConfigureAwait(false),
                BackendAuthType.AzureAdOnBehalfOf => await AcquireTokenOnBehalfOfAsync(userAssertion!, cancellationToken).ConfigureAwait(false),
                BackendAuthType.AzureAdManagedIdentity => await AcquireTokenWithManagedIdentityAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported auth type: {_authType}")
            };

            ProxyLogger.TokenAcquired(_logger, _authTypeName);
            return result.AccessToken;
        }
        catch (MsalException ex)
        {
            ProxyLogger.TokenAcquisitionFailed(_logger, _authTypeName, ex.Message);
            throw new AuthenticationException($"Failed to acquire token: {ex.Message}", ex);
        }
    }

    private async Task<AuthenticationResult> AcquireTokenWithClientCredentialsAsync(CancellationToken cancellationToken)
    {
        if (_confidentialClient is null)
        {
            throw new InvalidOperationException("Confidential client not initialized");
        }

        return await _confidentialClient
            .AcquireTokenForClient(_scopes)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AuthenticationResult> AcquireTokenOnBehalfOfAsync(string userAssertion, CancellationToken cancellationToken)
    {
        if (_confidentialClient is null)
        {
            throw new InvalidOperationException("Confidential client not initialized");
        }

        if (string.IsNullOrEmpty(userAssertion))
        {
            throw new ArgumentException("User assertion is required for on-behalf-of flow", nameof(userAssertion));
        }

        var assertion = new UserAssertion(userAssertion);

        return await _confidentialClient
            .AcquireTokenOnBehalfOf(_scopes, assertion)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AuthenticationResult> AcquireTokenWithManagedIdentityAsync(CancellationToken cancellationToken)
    {
        if (_managedIdentityClient is null)
        {
            throw new InvalidOperationException("Managed identity client not initialized");
        }

        // For managed identity, we need to use a single scope (the resource)
        var scope = _scopes.Length > 0 ? _scopes[0] : throw new InvalidOperationException("At least one scope is required for managed identity");

        return await _managedIdentityClient.Value
            .AcquireTokenForManagedIdentity(scope)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IConfidentialClientApplication CreateConfidentialClient(BackendAzureAdConfiguration config)
    {
        if (string.IsNullOrEmpty(config.ClientId))
        {
            throw new InvalidOperationException("ClientId is required for Azure AD authentication");
        }

        var builder = ConfidentialClientApplicationBuilder
            .Create(config.ClientId)
            .WithAuthority(config.Authority);

        // Configure credentials (secret or certificate)
        if (!string.IsNullOrEmpty(config.ClientSecret))
        {
            var secret = ResolveSecretValue(config.ClientSecret);
            builder.WithClientSecret(secret);
        }
        else if (!string.IsNullOrEmpty(config.CertificatePath))
        {
            var certificate = LoadCertificateFromFile(config.CertificatePath);
            builder.WithCertificate(certificate);
        }
        else if (!string.IsNullOrEmpty(config.CertificateThumbprint))
        {
            var certificate = LoadCertificateFromStore(config.CertificateThumbprint);
            builder.WithCertificate(certificate);
        }
        else
        {
            throw new InvalidOperationException(
                "Either ClientSecret, CertificatePath, or CertificateThumbprint is required for confidential client authentication");
        }

        return builder.Build();
    }

    private static ManagedIdentityApplication CreateManagedIdentityClient(BackendAzureAdConfiguration config)
    {
        var managedIdentityId = string.IsNullOrEmpty(config.ManagedIdentityClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.WithUserAssignedClientId(config.ManagedIdentityClientId);

        return (ManagedIdentityApplication)ManagedIdentityApplicationBuilder
            .Create(managedIdentityId)
            .Build();
    }

    private static string ResolveSecretValue(string value)
    {
        if (value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var envVarName = value[4..];
            return Environment.GetEnvironmentVariable(envVarName)
                ?? throw new InvalidOperationException($"Environment variable '{envVarName}' not found");
        }

        return value;
    }

    private static X509Certificate2 LoadCertificateFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Certificate file not found: {path}");
        }

        return X509CertificateLoader.LoadCertificateFromFile(path);
    }

    private static X509Certificate2 LoadCertificateFromStore(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var certificates = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            thumbprint,
            validOnly: false);

        if (certificates.Count == 0)
        {
            throw new InvalidOperationException($"Certificate with thumbprint '{thumbprint}' not found in store");
        }

        return certificates[0];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // MSAL clients don't require explicit disposal but we mark as disposed
        _disposed = true;
    }
}

/// <summary>
/// Exception thrown when authentication fails.
/// </summary>
public sealed class AuthenticationException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="AuthenticationException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AuthenticationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AuthenticationException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
