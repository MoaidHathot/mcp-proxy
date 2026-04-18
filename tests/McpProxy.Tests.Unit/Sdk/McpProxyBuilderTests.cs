using McpProxy.Abstractions;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Sdk;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Sdk;

public class McpProxyBuilderTests
{
    public class WithRoutingTests
    {
        [Fact]
        public void Sets_Routing_Mode_To_PerServer()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act
            builder.WithRouting(RoutingMode.PerServer, "/mcp");
            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Proxy.Routing.Mode.Should().Be(RoutingMode.PerServer);
            config.Configuration.Proxy.Routing.BasePath.Should().Be("/mcp");
        }

        [Fact]
        public void Sets_Routing_Mode_To_Unified()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act
            builder.WithRouting(RoutingMode.Unified);
            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Proxy.Routing.Mode.Should().Be(RoutingMode.Unified);
        }

        [Fact]
        public void Does_Not_Override_BasePath_When_Null()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
            // Set basePath first
            builder.WithRouting(RoutingMode.PerServer, "/initial");

            // Act - change mode without specifying basePath
            builder.WithRouting(RoutingMode.PerServer);
            var config = builder.BuildConfiguration();

            // Assert - basePath should be preserved
            config.Configuration.Proxy.Routing.BasePath.Should().Be("/initial");
        }

        [Fact]
        public void Returns_Builder_For_Chaining()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act
            var result = builder.WithRouting(RoutingMode.PerServer, "/mcp");

            // Assert
            result.Should().BeSameAs(builder);
        }
    }

    public class WithRoutingInterfaceTests
    {
        [Fact]
        public void Interface_Method_Sets_Routing_Mode()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
#pragma warning disable CA1859 // Intentionally testing through interface
            IMcpProxyBuilder interfaceRef = builder;
#pragma warning restore CA1859

            // Act
            interfaceRef.WithRouting(RoutingMode.PerServer, "/mcp");
            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Proxy.Routing.Mode.Should().Be(RoutingMode.PerServer);
            config.Configuration.Proxy.Routing.BasePath.Should().Be("/mcp");
        }
    }

    public class WithServerInfoTests
    {
        [Fact]
        public void Sets_Server_Info()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act
            builder.WithServerInfo("Test Proxy", "2.0.0", "Test instructions");
            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Proxy.ServerInfo.Name.Should().Be("Test Proxy");
            config.Configuration.Proxy.ServerInfo.Version.Should().Be("2.0.0");
            config.Configuration.Proxy.ServerInfo.Instructions.Should().Be("Test instructions");
        }
    }

    public class AddServerTests
    {
        [Fact]
        public void AddStdioServer_Adds_Configuration()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act
            builder.AddStdioServer("test", "echo", "hello").Build();
            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Mcp.Should().ContainKey("test");
            config.Configuration.Mcp["test"].Type.Should().Be(ServerTransportType.Stdio);
            config.Configuration.Mcp["test"].Command.Should().Be("echo");
        }

        [Fact]
        public void AddHttpServer_Adds_Configuration()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act
            builder.AddHttpServer("test", "https://example.com/mcp").Build();
            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Mcp.Should().ContainKey("test");
            config.Configuration.Mcp["test"].Type.Should().Be(ServerTransportType.Http);
            config.Configuration.Mcp["test"].Url.Should().Be("https://example.com/mcp");
        }

        [Fact]
        public void AddSseServer_Adds_Configuration()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act
            builder.AddSseServer("test", "https://example.com/sse").Build();
            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Mcp.Should().ContainKey("test");
            config.Configuration.Mcp["test"].Type.Should().Be(ServerTransportType.Sse);
        }
    }

    public class ChainedBuildTests
    {
        [Fact]
        public void Full_Builder_Chain_Produces_Valid_Config()
        {
            // Arrange & Act
            var builder = McpProxyBuilder.Create();
            builder
                .WithServerInfo("My Proxy", "1.0.0")
                .WithRouting(RoutingMode.PerServer, "/mcp")
                .WithToolCaching(true, 600)
                .AddHttpServer("calendar", "https://example.com/calendar-mcp")
                    .WithTitle("Calendar")
                    .WithRoute("/calendar")
                    .Build()
                .AddStdioServer("local", "npx", "-y", "@test/server")
                    .WithTitle("Local")
                    .Build();

            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Proxy.ServerInfo.Name.Should().Be("My Proxy");
            config.Configuration.Proxy.Routing.Mode.Should().Be(RoutingMode.PerServer);
            config.Configuration.Proxy.Routing.BasePath.Should().Be("/mcp");
            config.Configuration.Proxy.Caching.Tools.Enabled.Should().BeTrue();
            config.Configuration.Proxy.Caching.Tools.TtlSeconds.Should().Be(600);
            config.Configuration.Mcp.Should().HaveCount(2);
            config.Configuration.Mcp["calendar"].Title.Should().Be("Calendar");
            config.Configuration.Mcp["calendar"].Route.Should().Be("/calendar");
            config.Configuration.Mcp["local"].Title.Should().Be("Local");
        }
    }

    public class DuplicateServerNameTests
    {
        [Fact]
        public void AddStdioServer_Throws_On_Duplicate_Name()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
            builder.AddStdioServer("test", "echo").Build();

            // Act
            var act = () => builder.AddStdioServer("test", "echo2").Build();

            // Assert
            act.Should().Throw<ArgumentException>().WithMessage("*test*");
        }

        [Fact]
        public void AddHttpServer_Throws_On_Duplicate_Name()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
            builder.AddHttpServer("test", "http://a.com/mcp").Build();

            // Act
            var act = () => builder.AddHttpServer("test", "http://b.com/mcp").Build();

            // Assert
            act.Should().Throw<ArgumentException>().WithMessage("*test*");
        }

        [Fact]
        public void AddSseServer_Throws_On_Duplicate_Name()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
            builder.AddSseServer("test", "http://a.com/sse").Build();

            // Act
            var act = () => builder.AddSseServer("test", "http://b.com/sse").Build();

            // Assert
            act.Should().Throw<ArgumentException>().WithMessage("*test*");
        }

        [Fact]
        public void Different_Names_Do_Not_Throw()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act & Assert — should not throw
            builder.AddStdioServer("server-a", "echo").Build();
            builder.AddHttpServer("server-b", "http://b.com/mcp").Build();
            builder.AddSseServer("server-c", "http://c.com/sse").Build();

            var config = builder.BuildConfiguration();
            config.Configuration.Mcp.Should().HaveCount(3);
        }
    }

    public class WithBackendAuthTests
    {
        [Fact]
        public void Sets_Auth_Type_Via_Interface()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();

            // Act
            builder.AddHttpServer("test", "http://example.com/mcp")
                .WithBackendAuth(BackendAuthType.ForwardAuthorization)
                .Build();

            var config = builder.BuildConfiguration();

            // Assert
            config.Configuration.Mcp["test"].Auth.Should().NotBeNull();
            config.Configuration.Mcp["test"].Auth!.Type.Should().Be(BackendAuthType.ForwardAuthorization);
        }
    }

    public class VirtualToolTests
    {
        [Fact]
        public void AddVirtualTool_On_Server_Stores_In_ServerState()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
            var tool = new Tool
            {
                Name = "my_virtual_tool",
                Description = "A virtual tool",
                InputSchema = System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement
            };

            // Act
            builder.AddHttpServer("test", "http://example.com/mcp")
                .AddVirtualTool(tool, (_, _) => ValueTask.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "virtual result" }]
                }))
                .Build();

            var config = builder.BuildConfiguration();

            // Assert
            config.ServerStates["test"].VirtualTools.Should().HaveCount(1);
            config.ServerStates["test"].VirtualTools[0].Tool.Name.Should().Be("my_virtual_tool");
        }

        [Fact]
        public void Global_AddVirtualTool_Stores_In_VirtualTools()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
            var tool = new Tool
            {
                Name = "global_tool",
                Description = "A global virtual tool",
                InputSchema = System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement
            };

            // Act
            builder.AddVirtualTool(tool, (_, _) => ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "global result" }]
            }));

            var config = builder.BuildConfiguration();

            // Assert
            config.VirtualTools.Should().HaveCount(1);
            config.VirtualTools[0].Tool.Name.Should().Be("global_tool");
        }
    }

    public class GlobalVirtualToolsFlagTests
    {
        [Fact]
        public void Default_Is_False()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
            var config = builder.BuildConfiguration();

            // Assert
            config.ShowGlobalVirtualToolsOnPerServerRoutes.Should().BeFalse();
        }

        [Fact]
        public void Sets_To_True()
        {
            // Arrange
            var builder = McpProxyBuilder.Create();
            builder.WithGlobalVirtualToolsOnPerServerRoutes();
            var config = builder.BuildConfiguration();

            // Assert
            config.ShowGlobalVirtualToolsOnPerServerRoutes.Should().BeTrue();
        }
    }
}
