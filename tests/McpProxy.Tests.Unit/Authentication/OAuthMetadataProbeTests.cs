using McpProxy.Core.Authentication;
using Microsoft.Extensions.Logging;
using System.Net;

namespace McpProxy.Tests.Unit.Authentication;

public class OAuthMetadataProbeTests
{
    private readonly ILogger<OAuthMetadataProbe> _logger;

    public OAuthMetadataProbeTests()
    {
        _logger = Substitute.For<ILogger<OAuthMetadataProbe>>();
    }

    private static IHttpClientFactory CreateHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var mockHandler = new MockHttpMessageHandler(handler);
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(mockHandler));
        return factory;
    }

    private static IHttpClientFactory CreateHttpClientFactoryWithResponses(
        Dictionary<string, (HttpStatusCode StatusCode, string? Content)> responses)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;

            if (responses.TryGetValue(path, out var response))
            {
                var httpResponse = new HttpResponseMessage(response.StatusCode);
                if (response.Content is not null)
                {
                    httpResponse.Content = new StringContent(response.Content, System.Text.Encoding.UTF8, "application/json");
                }
                return httpResponse;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(mockHandler));
        return factory;
    }

    public class ConstructorTests : OAuthMetadataProbeTests
    {
        [Fact]
        public void Creates_Probe_With_Valid_Dependencies()
        {
            // Arrange
            var factory = CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));

            // Act
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Assert
            probe.Should().NotBeNull();
        }

        [Fact]
        public void Throws_When_HttpClientFactory_Is_Null()
        {
            // Act
            var act = () => new OAuthMetadataProbe(null!, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClientFactory");
        }

        [Fact]
        public void Throws_When_Logger_Is_Null()
        {
            // Arrange
            var factory = CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));

            // Act
            var act = () => new OAuthMetadataProbe(factory, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }
    }

    public class ProbeAsyncTests : OAuthMetadataProbeTests
    {
        [Fact]
        public async Task Returns_NoSupport_When_Both_Endpoints_Return_404()
        {
            // Arrange
            var factory = CreateHttpClientFactoryWithResponses(new Dictionary<string, (HttpStatusCode, string?)>
            {
                ["/.well-known/oauth-authorization-server"] = (HttpStatusCode.NotFound, null),
                ["/.well-known/openid-configuration"] = (HttpStatusCode.NotFound, null)
            });
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var result = await probe.ProbeAsync("https://example.com", TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.BackendUrl.Should().Be("https://example.com");
            result.SupportsOAuth.Should().BeFalse();
            result.SupportsOAuthAuthorizationServer.Should().BeFalse();
            result.SupportsOpenIdConfiguration.Should().BeFalse();
            result.OAuthAuthorizationServerMetadata.Should().BeNull();
            result.OpenIdConfigurationMetadata.Should().BeNull();
        }

        [Fact]
        public async Task Returns_Support_When_OAuthAuthorizationServer_Returns_200()
        {
            // Arrange
            var oauthMetadata = """{"issuer": "https://login.microsoftonline.com/test"}""";
            var factory = CreateHttpClientFactoryWithResponses(new Dictionary<string, (HttpStatusCode, string?)>
            {
                ["/.well-known/oauth-authorization-server"] = (HttpStatusCode.OK, oauthMetadata),
                ["/.well-known/openid-configuration"] = (HttpStatusCode.NotFound, null)
            });
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var result = await probe.ProbeAsync("https://example.com", TestContext.Current.CancellationToken);

            // Assert
            result.SupportsOAuth.Should().BeTrue();
            result.SupportsOAuthAuthorizationServer.Should().BeTrue();
            result.SupportsOpenIdConfiguration.Should().BeFalse();
            result.OAuthAuthorizationServerMetadata.Should().Be(oauthMetadata);
            result.OpenIdConfigurationMetadata.Should().BeNull();
        }

        [Fact]
        public async Task Returns_Support_When_OpenIdConfiguration_Returns_200()
        {
            // Arrange
            var openIdMetadata = """{"issuer": "https://login.microsoftonline.com/test", "jwks_uri": "https://example.com/keys"}""";
            var factory = CreateHttpClientFactoryWithResponses(new Dictionary<string, (HttpStatusCode, string?)>
            {
                ["/.well-known/oauth-authorization-server"] = (HttpStatusCode.NotFound, null),
                ["/.well-known/openid-configuration"] = (HttpStatusCode.OK, openIdMetadata)
            });
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var result = await probe.ProbeAsync("https://example.com", TestContext.Current.CancellationToken);

            // Assert
            result.SupportsOAuth.Should().BeTrue();
            result.SupportsOAuthAuthorizationServer.Should().BeFalse();
            result.SupportsOpenIdConfiguration.Should().BeTrue();
            result.OAuthAuthorizationServerMetadata.Should().BeNull();
            result.OpenIdConfigurationMetadata.Should().Be(openIdMetadata);
        }

        [Fact]
        public async Task Returns_Support_When_Both_Endpoints_Return_200()
        {
            // Arrange
            var oauthMetadata = """{"issuer": "https://login.microsoftonline.com/test"}""";
            var openIdMetadata = """{"issuer": "https://login.microsoftonline.com/test", "jwks_uri": "https://example.com/keys"}""";
            var factory = CreateHttpClientFactoryWithResponses(new Dictionary<string, (HttpStatusCode, string?)>
            {
                ["/.well-known/oauth-authorization-server"] = (HttpStatusCode.OK, oauthMetadata),
                ["/.well-known/openid-configuration"] = (HttpStatusCode.OK, openIdMetadata)
            });
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var result = await probe.ProbeAsync("https://example.com", TestContext.Current.CancellationToken);

            // Assert
            result.SupportsOAuth.Should().BeTrue();
            result.SupportsOAuthAuthorizationServer.Should().BeTrue();
            result.SupportsOpenIdConfiguration.Should().BeTrue();
            result.OAuthAuthorizationServerMetadata.Should().Be(oauthMetadata);
            result.OpenIdConfigurationMetadata.Should().Be(openIdMetadata);
        }

        [Fact]
        public async Task Handles_Trailing_Slash_In_Backend_Url()
        {
            // Arrange
            var oauthMetadata = """{"issuer": "https://example.com"}""";
            var factory = CreateHttpClientFactoryWithResponses(new Dictionary<string, (HttpStatusCode, string?)>
            {
                ["/.well-known/oauth-authorization-server"] = (HttpStatusCode.OK, oauthMetadata),
                ["/.well-known/openid-configuration"] = (HttpStatusCode.NotFound, null)
            });
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var result = await probe.ProbeAsync("https://example.com/", TestContext.Current.CancellationToken);

            // Assert
            result.BackendUrl.Should().Be("https://example.com");
            result.SupportsOAuth.Should().BeTrue();
        }

        [Fact]
        public async Task Returns_NoSupport_When_Request_Fails()
        {
            // Arrange
            var factory = Substitute.For<IHttpClientFactory>();
            var mockHandler = new MockHttpMessageHandler(_ => throw new HttpRequestException("Connection failed"));
            factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(mockHandler));
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var result = await probe.ProbeAsync("https://example.com", TestContext.Current.CancellationToken);

            // Assert
            result.SupportsOAuth.Should().BeFalse();
            result.SupportsOAuthAuthorizationServer.Should().BeFalse();
            result.SupportsOpenIdConfiguration.Should().BeFalse();
        }

        [Fact]
        public async Task Throws_When_Backend_Url_Is_Null()
        {
            // Arrange
            var factory = CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var act = () => probe.ProbeAsync(null!, TestContext.Current.CancellationToken);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Throws_When_Backend_Url_Is_Empty()
        {
            // Arrange
            var factory = CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var act = () => probe.ProbeAsync(string.Empty, TestContext.Current.CancellationToken);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        public async Task Returns_NoSupport_For_Non_Success_Status_Codes(HttpStatusCode statusCode)
        {
            // Arrange
            var factory = CreateHttpClientFactoryWithResponses(new Dictionary<string, (HttpStatusCode, string?)>
            {
                ["/.well-known/oauth-authorization-server"] = (statusCode, null),
                ["/.well-known/openid-configuration"] = (statusCode, null)
            });
            var probe = new OAuthMetadataProbe(factory, _logger);

            // Act
            var result = await probe.ProbeAsync("https://example.com", TestContext.Current.CancellationToken);

            // Assert
            result.SupportsOAuth.Should().BeFalse();
        }
    }

    public class NoSupportTests : OAuthMetadataProbeTests
    {
        [Fact]
        public void NoSupport_Creates_Result_With_Correct_BackendUrl()
        {
            // Act
            var result = OAuthProbeResult.NoSupport("https://example.com");

            // Assert
            result.BackendUrl.Should().Be("https://example.com");
            result.SupportsOAuth.Should().BeFalse();
            result.SupportsOAuthAuthorizationServer.Should().BeFalse();
            result.SupportsOpenIdConfiguration.Should().BeFalse();
            result.OAuthAuthorizationServerMetadata.Should().BeNull();
            result.OpenIdConfigurationMetadata.Should().BeNull();
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
