using McpProxy.Sdk.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace McpProxy.Tests.Unit.Authentication;

public class ForwardAuthorizationHandlerTests
{
    private readonly ILogger<ForwardAuthorizationHandler> _logger;

    public ForwardAuthorizationHandlerTests()
    {
        _logger = Substitute.For<ILogger<ForwardAuthorizationHandler>>();
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(string? authorizationHeader = null)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();

        if (authorizationHeader is not null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Authorization"] = authorizationHeader;
            accessor.HttpContext.Returns(httpContext);
        }
        else
        {
            accessor.HttpContext.Returns((HttpContext?)null);
        }

        return accessor;
    }

    private static IHttpContextAccessor CreateHttpContextAccessorWithEmptyHeader()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = new StringValues(string.Empty);
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    private static IHttpContextAccessor CreateHttpContextAccessorWithNoAuthHeader()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        // Don't add Authorization header
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    public class ConstructorTests : ForwardAuthorizationHandlerTests
    {
        [Fact]
        public void Creates_Handler_With_HttpContextAccessor()
        {
            // Arrange
            var accessor = CreateHttpContextAccessor("Bearer test-token");

            // Act
            var handler = new ForwardAuthorizationHandler(accessor, _logger);

            // Assert
            handler.Should().NotBeNull();
            handler.Dispose();
        }

        [Fact]
        public void Creates_Handler_With_InnerHandler()
        {
            // Arrange
            var accessor = CreateHttpContextAccessor("Bearer test-token");
            var innerHandler = new HttpClientHandler();

            // Act
            var handler = new ForwardAuthorizationHandler(accessor, _logger, innerHandler);

            // Assert
            handler.Should().NotBeNull();
            handler.Dispose();
        }

        [Fact]
        public void Throws_When_HttpContextAccessor_Is_Null()
        {
            // Arrange & Act
            var act = () => new ForwardAuthorizationHandler(null!, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpContextAccessor");
        }

        [Fact]
        public void Throws_When_Logger_Is_Null()
        {
            // Arrange
            var accessor = CreateHttpContextAccessor("Bearer test-token");

            // Act
            var act = () => new ForwardAuthorizationHandler(accessor, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }
    }

    public class SendAsyncTests : ForwardAuthorizationHandlerTests
    {
        [Fact]
        public async Task Forwards_Authorization_Header_When_Present()
        {
            // Arrange
            var token = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test";
            var accessor = CreateHttpContextAccessor(token);
            var mockHandler = new MockHttpMessageHandler(req =>
            {
                // Verify the Authorization header was forwarded
                req.Headers.Authorization.Should().NotBeNull();
                req.Headers.Authorization!.ToString().Should().Be(token);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            });

            var handler = new ForwardAuthorizationHandler(accessor, _logger, mockHandler);
            var client = new HttpClient(handler);

            // Act
            var response = await client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            handler.Dispose();
        }

        [Fact]
        public async Task Does_Not_Fail_When_HttpContext_Is_Null()
        {
            // Arrange
            var accessor = CreateHttpContextAccessor(null); // No HttpContext
            var mockHandler = new MockHttpMessageHandler(req =>
            {
                // Verify no Authorization header was added
                req.Headers.Authorization.Should().BeNull();
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            });

            var handler = new ForwardAuthorizationHandler(accessor, _logger, mockHandler);
            var client = new HttpClient(handler);

            // Act
            var response = await client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            handler.Dispose();
        }

        [Fact]
        public async Task Does_Not_Add_Header_When_Authorization_Is_Empty()
        {
            // Arrange
            var accessor = CreateHttpContextAccessorWithEmptyHeader();
            var mockHandler = new MockHttpMessageHandler(req =>
            {
                // Verify no Authorization header was added
                req.Headers.Authorization.Should().BeNull();
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            });

            var handler = new ForwardAuthorizationHandler(accessor, _logger, mockHandler);
            var client = new HttpClient(handler);

            // Act
            var response = await client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            handler.Dispose();
        }

        [Fact]
        public async Task Does_Not_Add_Header_When_Authorization_Header_Missing()
        {
            // Arrange
            var accessor = CreateHttpContextAccessorWithNoAuthHeader();
            var mockHandler = new MockHttpMessageHandler(req =>
            {
                // Verify no Authorization header was added
                req.Headers.Authorization.Should().BeNull();
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            });

            var handler = new ForwardAuthorizationHandler(accessor, _logger, mockHandler);
            var client = new HttpClient(handler);

            // Act
            var response = await client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            handler.Dispose();
        }

        [Theory]
        [InlineData("Bearer abc123")]
        [InlineData("Basic dXNlcm5hbWU6cGFzc3dvcmQ=")]
        [InlineData("ApiKey my-secret-key")]
        public async Task Forwards_Various_Authorization_Schemes(string authHeader)
        {
            // Arrange
            var accessor = CreateHttpContextAccessor(authHeader);
            var mockHandler = new MockHttpMessageHandler(req =>
            {
                req.Headers.TryGetValues("Authorization", out var values).Should().BeTrue();
                values.Should().Contain(authHeader);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            });

            var handler = new ForwardAuthorizationHandler(accessor, _logger, mockHandler);
            var client = new HttpClient(handler);

            // Act
            var response = await client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            handler.Dispose();
        }
    }

    /// <summary>
    /// Mock HTTP message handler for testing.
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
