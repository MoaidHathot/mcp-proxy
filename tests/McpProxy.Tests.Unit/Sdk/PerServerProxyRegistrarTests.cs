using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Proxy;
using McpProxy.Sdk.Sdk;
using Microsoft.Extensions.Logging;

namespace McpProxy.Tests.Unit.Sdk;

/// <summary>
/// Tests for <see cref="IPerServerProxyRegistrar"/> implementations including
/// route-to-server mapping via <see cref="IPerServerProxyRegistrar.TryGetProxyForRoute"/>.
/// </summary>
public class PerServerProxyRegistrarTests
{
    private static SingleServerProxy CreateProxy(string serverName) =>
        new(Substitute.For<ILogger<SingleServerProxy>>(),
            new McpClientManager(
                Substitute.For<ILogger<McpClientManager>>(),
                Substitute.For<ILoggerFactory>()),
            serverName,
            new ServerConfiguration { Type = ServerTransportType.Stdio, Command = "mock" });

    public class TryGetProxyForRouteTests
    {
        [Fact]
        public void Returns_Proxy_For_Exact_Route_Match()
        {
            // Arrange
            var proxyA = CreateProxy("server-a");
            var proxyB = CreateProxy("server-b");
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["server-a"] = proxyA,
                ["server-b"] = proxyB
            };
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["/mcp/server-a"] = proxyA,
                ["/mcp/server-b"] = proxyB
            };
            var registrar = new PerServerProxyRegistrar(proxies, routeToProxy);

            // Act
            var result = registrar.TryGetProxyForRoute("/mcp/server-a");

            // Assert
            result.Should().BeSameAs(proxyA);
        }

        [Fact]
        public void Returns_Proxy_For_Subpath_Match()
        {
            // Arrange
            var proxyA = CreateProxy("server-a");
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["server-a"] = proxyA
            };
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["/mcp/server-a"] = proxyA
            };
            var registrar = new PerServerProxyRegistrar(proxies, routeToProxy);

            // Act — MCP SDK might add subpaths like /mcp/server-a/mcp
            var result = registrar.TryGetProxyForRoute("/mcp/server-a/mcp");

            // Assert
            result.Should().BeSameAs(proxyA);
        }

        [Fact]
        public void Returns_Null_For_Unified_Route()
        {
            // Arrange
            var proxyA = CreateProxy("server-a");
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["server-a"] = proxyA
            };
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["/mcp/server-a"] = proxyA
            };
            var registrar = new PerServerProxyRegistrar(proxies, routeToProxy);

            // Act — unified endpoint path should NOT match any per-server route
            var result = registrar.TryGetProxyForRoute("/mcp");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void Returns_Null_For_Unknown_Route()
        {
            // Arrange
            var proxyA = CreateProxy("server-a");
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["server-a"] = proxyA
            };
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["/mcp/server-a"] = proxyA
            };
            var registrar = new PerServerProxyRegistrar(proxies, routeToProxy);

            // Act
            var result = registrar.TryGetProxyForRoute("/mcp/unknown");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void Route_Matching_Is_Case_Insensitive()
        {
            // Arrange
            var proxyA = CreateProxy("server-a");
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["server-a"] = proxyA
            };
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["/mcp/server-a"] = proxyA
            };
            var registrar = new PerServerProxyRegistrar(proxies, routeToProxy);

            // Act
            var result = registrar.TryGetProxyForRoute("/MCP/Server-A");

            // Assert
            result.Should().BeSameAs(proxyA);
        }

        [Fact]
        public void Custom_Route_Matches_Correctly()
        {
            // Arrange
            var proxyA = CreateProxy("server-a");
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["server-a"] = proxyA
            };
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["/custom/route"] = proxyA  // custom route, not default
            };
            var registrar = new PerServerProxyRegistrar(proxies, routeToProxy);

            // Act
            var result = registrar.TryGetProxyForRoute("/custom/route");

            // Assert
            result.Should().BeSameAs(proxyA);
        }

        [Fact]
        public void Multiple_Servers_Resolve_To_Correct_Proxy()
        {
            // Arrange
            var proxyA = CreateProxy("server-a");
            var proxyB = CreateProxy("server-b");
            var proxyC = CreateProxy("server-c");
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["server-a"] = proxyA,
                ["server-b"] = proxyB,
                ["server-c"] = proxyC
            };
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["/mcp/server-a"] = proxyA,
                ["/mcp/server-b"] = proxyB,
                ["/mcp/server-c"] = proxyC
            };
            var registrar = new PerServerProxyRegistrar(proxies, routeToProxy);

            // Act & Assert
            registrar.TryGetProxyForRoute("/mcp/server-a").Should().BeSameAs(proxyA);
            registrar.TryGetProxyForRoute("/mcp/server-b").Should().BeSameAs(proxyB);
            registrar.TryGetProxyForRoute("/mcp/server-c").Should().BeSameAs(proxyC);
        }
    }

    public class GetProxyTests
    {
        [Fact]
        public void Returns_Proxy_For_Known_Server()
        {
            // Arrange
            var proxyA = CreateProxy("server-a");
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase)
            {
                ["server-a"] = proxyA
            };
            var registrar = new PerServerProxyRegistrar(proxies, []);

            // Act
            var result = registrar.GetProxy("server-a");

            // Assert
            result.Should().BeSameAs(proxyA);
        }

        [Fact]
        public void Throws_For_Unknown_Server()
        {
            // Arrange
            var registrar = new PerServerProxyRegistrar(
                new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase),
                []);

            // Act
            var act = () => registrar.GetProxy("unknown");

            // Assert
            act.Should().Throw<KeyNotFoundException>()
                .WithMessage("*unknown*");
        }
    }

    public class NoOpRegistrarTests
    {
        [Fact]
        public void TryGetProxyForRoute_Always_Returns_Null()
        {
            // Arrange
            var registrar = new NoOpPerServerProxyRegistrar();

            // Act & Assert
            registrar.TryGetProxyForRoute("/mcp/server-a").Should().BeNull();
            registrar.TryGetProxyForRoute("/anything").Should().BeNull();
        }

        [Fact]
        public void Proxies_Is_Empty()
        {
            // Arrange
            var registrar = new NoOpPerServerProxyRegistrar();

            // Act & Assert
            registrar.Proxies.Should().BeEmpty();
        }

        [Fact]
        public void GetProxy_Throws_InvalidOperationException()
        {
            // Arrange
            var registrar = new NoOpPerServerProxyRegistrar();

            // Act
            var act = () => registrar.GetProxy("anything");

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*not enabled*");
        }
    }
}
