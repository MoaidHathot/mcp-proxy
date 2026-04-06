using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using Microsoft.AspNetCore.Http;

namespace McpProxy.Tests.Unit.Authentication;

public class AzureAdAuthHandlerTests
{
    private const string TestTenantId = "test-tenant-id";
    private const string TestClientId = "test-client-id";

    private static AzureAdConfiguration CreateDefaultConfig() => new()
    {
        TenantId = TestTenantId,
        ClientId = TestClientId,
        Audience = TestClientId,
        ValidateIssuer = true,
        ValidateAudience = true
    };

    private static DefaultHttpContext CreateHttpContext(string? authorizationHeader = null)
    {
        var context = new DefaultHttpContext();
        if (authorizationHeader is not null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }
        return context;
    }

    public class AuthenticateAsyncTests
    {
        [Fact]
        public async Task Returns_Failure_When_Authorization_Header_Missing()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var handler = new AzureAdAuthHandler(config);
            var context = CreateHttpContext();

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Authorization header not provided");
        }

        [Fact]
        public async Task Returns_Failure_When_Authorization_Scheme_Is_Not_Bearer()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var handler = new AzureAdAuthHandler(config);
            var context = CreateHttpContext("Basic dXNlcjpwYXNz");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Invalid authorization scheme. Expected 'Bearer'");
        }

        [Fact]
        public async Task Returns_Failure_When_Bearer_Token_Is_Empty()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var handler = new AzureAdAuthHandler(config);
            var context = CreateHttpContext("Bearer ");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Bearer token not provided");
        }

        [Fact]
        public async Task Returns_Failure_When_Token_Is_Malformed()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var handler = new AzureAdAuthHandler(config);
            var context = CreateHttpContext("Bearer not-a-valid-jwt");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Contain("Authentication failed");
        }
    }

    public class ChallengeAsyncTests
    {
        [Fact]
        public async Task Sets_401_Status_Code()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var handler = new AzureAdAuthHandler(config);
            var context = CreateHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task Sets_WWWAuthenticate_Header_With_Authority()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var handler = new AzureAdAuthHandler(config);
            var context = CreateHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            var wwwAuthHeader = context.Response.Headers.WWWAuthenticate.ToString();
            wwwAuthHeader.Should().Contain("Bearer");
            wwwAuthHeader.Should().Contain(config.Authority);
        }
    }

    public class SchemeNameTests
    {
        [Fact]
        public void SchemeName_Returns_Bearer()
        {
            // Arrange
            var config = CreateDefaultConfig();
            var handler = new AzureAdAuthHandler(config);

            // Act & Assert
            handler.SchemeName.Should().Be("Bearer");
        }
    }
}

public class AzureAdConfigurationTests
{
    [Fact]
    public void Authority_Returns_Correct_Url_With_TenantId()
    {
        // Arrange
        var config = new AzureAdConfiguration
        {
            Instance = "https://login.microsoftonline.com/",
            TenantId = "my-tenant-id"
        };

        // Act
        var authority = config.Authority;

        // Assert
        authority.Should().Be("https://login.microsoftonline.com/my-tenant-id");
    }

    [Fact]
    public void Authority_Handles_Instance_Without_Trailing_Slash()
    {
        // Arrange
        var config = new AzureAdConfiguration
        {
            Instance = "https://login.microsoftonline.com",
            TenantId = "my-tenant-id"
        };

        // Act
        var authority = config.Authority;

        // Assert
        authority.Should().Be("https://login.microsoftonline.com/my-tenant-id");
    }

    [Fact]
    public void Authority_Returns_Instance_When_TenantId_Is_Null()
    {
        // Arrange
        var config = new AzureAdConfiguration
        {
            Instance = "https://login.microsoftonline.com/",
            TenantId = null
        };

        // Act
        var authority = config.Authority;

        // Assert
        authority.Should().Be("https://login.microsoftonline.com");
    }

    [Fact]
    public void Default_Instance_Is_AzureAd_Login()
    {
        // Arrange
        var config = new AzureAdConfiguration();

        // Assert
        config.Instance.Should().Be("https://login.microsoftonline.com/");
    }

    [Fact]
    public void Default_ValidateIssuer_Is_True()
    {
        // Arrange
        var config = new AzureAdConfiguration();

        // Assert
        config.ValidateIssuer.Should().BeTrue();
    }

    [Fact]
    public void Default_ValidateAudience_Is_True()
    {
        // Arrange
        var config = new AzureAdConfiguration();

        // Assert
        config.ValidateAudience.Should().BeTrue();
    }
}

public class AuthenticationTypeEnumTests
{
    [Fact]
    public void AuthenticationType_Contains_AzureAd()
    {
        // Assert
        Enum.IsDefined(AuthenticationType.AzureAd).Should().BeTrue();
    }

    [Fact]
    public void All_Authentication_Types_Are_Present()
    {
        // Assert
        var values = Enum.GetValues<AuthenticationType>();
        values.Should().Contain(AuthenticationType.None);
        values.Should().Contain(AuthenticationType.ApiKey);
        values.Should().Contain(AuthenticationType.Bearer);
        values.Should().Contain(AuthenticationType.AzureAd);
    }
}

public class AuthenticationConfigurationTests
{
    [Fact]
    public void Default_AzureAd_Property_Is_Not_Null()
    {
        // Arrange
        var config = new AuthenticationConfiguration();

        // Assert
        config.AzureAd.Should().NotBeNull();
    }

    [Fact]
    public void Can_Set_AzureAd_Configuration()
    {
        // Arrange
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Type = AuthenticationType.AzureAd,
            AzureAd = new AzureAdConfiguration
            {
                TenantId = "test-tenant",
                ClientId = "test-client"
            }
        };

        // Assert
        config.Type.Should().Be(AuthenticationType.AzureAd);
        config.AzureAd.TenantId.Should().Be("test-tenant");
        config.AzureAd.ClientId.Should().Be("test-client");
    }
}
