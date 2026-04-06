using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using Microsoft.Extensions.Logging;

namespace McpProxy.Tests.Unit.Authentication;

public class AzureAdAuthorizationHandlerTests
{
    private readonly ILogger<AzureAdAuthorizationHandler> _logger;
    private readonly ILogger<AzureAdCredentialProvider> _providerLogger;

    public AzureAdAuthorizationHandlerTests()
    {
        _logger = Substitute.For<ILogger<AzureAdAuthorizationHandler>>();
        _providerLogger = Substitute.For<ILogger<AzureAdCredentialProvider>>();
    }

    private static BackendAzureAdConfiguration CreateDefaultConfig() => new()
    {
        TenantId = "test-tenant-id",
        ClientId = "test-client-id",
        ClientSecret = "test-secret",
        Scopes = ["api://backend/.default"]
    };

    public class ConstructorTests : AzureAdAuthorizationHandlerTests
    {
        [Fact]
        public void Creates_Handler_With_CredentialProvider()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _providerLogger);

            // Act
            var handler = new AzureAdAuthorizationHandler(provider, _logger);

            // Assert
            handler.Should().NotBeNull();
            handler.Dispose();
        }

        [Fact]
        public void Creates_Handler_With_UserTokenAccessor()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdOnBehalfOf, _providerLogger);
            Func<string?> userTokenAccessor = () => "user-token";

            // Act
            var handler = new AzureAdAuthorizationHandler(provider, _logger, userTokenAccessor);

            // Assert
            handler.Should().NotBeNull();
            handler.Dispose();
        }

        [Fact]
        public void Creates_Handler_With_InnerHandler()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _providerLogger);
            var innerHandler = new HttpClientHandler();

            // Act
            var handler = new AzureAdAuthorizationHandler(provider, _logger, innerHandler);

            // Assert
            handler.Should().NotBeNull();
            handler.Dispose();
        }
    }

    public class DisposeTests : AzureAdAuthorizationHandlerTests
    {
        [Fact]
        public void Disposes_CredentialProvider()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _providerLogger);
            var handler = new AzureAdAuthorizationHandler(provider, _logger);

            // Act & Assert - should not throw
            handler.Dispose();
        }

        [Fact]
        public void Can_Be_Disposed_Multiple_Times()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var provider = new AzureAdCredentialProvider(config, BackendAuthType.AzureAdClientCredentials, _providerLogger);
            var handler = new AzureAdAuthorizationHandler(provider, _logger);

            // Act & Assert - should not throw
            handler.Dispose();
            handler.Dispose();
        }
    }
}

public class AuthenticationExceptionTests
{
    [Fact]
    public void Creates_Exception_With_Message()
    {
        // Arrange & Act
        var exception = new AuthenticationException("Test error");

        // Assert
        exception.Message.Should().Be("Test error");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Creates_Exception_With_InnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner error");

        // Act
        var exception = new AuthenticationException("Outer error", inner);

        // Assert
        exception.Message.Should().Be("Outer error");
        exception.InnerException.Should().Be(inner);
    }
}
