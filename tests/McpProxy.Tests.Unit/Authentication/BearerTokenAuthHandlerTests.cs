using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using Microsoft.AspNetCore.Http;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task

namespace McpProxy.Tests.Unit.Authentication;

public class BearerTokenAuthHandlerTests
{
    private static BearerConfiguration CreateDefaultConfig() => new()
    {
        Authority = "https://login.example.com",
        Audience = "api://test-audience",
        ValidateIssuer = false,
        ValidateAudience = false
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
        public async Task Returns_Failure_When_No_Authorization_Header()
        {
            // Arrange
            var handler = new BearerTokenAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Authorization header not provided");
        }

        [Fact]
        public async Task Returns_Failure_When_Scheme_Is_Not_Bearer()
        {
            // Arrange
            var handler = new BearerTokenAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext("Basic dXNlcjpwYXNz");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Invalid authorization scheme");
        }

        [Fact]
        public async Task Returns_Failure_When_Token_Is_Empty()
        {
            // Arrange
            var handler = new BearerTokenAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext("Bearer ");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Bearer token not provided");
        }

        [Fact]
        public async Task Throws_When_Token_Is_Malformed()
        {
            // Arrange
            var handler = new BearerTokenAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext("Bearer not-a-valid-jwt");

            // Act
            // Note: SecurityTokenMalformedException inherits from ArgumentException,
            // not SecurityTokenException, so it isn't caught by the handler's catch blocks.
            // This is a known limitation -- malformed tokens throw rather than returning a failure.
            Func<Task> act = async () => await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            await act.Should().ThrowAsync<Exception>();
        }
    }

    public class ChallengeAsyncTests
    {
        [Fact]
        public async Task Sets_401_Status_Code()
        {
            // Arrange
            var handler = new BearerTokenAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task Sets_WWW_Authenticate_Header_With_Bearer_Scheme()
        {
            // Arrange
            var handler = new BearerTokenAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.Headers["WWW-Authenticate"].ToString()
                .Should().StartWith("Bearer");
        }

        [Fact]
        public async Task Includes_Realm_In_Challenge_When_Authority_Configured()
        {
            // Arrange
            var config = CreateDefaultConfig();
            config.Authority = "https://login.example.com";
            var handler = new BearerTokenAuthHandler(config);
            var context = CreateHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.Headers["WWW-Authenticate"].ToString()
                .Should().Contain("realm=\"https://login.example.com\"");
        }

        [Fact]
        public async Task Omits_Realm_When_No_Authority_Configured()
        {
            // Arrange
            var config = new BearerConfiguration
            {
                Authority = null,
                ValidateIssuer = false,
                ValidateAudience = false
            };
            var handler = new BearerTokenAuthHandler(config);
            var context = CreateHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.Headers["WWW-Authenticate"].ToString()
                .Should().Be("Bearer");
        }
    }

    public class SchemeNameTests
    {
        [Fact]
        public void Returns_Bearer()
        {
            // Arrange
            var handler = new BearerTokenAuthHandler(CreateDefaultConfig());

            // Act & Assert
            handler.SchemeName.Should().Be("Bearer");
        }
    }
}
