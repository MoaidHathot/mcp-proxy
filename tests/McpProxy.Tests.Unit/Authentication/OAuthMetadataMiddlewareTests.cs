using McpProxy.Core.Authentication;
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
