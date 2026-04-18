using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace McpProxy.Tests.Unit.Authentication;

public class AuthenticationMiddlewareTests
{
    private static AuthenticationConfiguration CreateDisabledConfig() => new()
    {
        Enabled = false,
        Type = AuthenticationType.None
    };

    private static AuthenticationConfiguration CreateApiKeyConfig(string apiKey = "test-key") => new()
    {
        Enabled = true,
        Type = AuthenticationType.ApiKey,
        ApiKey = new ApiKeyConfiguration
        {
            Header = "X-API-Key",
            Value = apiKey
        }
    };

    private static ILogger<AuthenticationMiddleware> CreateLogger() =>
        Substitute.For<ILogger<AuthenticationMiddleware>>();

    public class InvokeAsyncTests
    {
        [Fact]
        public async Task Calls_Next_When_Auth_Disabled()
        {
            // Arrange
            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };
            var middleware = new AuthenticationMiddleware(next, CreateDisabledConfig(), CreateLogger());
            var context = new DefaultHttpContext();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task Calls_Next_When_Authenticated()
        {
            // Arrange
            const string apiKey = "valid-key";
            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };
            var middleware = new AuthenticationMiddleware(next, CreateApiKeyConfig(apiKey), CreateLogger());
            var context = new DefaultHttpContext();
            context.Request.Headers["X-API-Key"] = apiKey;

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task Stores_AuthResult_In_HttpContext_Items_When_Authenticated()
        {
            // Arrange
            const string apiKey = "valid-key";
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = new AuthenticationMiddleware(next, CreateApiKeyConfig(apiKey), CreateLogger());
            var context = new DefaultHttpContext();
            context.Request.Headers["X-API-Key"] = apiKey;

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            context.Items.Should().ContainKey("McpProxy.Authentication.Result");
            context.Items.Should().ContainKey("McpProxy.Authentication.PrincipalId");
            context.Items["McpProxy.Authentication.PrincipalId"].Should().Be("api-key-user");
        }

        [Fact]
        public async Task Does_Not_Call_Next_When_Unauthenticated()
        {
            // Arrange
            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };
            var middleware = new AuthenticationMiddleware(next, CreateApiKeyConfig(), CreateLogger());
            var context = new DefaultHttpContext();
            // No API key header set

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            nextCalled.Should().BeFalse();
        }

        [Fact]
        public async Task Returns_401_When_Unauthenticated()
        {
            // Arrange
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = new AuthenticationMiddleware(next, CreateApiKeyConfig(), CreateLogger());
            var context = new DefaultHttpContext();
            // No API key header set

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task Does_Not_Store_AuthResult_When_Unauthenticated()
        {
            // Arrange
            RequestDelegate next = _ => Task.CompletedTask;
            var middleware = new AuthenticationMiddleware(next, CreateApiKeyConfig(), CreateLogger());
            var context = new DefaultHttpContext();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            context.Items.Should().NotContainKey("McpProxy.Authentication.Result");
        }

        [Fact]
        public async Task Calls_Next_When_Auth_Handler_Is_Null_For_Unknown_Type()
        {
            // Arrange
            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };
            var config = new AuthenticationConfiguration
            {
                Enabled = true,
                Type = AuthenticationType.None // No handler created for this type
            };
            var middleware = new AuthenticationMiddleware(next, config, CreateLogger());
            var context = new DefaultHttpContext();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            nextCalled.Should().BeTrue();
        }
    }
}
