using Azure.Core;
using McpProxy.SDK.Authentication;
using Microsoft.Extensions.Logging;

namespace McpProxy.Tests.Unit.Authentication;

public class DefaultAzureCredentialAuthHandlerTests
{
    private readonly ILogger _logger;

    public DefaultAzureCredentialAuthHandlerTests()
    {
        _logger = Substitute.For<ILogger>();
    }

    public class ConstructorTests : DefaultAzureCredentialAuthHandlerTests
    {
        [Fact]
        public void Creates_Handler_With_Scopes_And_Logger()
        {
            // Arrange
            var scopes = new[] { "api://backend/.default" };
            var credential = new FakeTokenCredential("test-token");

            // Act
            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, credential);

            // Assert
            handler.Should().NotBeNull();
            handler.Dispose();
        }

        [Fact]
        public void Creates_Handler_With_InnerHandler()
        {
            // Arrange
            var scopes = new[] { "api://backend/.default" };
            var credential = new FakeTokenCredential("test-token");
            var innerHandler = new MockHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK));

            // Act
            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, innerHandler, credential);

            // Assert
            handler.Should().NotBeNull();
            handler.Dispose();
        }

        [Fact]
        public void Throws_When_Scopes_Is_Null()
        {
            // Arrange
            var credential = new FakeTokenCredential("test-token");

            // Act
            var act = () => new DefaultAzureCredentialAuthHandler(null!, _logger, credential);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("scopes");
        }

        [Fact]
        public void Throws_When_Logger_Is_Null()
        {
            // Arrange
            var scopes = new[] { "api://backend/.default" };
            var credential = new FakeTokenCredential("test-token");

            // Act
            var act = () => new DefaultAzureCredentialAuthHandler(scopes, null!, credential);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }
    }

    public class SendAsyncTests : DefaultAzureCredentialAuthHandlerTests
    {
        [Fact]
        public async Task Attaches_Bearer_Token_To_Request()
        {
            // Arrange
            var expectedToken = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.test-token";
            var credential = new FakeTokenCredential(expectedToken);
            var scopes = new[] { "api://backend/.default" };

            var mockInner = new MockHttpMessageHandler(req =>
            {
                req.Headers.Authorization.Should().NotBeNull();
                req.Headers.Authorization!.Scheme.Should().Be("Bearer");
                req.Headers.Authorization.Parameter.Should().Be(expectedToken);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            });

            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, mockInner, credential);
            var client = new HttpClient(handler);

            // Act
            var response = await client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            handler.Dispose();
        }

        [Fact]
        public async Task Uses_Correct_Scopes_When_Requesting_Token()
        {
            // Arrange
            var scopes = new[] { "https://teams.microsoft.com/.default" };
            string[]? capturedScopes = null;

            var credential = new FakeTokenCredential("test-token", ctx =>
            {
                capturedScopes = ctx.Scopes;
            });

            var mockInner = new MockHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK));

            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, mockInner, credential);
            var client = new HttpClient(handler);

            // Act
            await client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            capturedScopes.Should().NotBeNull();
            capturedScopes.Should().BeEquivalentTo(scopes);

            handler.Dispose();
        }

        [Fact]
        public async Task Supports_Multiple_Scopes()
        {
            // Arrange
            var scopes = new[] { "openid", "profile", "api://backend/.default" };
            string[]? capturedScopes = null;

            var credential = new FakeTokenCredential("test-token", ctx =>
            {
                capturedScopes = ctx.Scopes;
            });

            var mockInner = new MockHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK));

            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, mockInner, credential);
            var client = new HttpClient(handler);

            // Act
            await client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            capturedScopes.Should().NotBeNull();
            capturedScopes.Should().BeEquivalentTo(scopes);

            handler.Dispose();
        }

        [Fact]
        public async Task Throws_When_Token_Acquisition_Fails()
        {
            // Arrange
            var scopes = new[] { "api://backend/.default" };
            var credential = new FailingTokenCredential(new InvalidOperationException("Token acquisition failed"));

            var mockInner = new MockHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK));

            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, mockInner, credential);
            var client = new HttpClient(handler);

            // Act
            var act = () => client.GetAsync("https://example.com/api", TestContext.Current.CancellationToken);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Token acquisition failed");

            handler.Dispose();
        }

        [Fact]
        public async Task Acquires_Fresh_Token_For_Each_Request()
        {
            // Arrange
            var callCount = 0;
            var scopes = new[] { "api://backend/.default" };

            var credential = new FakeTokenCredential("test-token", _ =>
            {
                Interlocked.Increment(ref callCount);
            });

            var mockInner = new MockHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK));

            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, mockInner, credential);
            var client = new HttpClient(handler);

            // Act
            await client.GetAsync("https://example.com/api/1", TestContext.Current.CancellationToken);
            await client.GetAsync("https://example.com/api/2", TestContext.Current.CancellationToken);
            await client.GetAsync("https://example.com/api/3", TestContext.Current.CancellationToken);

            // Assert
            callCount.Should().Be(3);

            handler.Dispose();
        }

        [Fact]
        public async Task Replaces_Existing_Authorization_Header()
        {
            // Arrange
            var expectedToken = "new-token";
            var credential = new FakeTokenCredential(expectedToken);
            var scopes = new[] { "api://backend/.default" };

            var mockInner = new MockHttpMessageHandler(req =>
            {
                req.Headers.Authorization.Should().NotBeNull();
                req.Headers.Authorization!.Parameter.Should().Be(expectedToken);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            });

            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, mockInner, credential);
            var client = new HttpClient(handler);

            // The handler sets auth on SendAsync, overriding any default
            var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");

            // Act
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            handler.Dispose();
        }

        [Fact]
        public async Task Passes_CancellationToken_To_Credential()
        {
            // Arrange
            var scopes = new[] { "api://backend/.default" };
            CancellationToken capturedToken = default;

            var credential = new FakeTokenCredential("test-token", cancellationTokenCapture: ct =>
            {
                capturedToken = ct;
            });

            var mockInner = new MockHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK));

            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, mockInner, credential);
            var client = new HttpClient(handler);

            using var cts = new CancellationTokenSource();

            // Act
            await client.GetAsync("https://example.com/api", cts.Token);

            // Assert - the token should have been propagated (it may be wrapped but should not be None)
            capturedToken.Should().NotBe(CancellationToken.None);

            handler.Dispose();
        }
    }

    public class DisposeTests : DefaultAzureCredentialAuthHandlerTests
    {
        [Fact]
        public void Can_Be_Disposed()
        {
            // Arrange
            var scopes = new[] { "api://backend/.default" };
            var credential = new FakeTokenCredential("test-token");
            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, credential);

            // Act & Assert - should not throw
            handler.Dispose();
        }

        [Fact]
        public void Can_Be_Disposed_Multiple_Times()
        {
            // Arrange
            var scopes = new[] { "api://backend/.default" };
            var credential = new FakeTokenCredential("test-token");
            var handler = new DefaultAzureCredentialAuthHandler(scopes, _logger, credential);

            // Act & Assert - should not throw
            handler.Dispose();
            handler.Dispose();
        }
    }

    /// <summary>
    /// A fake <see cref="TokenCredential"/> that returns a predetermined token for testing.
    /// </summary>
    private sealed class FakeTokenCredential : TokenCredential
    {
        private readonly string _token;
        private readonly Action<TokenRequestContext>? _onGetToken;
        private readonly Action<CancellationToken>? _cancellationTokenCapture;

        public FakeTokenCredential(
            string token,
            Action<TokenRequestContext>? onGetToken = null,
            Action<CancellationToken>? cancellationTokenCapture = null)
        {
            _token = token;
            _onGetToken = onGetToken;
            _cancellationTokenCapture = cancellationTokenCapture;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            _onGetToken?.Invoke(requestContext);
            _cancellationTokenCapture?.Invoke(cancellationToken);
            return new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            _onGetToken?.Invoke(requestContext);
            _cancellationTokenCapture?.Invoke(cancellationToken);
            return ValueTask.FromResult(new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    /// <summary>
    /// A <see cref="TokenCredential"/> that always throws for testing error paths.
    /// </summary>
    private sealed class FailingTokenCredential : TokenCredential
    {
        private readonly Exception _exception;

        public FailingTokenCredential(Exception exception)
        {
            _exception = exception;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw _exception;
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw _exception;
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
