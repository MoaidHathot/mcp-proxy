using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using Microsoft.AspNetCore.Http;

namespace McpProxy.Tests.Unit.Authentication;

public class ApiKeyAuthHandlerTests
{
    private const string TestApiKey = "test-api-key-12345";
    private const string DefaultHeaderName = "X-API-Key";

    private static ApiKeyConfiguration CreateDefaultConfig() => new()
    {
        Header = DefaultHeaderName,
        Value = TestApiKey
    };

    private static DefaultHttpContext CreateHttpContext() => new();

    public class AuthenticateAsyncTests
    {
        [Fact]
        public async Task Returns_Success_When_Valid_Key_In_Header()
        {
            // Arrange
            var handler = new ApiKeyAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();
            context.Request.Headers[DefaultHeaderName] = TestApiKey;

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeTrue();
            result.PrincipalId.Should().Be("api-key-user");
        }

        [Fact]
        public async Task Returns_Failure_When_Invalid_Key_In_Header()
        {
            // Arrange
            var handler = new ApiKeyAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();
            context.Request.Headers[DefaultHeaderName] = "wrong-key";

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Invalid API key");
        }

        [Fact]
        public async Task Returns_Failure_When_No_Key_Provided()
        {
            // Arrange
            var handler = new ApiKeyAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("API key not provided");
        }

        [Fact]
        public async Task Returns_Success_When_Valid_Key_In_Query_Parameter()
        {
            // Arrange
            var config = CreateDefaultConfig();
            config.QueryParameter = "api_key";
            var handler = new ApiKeyAuthHandler(config);
            var context = CreateHttpContext();
            context.Request.QueryString = new QueryString($"?api_key={TestApiKey}");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeTrue();
            result.PrincipalId.Should().Be("api-key-user");
        }

        [Fact]
        public async Task Returns_Failure_When_Invalid_Key_In_Query_Parameter()
        {
            // Arrange
            var config = CreateDefaultConfig();
            config.QueryParameter = "api_key";
            var handler = new ApiKeyAuthHandler(config);
            var context = CreateHttpContext();
            context.Request.QueryString = new QueryString("?api_key=wrong-key");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Invalid API key");
        }

        [Fact]
        public async Task Header_Takes_Priority_Over_Query_Parameter()
        {
            // Arrange
            var config = CreateDefaultConfig();
            config.QueryParameter = "api_key";
            var handler = new ApiKeyAuthHandler(config);
            var context = CreateHttpContext();
            context.Request.Headers[DefaultHeaderName] = TestApiKey;
            context.Request.QueryString = new QueryString("?api_key=wrong-key");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public async Task Query_Parameter_Not_Checked_When_Not_Configured()
        {
            // Arrange
            var config = CreateDefaultConfig();
            config.QueryParameter = null;
            var handler = new ApiKeyAuthHandler(config);
            var context = CreateHttpContext();
            context.Request.QueryString = new QueryString($"?api_key={TestApiKey}");

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("API key not provided");
        }

        [Fact]
        public async Task Returns_Failure_When_Header_Is_Empty()
        {
            // Arrange
            var handler = new ApiKeyAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();
            context.Request.Headers[DefaultHeaderName] = "";

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
        }

        [Fact]
        public async Task Uses_Custom_Header_Name()
        {
            // Arrange
            var config = new ApiKeyConfiguration
            {
                Header = "X-Custom-Key",
                Value = TestApiKey
            };
            var handler = new ApiKeyAuthHandler(config);
            var context = CreateHttpContext();
            context.Request.Headers["X-Custom-Key"] = TestApiKey;

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public async Task Key_Comparison_Is_Case_Sensitive()
        {
            // Arrange
            var handler = new ApiKeyAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();
            context.Request.Headers[DefaultHeaderName] = TestApiKey.ToUpperInvariant();

            // Act
            var result = await handler.AuthenticateAsync(context, TestContext.Current.CancellationToken);

            // Assert
            result.IsAuthenticated.Should().BeFalse();
            result.FailureReason.Should().Be("Invalid API key");
        }
    }

    public class ChallengeAsyncTests
    {
        [Fact]
        public async Task Sets_401_Status_Code()
        {
            // Arrange
            var handler = new ApiKeyAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task Sets_WWW_Authenticate_Header()
        {
            // Arrange
            var handler = new ApiKeyAuthHandler(CreateDefaultConfig());
            var context = CreateHttpContext();

            // Act
            await handler.ChallengeAsync(context, TestContext.Current.CancellationToken);

            // Assert
            context.Response.Headers["WWW-Authenticate"].ToString()
                .Should().Contain("ApiKey")
                .And.Contain(DefaultHeaderName);
        }
    }

    public class SchemeNameTests
    {
        [Fact]
        public void Returns_ApiKey()
        {
            // Arrange
            var handler = new ApiKeyAuthHandler(CreateDefaultConfig());

            // Act & Assert
            handler.SchemeName.Should().Be("ApiKey");
        }
    }
}
