using McpProxy.SDK.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

namespace McpProxy.Tests.Unit.Authentication;

public class OAuthMetadataProxyEndpointsTests
{
    private const string BackendUrl = "https://backend.example.com/mcp";

    private static IHost CreateTestHostWithMockedHttpClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        bool useCaching = false,
        TimeSpan? cacheDuration = null)
    {
        return new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();

                    // Register a mocked HttpClientFactory
                    // Note: We use ReturnsLazily to create a new HttpClient for each call,
                    // since the endpoint disposes the client after use
                    var mockFactory = Substitute.For<IHttpClientFactory>();
                    mockFactory
                        .CreateClient("OAuthMetadataProxy")
                        .Returns(_ => new HttpClient(new MockHttpMessageHandler(handler)));
                    services.AddSingleton(mockFactory);
                });
                webBuilder.Configure(app =>
                {
                    var logger = app.ApplicationServices.GetService<ILogger<OAuthMetadataProxyEndpointsTests>>();
                    var httpClientFactory = app.ApplicationServices.GetService<IHttpClientFactory>();

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        if (useCaching)
                        {
                            endpoints.MapCachedProxiedOAuthMetadata(
                                BackendUrl,
                                cacheDuration,
                                httpClientFactory,
                                logger);
                        }
                        else
                        {
                            endpoints.MapProxiedOAuthMetadata(
                                BackendUrl,
                                httpClientFactory,
                                logger);
                        }
                    });
                });
            })
            .Build();
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Content = new StringContent(json);
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return response;
    }

    public class BasicProxyTests
    {
        [Fact]
        public async Task Proxies_OAuthAuthorizationServer_From_Backend()
        {
            // Arrange
            var expectedMetadata = """
                {
                    "issuer": "https://login.microsoftonline.com/tenant-id/v2.0",
                    "authorization_endpoint": "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/authorize",
                    "token_endpoint": "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token"
                }
                """;

            HttpRequestMessage? capturedRequest = null;
            using var host = CreateTestHostWithMockedHttpClient(req =>
            {
                capturedRequest = req;
                return CreateJsonResponse(HttpStatusCode.OK, expectedMetadata);
            });
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync(
                "/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            content.Should().Contain("authorization_endpoint");
            content.Should().Contain("token_endpoint");

            // Verify the request was made to the correct URL
            capturedRequest.Should().NotBeNull();
            capturedRequest!.RequestUri!.ToString()
                .Should().Be($"{BackendUrl}/.well-known/oauth-authorization-server");
        }

        [Fact]
        public async Task Proxies_OpenIdConfiguration_From_Backend()
        {
            // Arrange
            var expectedMetadata = """
                {
                    "issuer": "https://login.microsoftonline.com/tenant-id/v2.0",
                    "jwks_uri": "https://login.microsoftonline.com/tenant-id/discovery/v2.0/keys"
                }
                """;

            HttpRequestMessage? capturedRequest = null;
            using var host = CreateTestHostWithMockedHttpClient(req =>
            {
                capturedRequest = req;
                return CreateJsonResponse(HttpStatusCode.OK, expectedMetadata);
            });
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync(
                "/.well-known/openid-configuration",
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            content.Should().Contain("jwks_uri");

            capturedRequest.Should().NotBeNull();
            capturedRequest!.RequestUri!.ToString()
                .Should().Be($"{BackendUrl}/.well-known/openid-configuration");
        }

        [Fact]
        public async Task Forwards_Authorization_Header_To_Backend()
        {
            // Arrange
            HttpRequestMessage? capturedRequest = null;
            using var host = CreateTestHostWithMockedHttpClient(req =>
            {
                capturedRequest = req;
                return CreateJsonResponse(HttpStatusCode.OK, "{}");
            });
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-authorization-server");
            request.Headers.Add("Authorization", "Bearer test-token-123");
            await client.SendAsync(request, TestContext.Current.CancellationToken);

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest!.Headers.Authorization.Should().NotBeNull();
            capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
            capturedRequest.Headers.Authorization!.Parameter.Should().Be("test-token-123");
        }

        [Fact]
        public async Task Returns_502_When_Backend_Request_Fails()
        {
            // Arrange
            using var host = CreateTestHostWithMockedHttpClient(_ =>
                throw new HttpRequestException("Connection refused"));
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync(
                "/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            content.Should().Contain("Failed to fetch OAuth metadata");
        }

        [Fact]
        public async Task Proxies_Backend_Error_Status_Codes()
        {
            // Arrange
            using var host = CreateTestHostWithMockedHttpClient(_ =>
                CreateJsonResponse(HttpStatusCode.Unauthorized, """{"error": "unauthorized"}"""));
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync(
                "/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    public class CachingTests
    {
        [Fact]
        public async Task Cached_Endpoint_Returns_Cached_Response_On_Second_Request()
        {
            // Arrange
            var callCount = 0;
            using var host = CreateTestHostWithMockedHttpClient(
                _ =>
                {
                    callCount++;
                    return CreateJsonResponse(HttpStatusCode.OK, $"{{\"call\": {callCount}}}");
                },
                useCaching: true,
                cacheDuration: TimeSpan.FromMinutes(5));
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act - First request (cache miss)
            var response1 = await client.GetAsync(
                "/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);
            var content1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // Act - Second request (cache hit)
            var response2 = await client.GetAsync(
                "/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);
            var content2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // Assert
            response1.Headers.TryGetValues("X-Cache", out var cacheHeader1);
            cacheHeader1.Should().Contain("MISS");

            response2.Headers.TryGetValues("X-Cache", out var cacheHeader2);
            cacheHeader2.Should().Contain("HIT");

            // Content should be the same (from cache)
            content1.Should().Be(content2);

            // Backend should only be called once
            callCount.Should().Be(1);
        }

        [Fact]
        public async Task Cached_Endpoint_Does_Not_Cache_Error_Responses()
        {
            // Arrange
            var callCount = 0;
            using var host = CreateTestHostWithMockedHttpClient(
                _ =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First call returns error
                        return CreateJsonResponse(HttpStatusCode.InternalServerError, """{"error": "server error"}""");
                    }

                    // Second call succeeds
                    return CreateJsonResponse(HttpStatusCode.OK, """{"success": true}""");
                },
                useCaching: true,
                cacheDuration: TimeSpan.FromMinutes(5));
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act - First request (error, should not cache)
            var response1 = await client.GetAsync(
                "/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);

            // Act - Second request (success)
            var response2 = await client.GetAsync(
                "/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);
            var content2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // Assert
            response1.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            response2.StatusCode.Should().Be(HttpStatusCode.OK);
            content2.Should().Contain("success");

            // Backend should be called twice (error not cached)
            callCount.Should().Be(2);
        }

        [Fact]
        public async Task Cached_Endpoint_Returns_502_On_Backend_Failure()
        {
            // Arrange
            using var host = CreateTestHostWithMockedHttpClient(
                _ => throw new HttpRequestException("Connection refused"),
                useCaching: true);
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync(
                "/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            content.Should().Contain("Failed to fetch OAuth metadata");
        }
    }

    public class ValidationTests
    {
        [Fact]
        public void MapProxiedOAuthMetadata_Throws_When_BackendUrl_Is_Null()
        {
            // Arrange & Act & Assert
            using var host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services => services.AddRouting());
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            var act = () => endpoints.MapProxiedOAuthMetadata(null!);
                            act.Should().Throw<ArgumentException>();
                        });
                    });
                })
                .Build();
        }

        [Fact]
        public void MapProxiedOAuthMetadata_Throws_When_BackendUrl_Is_Empty()
        {
            // Arrange & Act & Assert
            using var host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services => services.AddRouting());
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            var act = () => endpoints.MapProxiedOAuthMetadata("");
                            act.Should().Throw<ArgumentException>();
                        });
                    });
                })
                .Build();
        }

        [Fact]
        public void MapCachedProxiedOAuthMetadata_Throws_When_BackendUrl_Is_Null()
        {
            // Arrange & Act & Assert
            using var host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services => services.AddRouting());
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            var act = () => endpoints.MapCachedProxiedOAuthMetadata(null!);
                            act.Should().Throw<ArgumentException>();
                        });
                    });
                })
                .Build();
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
