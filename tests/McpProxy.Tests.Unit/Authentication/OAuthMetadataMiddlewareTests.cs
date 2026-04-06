using McpProxy.Sdk.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace McpProxy.Tests.Unit.Authentication;

public class OAuthMetadataRegistryTests
{
    public class RegisterTests
    {
        [Fact]
        public void Registers_Backend_With_OAuth_Support()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            var probeResult = new OAuthProbeResult
            {
                BackendUrl = "https://example.com",
                SupportsOAuthAuthorizationServer = true,
                SupportsOpenIdConfiguration = true,
                OAuthAuthorizationServerMetadata = """{"issuer": "test"}""",
                OpenIdConfigurationMetadata = """{"issuer": "test"}"""
            };

            // Act
            registry.Register("teams", "https://example.com", probeResult);

            // Assert
            registry.GetPrimaryOAuthBackendUrl().Should().Be("https://example.com");
            registry.GetAllBackends().Should().ContainKey("teams");
            registry.GetAllBackends()["teams"].BackendUrl.Should().Be("https://example.com");
            registry.GetAllBackends()["teams"].ProbeResult.Should().Be(probeResult);
        }

        [Fact]
        public void Does_Not_Register_Backend_Without_OAuth_Support()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            var probeResult = OAuthProbeResult.NoSupport("https://example.com");

            // Act
            registry.Register("teams", "https://example.com", probeResult);

            // Assert
            registry.GetPrimaryOAuthBackendUrl().Should().BeNull();
            registry.GetAllBackends().Should().BeEmpty();
        }

        [Fact]
        public void First_Registered_Backend_Becomes_Primary()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            var probeResult1 = new OAuthProbeResult
            {
                BackendUrl = "https://first.example.com",
                SupportsOAuthAuthorizationServer = true
            };
            var probeResult2 = new OAuthProbeResult
            {
                BackendUrl = "https://second.example.com",
                SupportsOAuthAuthorizationServer = true
            };

            // Act
            registry.Register("first", "https://first.example.com", probeResult1);
            registry.Register("second", "https://second.example.com", probeResult2);

            // Assert
            registry.GetPrimaryOAuthBackendUrl().Should().Be("https://first.example.com");
            registry.GetAllBackends().Should().HaveCount(2);
        }

        [Fact]
        public void Overwrites_Existing_Backend_With_Same_Name()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            var probeResult1 = new OAuthProbeResult
            {
                BackendUrl = "https://old.example.com",
                SupportsOAuthAuthorizationServer = true
            };
            var probeResult2 = new OAuthProbeResult
            {
                BackendUrl = "https://new.example.com",
                SupportsOAuthAuthorizationServer = true
            };

            // Act
            registry.Register("teams", "https://old.example.com", probeResult1);
            registry.Register("teams", "https://new.example.com", probeResult2);

            // Assert
            registry.GetAllBackends().Should().HaveCount(1);
            registry.GetAllBackends()["teams"].BackendUrl.Should().Be("https://new.example.com");
        }

        [Fact]
        public void Throws_When_ServerName_Is_Null()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            var probeResult = new OAuthProbeResult
            {
                BackendUrl = "https://example.com",
                SupportsOAuthAuthorizationServer = true
            };

            // Act
            var act = () => registry.Register(null!, "https://example.com", probeResult);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Throws_When_BackendUrl_Is_Null()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            var probeResult = new OAuthProbeResult
            {
                BackendUrl = "https://example.com",
                SupportsOAuthAuthorizationServer = true
            };

            // Act
            var act = () => registry.Register("teams", null!, probeResult);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Throws_When_ProbeResult_Is_Null()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();

            // Act
            var act = () => registry.Register("teams", "https://example.com", null!);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }
    }

    public class NoSupportTests
    {
        [Fact]
        public void NoSupport_Has_SupportsOAuthProtectedResource_False()
        {
            // Act
            var result = OAuthProbeResult.NoSupport("https://example.com");

            // Assert
            result.SupportsOAuthProtectedResource.Should().BeFalse();
            result.OAuthProtectedResourceMetadata.Should().BeNull();
        }
    }

    public class GetPrimaryOAuthBackendUrlTests
    {
        [Fact]
        public void Returns_Null_When_No_Backends_Registered()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();

            // Act
            var result = registry.GetPrimaryOAuthBackendUrl();

            // Assert
            result.Should().BeNull();
        }
    }

    public class GetPrimaryProbeResultTests
    {
        [Fact]
        public void Returns_Null_When_No_Backends_Registered()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();

            // Act
            var result = registry.GetPrimaryProbeResult();

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void Returns_ProbeResult_Of_First_Registered_Backend()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            var probeResult = new OAuthProbeResult
            {
                BackendUrl = "https://example.com",
                SupportsOAuthProtectedResource = true,
                OAuthProtectedResourceMetadata = """{"resource":"https://example.com","authorization_servers":["https://login.microsoftonline.com/organizations/v2.0"]}"""
            };

            // Act
            registry.Register("teams", "https://example.com", probeResult);

            // Assert
            registry.GetPrimaryProbeResult().Should().BeSameAs(probeResult);
        }

        [Fact]
        public void Returns_First_ProbeResult_Even_After_Multiple_Registrations()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            var firstResult = new OAuthProbeResult
            {
                BackendUrl = "https://first.example.com",
                SupportsOAuthAuthorizationServer = true
            };
            var secondResult = new OAuthProbeResult
            {
                BackendUrl = "https://second.example.com",
                SupportsOAuthProtectedResource = true,
                OAuthProtectedResourceMetadata = """{"resource":"x"}"""
            };

            // Act
            registry.Register("first", "https://first.example.com", firstResult);
            registry.Register("second", "https://second.example.com", secondResult);

            // Assert
            registry.GetPrimaryProbeResult().Should().BeSameAs(firstResult);
        }
    }

    public class GetAllBackendsTests
    {
        [Fact]
        public void Returns_Empty_When_No_Backends_Registered()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();

            // Act
            var result = registry.GetAllBackends();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void Returns_All_Registered_Backends()
        {
            // Arrange
            var registry = new OAuthMetadataRegistry();
            registry.Register("teams", "https://teams.example.com", new OAuthProbeResult
            {
                BackendUrl = "https://teams.example.com",
                SupportsOAuthAuthorizationServer = true
            });
            registry.Register("graph", "https://graph.example.com", new OAuthProbeResult
            {
                BackendUrl = "https://graph.example.com",
                SupportsOpenIdConfiguration = true
            });

            // Act
            var result = registry.GetAllBackends();

            // Assert
            result.Should().HaveCount(2);
            result.Should().ContainKey("teams");
            result.Should().ContainKey("graph");
        }
    }
}

public class OAuthMetadataProxyMiddlewareTests
{
    private readonly ILogger<OAuthMetadataProxyMiddleware> _logger;

    public OAuthMetadataProxyMiddlewareTests()
    {
        _logger = Substitute.For<ILogger<OAuthMetadataProxyMiddleware>>();
    }

    private static IOAuthMetadataRegistry CreateRegistryWithBackend(string? backendUrl = null)
    {
        var registry = Substitute.For<IOAuthMetadataRegistry>();
        registry.GetPrimaryOAuthBackendUrl().Returns(backendUrl);
        return registry;
    }

    private static DefaultHttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    public class InvokeAsyncTests : OAuthMetadataProxyMiddlewareTests
    {
        [Fact]
        public async Task Calls_Next_When_Path_Is_Not_OAuth()
        {
            // Arrange
            var nextCalled = false;
            var middleware = new OAuthMetadataProxyMiddleware(
                next: _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                logger: _logger);

            var context = CreateHttpContext("/api/test");
            var registry = CreateRegistryWithBackend("https://example.com");

            // Act
            await middleware.InvokeAsync(context, registry);

            // Assert
            nextCalled.Should().BeTrue();
        }

        [Theory]
        [InlineData("/.well-known/oauth-authorization-server")]
        [InlineData("/.well-known/openid-configuration")]
        public async Task Calls_Next_When_OAuth_Path_But_No_Backend_Registered(string path)
        {
            // Arrange
            var nextCalled = false;
            var middleware = new OAuthMetadataProxyMiddleware(
                next: _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                logger: _logger);

            var context = CreateHttpContext(path);
            var registry = CreateRegistryWithBackend(null);

            // Act
            await middleware.InvokeAsync(context, registry);

            // Assert
            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task Does_Not_Call_Next_When_OAuth_Path_With_Backend()
        {
            // Arrange
            var nextCalled = false;
            var middleware = new OAuthMetadataProxyMiddleware(
                next: _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                logger: _logger);

            var context = CreateHttpContext("/.well-known/oauth-authorization-server");
            var registry = CreateRegistryWithBackend("https://example.com");

            // Act - This will fail because we can't mock HttpClient in this test
            // But we can verify the middleware attempts to handle OAuth path
            try
            {
                await middleware.InvokeAsync(context, registry);
            }
            catch (InvalidOperationException)
            {
                // Expected - HttpClient fails because backend doesn't exist
            }

            // Assert
            nextCalled.Should().BeFalse();
        }

        [Fact]
        public async Task Ignores_Case_When_Matching_OAuth_Path()
        {
            // Arrange
            var nextCalled = false;
            var middleware = new OAuthMetadataProxyMiddleware(
                next: _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                logger: _logger);

            var context = CreateHttpContext("/.WELL-KNOWN/OAUTH-AUTHORIZATION-SERVER");
            var registry = CreateRegistryWithBackend("https://example.com");

            // Act
            try
            {
                await middleware.InvokeAsync(context, registry);
            }
            catch (InvalidOperationException)
            {
                // Expected - HttpClient fails
            }

            // Assert
            nextCalled.Should().BeFalse();
        }

        [Fact]
        public async Task Serves_ProtectedResource_Metadata_When_Backend_Supports_RFC9728()
        {
            // Arrange
            var protectedResourceMetadata = """{"resource":"https://backend.example.com/api","authorization_servers":["https://login.microsoftonline.com/organizations/v2.0"],"scopes_supported":["openid","profile"]}""";
            var probeResult = new OAuthProbeResult
            {
                BackendUrl = "https://backend.example.com/api",
                SupportsOAuthProtectedResource = true,
                OAuthProtectedResourceMetadata = protectedResourceMetadata
            };

            var registry = Substitute.For<IOAuthMetadataRegistry>();
            registry.GetPrimaryProbeResult().Returns(probeResult);
            registry.GetPrimaryOAuthBackendUrl().Returns("https://backend.example.com/api");

            var nextCalled = false;
            var middleware = new OAuthMetadataProxyMiddleware(
                next: _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                logger: _logger);

            var context = CreateHttpContext("/.well-known/oauth-protected-resource/mcp");
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost", 5100);

            // Act
            await middleware.InvokeAsync(context, registry);

            // Assert
            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(200);
            context.Response.ContentType.Should().Be("application/json");

            // Read the response body
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body);
            var responseBody = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

            // The resource field should be rewritten to point to the proxy
            responseBody.Should().Contain("\"resource\":\"http://localhost:5100/mcp\"");
            // Authorization servers should be preserved from the backend
            responseBody.Should().Contain("authorization_servers");
            responseBody.Should().Contain("https://login.microsoftonline.com/organizations/v2.0");
        }

        [Fact]
        public async Task Calls_Next_When_ProtectedResource_Path_But_No_RFC9728_Support()
        {
            // Arrange
            var probeResult = new OAuthProbeResult
            {
                BackendUrl = "https://backend.example.com",
                SupportsOAuthAuthorizationServer = true,
                SupportsOAuthProtectedResource = false
            };

            var registry = Substitute.For<IOAuthMetadataRegistry>();
            registry.GetPrimaryProbeResult().Returns(probeResult);
            registry.GetPrimaryOAuthBackendUrl().Returns((string?)null);

            var nextCalled = false;
            var middleware = new OAuthMetadataProxyMiddleware(
                next: _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                logger: _logger);

            var context = CreateHttpContext("/.well-known/oauth-protected-resource/mcp");

            // Act
            await middleware.InvokeAsync(context, registry);

            // Assert
            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task Calls_Next_When_ProtectedResource_Path_But_No_Backends_Registered()
        {
            // Arrange
            var registry = Substitute.For<IOAuthMetadataRegistry>();
            registry.GetPrimaryProbeResult().Returns((OAuthProbeResult?)null);
            registry.GetPrimaryOAuthBackendUrl().Returns((string?)null);

            var nextCalled = false;
            var middleware = new OAuthMetadataProxyMiddleware(
                next: _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                logger: _logger);

            var context = CreateHttpContext("/.well-known/oauth-protected-resource/mcp");

            // Act
            await middleware.InvokeAsync(context, registry);

            // Assert
            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task Returns_Cached_ProtectedResource_Metadata_On_Second_Request()
        {
            // Arrange
            var protectedResourceMetadata = """{"resource":"https://backend.example.com","authorization_servers":["https://auth.example.com"]}""";
            var probeResult = new OAuthProbeResult
            {
                BackendUrl = "https://backend.example.com",
                SupportsOAuthProtectedResource = true,
                OAuthProtectedResourceMetadata = protectedResourceMetadata
            };

            var registry = Substitute.For<IOAuthMetadataRegistry>();
            registry.GetPrimaryProbeResult().Returns(probeResult);

            var middleware = new OAuthMetadataProxyMiddleware(
                next: _ => Task.CompletedTask,
                logger: _logger);

            // First request
            var context1 = CreateHttpContext("/.well-known/oauth-protected-resource/mcp");
            context1.Request.Scheme = "http";
            context1.Request.Host = new HostString("localhost", 5100);
            await middleware.InvokeAsync(context1, registry);

            context1.Response.Headers["X-Cache"].ToString().Should().Be("MISS");

            // Second request - should be cached
            var context2 = CreateHttpContext("/.well-known/oauth-protected-resource/mcp");
            context2.Request.Scheme = "http";
            context2.Request.Host = new HostString("localhost", 5100);

            // Act
            await middleware.InvokeAsync(context2, registry);

            // Assert
            context2.Response.Headers["X-Cache"].ToString().Should().Be("HIT");
            context2.Response.StatusCode.Should().Be(200);
        }
    }
}

public class OAuthBackendInfoTests
{
    [Fact]
    public void Creates_OAuthBackendInfo_With_Required_Properties()
    {
        // Arrange & Act
        var probeResult = new OAuthProbeResult
        {
            BackendUrl = "https://example.com",
            SupportsOAuthAuthorizationServer = true
        };

        var info = new OAuthBackendInfo
        {
            ServerName = "teams",
            BackendUrl = "https://example.com",
            ProbeResult = probeResult
        };

        // Assert
        info.ServerName.Should().Be("teams");
        info.BackendUrl.Should().Be("https://example.com");
        info.ProbeResult.Should().Be(probeResult);
    }
}
