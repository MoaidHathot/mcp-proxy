using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Authentication;

public class AzureAdCredentialProviderTests
{
    private readonly ILogger<AzureAdCredentialProvider> _logger;

    public AzureAdCredentialProviderTests()
    {
        _logger = Substitute.For<ILogger<AzureAdCredentialProvider>>();
    }

    private static BackendAzureAdConfiguration CreateDefaultConfig() => new()
    {
        TenantId = "test-tenant-id",
        ClientId = "test-client-id",
        ClientSecret = "test-secret",
        Scopes = ["api://backend/.default"]
    };

    public class ConstructorTests : AzureAdCredentialProviderTests
    {
        [Fact]
        public void Creates_Provider_For_ClientCredentials_WithSecret()
        {
            // Arrange
            var config = CreateDefaultConfig();

            // Act
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _logger);

            // Assert
            provider.Should().NotBeNull();
            provider.Dispose();
        }

        [Fact]
        public void Creates_Provider_For_OnBehalfOf_WithSecret()
        {
            // Arrange
            var config = CreateDefaultConfig();

            // Act
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdOnBehalfOf, _logger);

            // Assert
            provider.Should().NotBeNull();
            provider.Dispose();
        }

        [Fact]
        public void Creates_Provider_For_ManagedIdentity_SystemAssigned()
        {
            // Arrange
            var config = new BackendAzureAdConfiguration
            {
                Scopes = ["api://backend/.default"]
            };

            // Act
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdManagedIdentity, _logger);

            // Assert
            provider.Should().NotBeNull();
            provider.Dispose();
        }

        [Fact]
        public void Creates_Provider_For_ManagedIdentity_UserAssigned()
        {
            // Arrange
            var config = new BackendAzureAdConfiguration
            {
                ManagedIdentityClientId = "managed-identity-client-id",
                Scopes = ["api://backend/.default"]
            };

            // Act
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdManagedIdentity, _logger);

            // Assert
            provider.Should().NotBeNull();
            provider.Dispose();
        }

        [Fact]
        public void Throws_When_ClientCredentials_MissingCredentials()
        {
            // Arrange
            var config = new BackendAzureAdConfiguration
            {
                TenantId = "test-tenant-id",
                ClientId = "test-client-id"
                // Missing ClientSecret, CertificatePath, and CertificateThumbprint
            };

            // Act
            var action = () => new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _logger);

            // Assert
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*ClientSecret*CertificatePath*CertificateThumbprint*");
        }

        [Fact]
        public void Throws_When_ClientCredentials_MissingClientId()
        {
            // Arrange
            var config = new BackendAzureAdConfiguration
            {
                TenantId = "test-tenant-id",
                ClientSecret = "test-secret"
                // Missing ClientId
            };

            // Act
            var action = () => new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _logger);

            // Assert
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*ClientId*required*");
        }

        [Fact]
        public void Resolves_ClientSecret_From_EnvironmentVariable()
        {
            // Arrange
            const string envVarName = "TEST_AAD_SECRET";
            const string secretValue = "secret-from-env";
            Environment.SetEnvironmentVariable(envVarName, secretValue);

            try
            {
                var config = new BackendAzureAdConfiguration
                {
                    TenantId = "test-tenant-id",
                    ClientId = "test-client-id",
                    ClientSecret = $"env:{envVarName}"
                };

                // Act - should not throw
                var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _logger);

                // Assert
                provider.Should().NotBeNull();
                provider.Dispose();
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, null);
            }
        }

        [Fact]
        public void Throws_When_EnvironmentVariable_NotFound()
        {
            // Arrange
            const string envVarName = "NONEXISTENT_AAD_SECRET_12345";
            var config = new BackendAzureAdConfiguration
            {
                TenantId = "test-tenant-id",
                ClientId = "test-client-id",
                ClientSecret = $"env:{envVarName}"
            };

            // Act
            var action = () => new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _logger);

            // Assert
            action.Should().Throw<InvalidOperationException>()
                .WithMessage($"*Environment variable '{envVarName}' not found*");
        }

        [Fact]
        public void Throws_When_CertificateFile_NotFound()
        {
            // Arrange
            var config = new BackendAzureAdConfiguration
            {
                TenantId = "test-tenant-id",
                ClientId = "test-client-id",
                CertificatePath = "/nonexistent/path/to/cert.pfx"
            };

            // Act
            var action = () => new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _logger);

            // Assert
            action.Should().Throw<FileNotFoundException>()
                .WithMessage("*Certificate file not found*");
        }
    }

    public class AcquireTokenAsyncTests : AzureAdCredentialProviderTests
    {
        [Fact]
        public async Task Throws_ArgumentException_When_OBO_MissingUserAssertion()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdOnBehalfOf, _logger);

            try
            {
                // Act
                var action = async () => await provider.AcquireTokenAsync(userAssertion: null);

                // Assert
                await action.Should().ThrowAsync<ArgumentException>()
                    .WithMessage("*User assertion is required for on-behalf-of flow*");
            }
            finally
            {
                provider.Dispose();
            }
        }

        [Fact]
        public async Task Throws_ArgumentException_When_OBO_EmptyUserAssertion()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdOnBehalfOf, _logger);

            try
            {
                // Act
                var action = async () => await provider.AcquireTokenAsync(userAssertion: "");

                // Assert
                await action.Should().ThrowAsync<ArgumentException>()
                    .WithMessage("*User assertion is required for on-behalf-of flow*");
            }
            finally
            {
                provider.Dispose();
            }
        }

        [Fact]
        public async Task Throws_InvalidOperation_When_ManagedIdentity_NoScopes()
        {
            // Arrange
            var config = new BackendAzureAdConfiguration
            {
                Scopes = [] // Empty scopes
            };
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdManagedIdentity, _logger);

            try
            {
                // Act
                var action = async () => await provider.AcquireTokenAsync();

                // Assert
                await action.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*At least one scope is required for managed identity*");
            }
            finally
            {
                provider.Dispose();
            }
        }
    }

    public class DisposeTests : AzureAdCredentialProviderTests
    {
        [Fact]
        public void Can_Be_Disposed_Multiple_Times()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _logger);

            // Act & Assert - should not throw
            provider.Dispose();
            provider.Dispose();
            provider.Dispose();
        }
    }
}
