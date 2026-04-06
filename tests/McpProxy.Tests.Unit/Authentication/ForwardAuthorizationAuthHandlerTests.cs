using McpProxy.SDK.Authentication;
using Microsoft.AspNetCore.Http;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Authentication;

/// <summary>
/// Tests for <see cref="ForwardAuthorizationAuthHandler"/> — the inbound authentication
/// handler that requires a Bearer token and returns 401 with RFC 9728 hints when missing.
/// </summary>
public class ForwardAuthorizationAuthHandlerTests
{
    public class AuthenticateAsyncTests : ForwardAuthorizationAuthHandlerTests
    {
        [Fact]
        public async Task Returns_Success_When_Bearer_Token_Present()
        {
            // Arrange
            var handler = new ForwardAuthorizationAuthHandler();
            var context = CreateHttpContext("Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.test");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeTrue();
            result.PrincipalId.Should().Be("forward-auth-user");
        }

        [Fact]
        public async Task Returns_Failure_When_Authorization_Header_Missing()
        {
            // Arrange
            var handler = new ForwardAuthorizationAuthHandler();
            var context = new DefaultHttpContext();

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Contain("Authorization header not provided");
        }

        [Fact]
        public async Task Returns_Failure_When_Scheme_Is_Not_Bearer()
        {
            // Arrange
            var handler = new ForwardAuthorizationAuthHandler();
            var context = CreateHttpContext("Basic dXNlcm5hbWU6cGFzc3dvcmQ=");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Contain("Invalid authorization scheme");
        }

        [Fact]
        public async Task Returns_Failure_When_Bearer_Token_Is_Empty()
        {
            // Arrange
            var handler = new ForwardAuthorizationAuthHandler();
            var context = CreateHttpContext("Bearer ");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Contain("Bearer token is empty");
        }

        [Fact]
        public async Task Returns_Failure_When_Bearer_Token_Is_Whitespace()
        {
            // Arrange
            var handler = new ForwardAuthorizationAuthHandler();
            var context = CreateHttpContext("Bearer    ");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Contain("Bearer token is empty");
        }

        [Theory]
        [InlineData("Bearer abc123")]
        [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abc")]
        [InlineData("bearer token123")]  // case-insensitive
        public async Task Accepts_Various_Valid_Bearer_Tokens(string authHeader)
        {
            // Arrange
            var handler = new ForwardAuthorizationAuthHandler();
            var context = CreateHttpContext(authHeader);

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public async Task SchemeName_Is_Bearer()
        {
            // Arrange & Act
            var handler = new ForwardAuthorizationAuthHandler();

            // Assert
            handler.SchemeName.Should().Be("Bearer");
        }
    }

    public class ChallengeAsyncTests : ForwardAuthorizationAuthHandlerTests
    {
        [Fact]
        public async Task Sets_401_Status_Code()
        {
            // Arrange
            var handler = new ForwardAuthorizationAuthHandler();
            var context = new DefaultHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task Sets_WWW_Authenticate_Header_Without_Registry()
        {
            // Arrange
            var handler = new ForwardAuthorizationAuthHandler(oauthRegistry: null);
            var context = new DefaultHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.Headers["WWW-Authenticate"].ToString().Should().Be("Bearer");
        }

        [Fact]
        public async Task Includes_Resource_Metadata_URL_When_Registry_Has_OAuth()
        {
            // Arrange
            var registry = Substitute.For<IOAuthMetadataRegistry>();
            registry.GetPrimaryProbeResult().Returns(new OAuthProbeResult
            {
                BackendUrl = "https://backend.example.com",
                SupportsOAuthProtectedResource = true,
                OAuthProtectedResourceMetadata = """{"resource":"https://backend.example.com"}"""
            });

            var handler = new ForwardAuthorizationAuthHandler(registry);

            var context = new DefaultHttpContext();
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost", 5101);
            context.Request.Path = "/mcp";

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            var wwwAuth = context.Response.Headers["WWW-Authenticate"].ToString();
            wwwAuth.Should().Contain("resource_metadata=");
            wwwAuth.Should().Contain("http://localhost:5101/.well-known/oauth-protected-resource/mcp");
        }

        [Fact]
        public async Task Uses_Request_Path_For_Resource_Metadata_URL()
        {
            // Arrange
            var registry = Substitute.For<IOAuthMetadataRegistry>();
            registry.GetPrimaryProbeResult().Returns(new OAuthProbeResult
            {
                BackendUrl = "https://backend.example.com",
                SupportsOAuthProtectedResource = true,
                OAuthProtectedResourceMetadata = """{"resource":"https://backend.example.com"}"""
            });

            var handler = new ForwardAuthorizationAuthHandler(registry);

            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("proxy.example.com");
            context.Request.Path = "/api/mcp";

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            var wwwAuth = context.Response.Headers["WWW-Authenticate"].ToString();
            wwwAuth.Should().Contain("https://proxy.example.com/.well-known/oauth-protected-resource/api/mcp");
        }

        [Fact]
        public async Task Falls_Back_To_Basic_Bearer_When_Registry_Has_No_OAuth()
        {
            // Arrange
            var registry = Substitute.For<IOAuthMetadataRegistry>();
            registry.GetPrimaryProbeResult().Returns(new OAuthProbeResult
            {
                BackendUrl = "https://backend.example.com",
                SupportsOAuthProtectedResource = false
            });

            var handler = new ForwardAuthorizationAuthHandler(registry);
            var context = new DefaultHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.Headers["WWW-Authenticate"].ToString().Should().Be("Bearer");
        }

        [Fact]
        public async Task Falls_Back_To_Basic_Bearer_When_Registry_Returns_Null()
        {
            // Arrange
            var registry = Substitute.For<IOAuthMetadataRegistry>();
            registry.GetPrimaryProbeResult().Returns((OAuthProbeResult?)null);

            var handler = new ForwardAuthorizationAuthHandler(registry);
            var context = new DefaultHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.Headers["WWW-Authenticate"].ToString().Should().Be("Bearer");
        }
    }

    private static DefaultHttpContext CreateHttpContext(string? authorizationHeader)
    {
        var context = new DefaultHttpContext();
        if (authorizationHeader is not null)
        {
            context.Request.Headers["Authorization"] = authorizationHeader;
        }

        return context;
    }
}
