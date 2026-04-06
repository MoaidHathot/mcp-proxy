using McpProxy.SDK.Caching;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Caching;

public class ToolCacheTests
{
    private static Tool CreateTool(string name) => new()
    {
        Name = name,
        Description = $"Test tool: {name}"
    };

    private static List<Tool> CreateToolList(params string[] names) =>
        names.Select(CreateTool).ToList();

    public class GetToolTests
    {
        [Fact]
        public void GetTool_WhenNotCached_ReturnsNull()
        {
            // Arrange
            var cache = new ToolCache(60);

            // Act
            var result = cache.GetTool("nonexistent");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetTool_WhenCached_ReturnsCachedInfo()
        {
            // Arrange
            var cache = new ToolCache(60);
            var tools = CreateToolList("tool1", "tool2");
            cache.SetToolsForServer("server1", tools);

            // Act
            var result = cache.GetTool("tool1");

            // Assert
            result.Should().NotBeNull();
            result!.Tool.Name.Should().Be("tool1");
            result.OriginalName.Should().Be("tool1");
            result.ServerName.Should().Be("server1");
        }

        [Fact]
        public void GetTool_CaseInsensitive_ReturnsCachedInfo()
        {
            // Arrange
            var cache = new ToolCache(60);
            var tools = CreateToolList("MyTool");
            cache.SetToolsForServer("server1", tools);

            // Act
            var result = cache.GetTool("mytool");

            // Assert
            result.Should().NotBeNull();
            result!.Tool.Name.Should().Be("MyTool");
        }

        [Fact]
        public void GetTool_WithPrefixedName_ReturnsCachedInfo()
        {
            // Arrange
            var cache = new ToolCache(60);
            var tools = CreateToolList("read", "write");
            var prefixedNames = new Dictionary<string, string>
            {
                ["server1_read"] = "read",
                ["server1_write"] = "write"
            };
            cache.SetToolsForServer("server1", tools, prefixedNames);

            // Act
            var result = cache.GetTool("server1_read");

            // Assert
            result.Should().NotBeNull();
            result!.OriginalName.Should().Be("read");
            result.ServerName.Should().Be("server1");
        }
    }

    public class GetToolsForServerTests
    {
        [Fact]
        public void GetToolsForServer_WhenNotCached_ReturnsNull()
        {
            // Arrange
            var cache = new ToolCache(60);

            // Act
            var result = cache.GetToolsForServer("nonexistent");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetToolsForServer_WhenCached_ReturnsTools()
        {
            // Arrange
            var cache = new ToolCache(60);
            var tools = CreateToolList("tool1", "tool2", "tool3");
            cache.SetToolsForServer("server1", tools);

            // Act
            var result = cache.GetToolsForServer("server1");

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(3);
        }

        [Fact]
        public void GetToolsForServer_CaseInsensitive_ReturnsTools()
        {
            // Arrange
            var cache = new ToolCache(60);
            var tools = CreateToolList("tool1");
            cache.SetToolsForServer("Server1", tools);

            // Act
            var result = cache.GetToolsForServer("server1");

            // Assert
            result.Should().NotBeNull();
        }
    }

    public class CacheExpirationTests
    {
        [Fact]
        public void GetTool_WhenExpired_ReturnsNull()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var cache = new ToolCache(60, timeProvider);
            var tools = CreateToolList("tool1");
            cache.SetToolsForServer("server1", tools);

            // Advance time past TTL
            timeProvider.Advance(TimeSpan.FromSeconds(61));

            // Act
            var result = cache.GetTool("tool1");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetToolsForServer_WhenExpired_ReturnsNull()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var cache = new ToolCache(60, timeProvider);
            var tools = CreateToolList("tool1");
            cache.SetToolsForServer("server1", tools);

            // Advance time past TTL
            timeProvider.Advance(TimeSpan.FromSeconds(61));

            // Act
            var result = cache.GetToolsForServer("server1");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetTool_WithinTtl_ReturnsCachedInfo()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var cache = new ToolCache(60, timeProvider);
            var tools = CreateToolList("tool1");
            cache.SetToolsForServer("server1", tools);

            // Advance time but stay within TTL
            timeProvider.Advance(TimeSpan.FromSeconds(30));

            // Act
            var result = cache.GetTool("tool1");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public void HasValidCache_WhenExpired_ReturnsFalse()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var cache = new ToolCache(60, timeProvider);
            var tools = CreateToolList("tool1");
            cache.SetToolsForServer("server1", tools);

            // Advance time past TTL
            timeProvider.Advance(TimeSpan.FromSeconds(61));

            // Act
            var result = cache.HasValidCache("server1");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HasValidCache_WithinTtl_ReturnsTrue()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var cache = new ToolCache(30, timeProvider);
            var tools = CreateToolList("tool1");
            cache.SetToolsForServer("server1", tools);

            // Advance time but stay within TTL
            timeProvider.Advance(TimeSpan.FromSeconds(15));

            // Act
            var result = cache.HasValidCache("server1");

            // Assert
            result.Should().BeTrue();
        }
    }

    public class InvalidationTests
    {
        [Fact]
        public void InvalidateServer_RemovesCachedTools()
        {
            // Arrange
            var cache = new ToolCache(60);
            var tools = CreateToolList("tool1", "tool2");
            cache.SetToolsForServer("server1", tools);

            // Act
            cache.InvalidateServer("server1");

            // Assert
            cache.GetTool("tool1").Should().BeNull();
            cache.GetToolsForServer("server1").Should().BeNull();
            cache.HasValidCache("server1").Should().BeFalse();
        }

        [Fact]
        public void InvalidateServer_DoesNotAffectOtherServers()
        {
            // Arrange
            var cache = new ToolCache(60);
            cache.SetToolsForServer("server1", CreateToolList("tool1"));
            cache.SetToolsForServer("server2", CreateToolList("tool2"));

            // Act
            cache.InvalidateServer("server1");

            // Assert
            cache.GetTool("tool1").Should().BeNull();
            cache.GetTool("tool2").Should().NotBeNull();
        }

        [Fact]
        public void InvalidateAll_RemovesAllCachedTools()
        {
            // Arrange
            var cache = new ToolCache(60);
            cache.SetToolsForServer("server1", CreateToolList("tool1"));
            cache.SetToolsForServer("server2", CreateToolList("tool2"));

            // Act
            cache.InvalidateAll();

            // Assert
            cache.GetTool("tool1").Should().BeNull();
            cache.GetTool("tool2").Should().BeNull();
            cache.GetToolsForServer("server1").Should().BeNull();
            cache.GetToolsForServer("server2").Should().BeNull();
        }

        [Fact]
        public void InvalidateServer_RemovesPrefixedNames()
        {
            // Arrange
            var cache = new ToolCache(60);
            var tools = CreateToolList("read");
            var prefixedNames = new Dictionary<string, string> { ["server1_read"] = "read" };
            cache.SetToolsForServer("server1", tools, prefixedNames);

            // Act
            cache.InvalidateServer("server1");

            // Assert
            cache.GetTool("read").Should().BeNull();
            cache.GetTool("server1_read").Should().BeNull();
        }
    }

    public class SetToolsForServerTests
    {
        [Fact]
        public void SetToolsForServer_UpdatesExistingCache()
        {
            // Arrange
            var cache = new ToolCache(60);
            cache.SetToolsForServer("server1", CreateToolList("old_tool"));

            // Act
            cache.SetToolsForServer("server1", CreateToolList("new_tool"));

            // Assert
            cache.GetTool("old_tool").Should().BeNull();
            cache.GetTool("new_tool").Should().NotBeNull();
        }

        [Fact]
        public void SetToolsForServer_RemovesOldPrefixedNames()
        {
            // Arrange
            var cache = new ToolCache(60);
            var oldPrefixedNames = new Dictionary<string, string> { ["server1_old"] = "old_tool" };
            cache.SetToolsForServer("server1", CreateToolList("old_tool"), oldPrefixedNames);

            // Act
            var newPrefixedNames = new Dictionary<string, string> { ["server1_new"] = "new_tool" };
            cache.SetToolsForServer("server1", CreateToolList("new_tool"), newPrefixedNames);

            // Assert
            cache.GetTool("server1_old").Should().BeNull();
            cache.GetTool("server1_new").Should().NotBeNull();
        }

        [Fact]
        public void SetToolsForServer_RefreshesTtl()
        {
            // Arrange
            var timeProvider = new FakeTimeProvider();
            var cache = new ToolCache(60, timeProvider);
            cache.SetToolsForServer("server1", CreateToolList("tool1"));

            // Advance time to near expiration
            timeProvider.Advance(TimeSpan.FromSeconds(55));

            // Re-set the tools (refresh cache)
            cache.SetToolsForServer("server1", CreateToolList("tool1"));

            // Advance time past original expiration but within new TTL
            timeProvider.Advance(TimeSpan.FromSeconds(30));

            // Act
            var result = cache.GetTool("tool1");

            // Assert
            result.Should().NotBeNull();
        }
    }

    public class NullToolCacheTests
    {
        [Fact]
        public void GetTool_Always_ReturnsNull()
        {
            // Arrange
            var cache = NullToolCache.Instance;

            // Act
            var result = cache.GetTool("any_tool");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetToolsForServer_Always_ReturnsNull()
        {
            // Arrange
            var cache = NullToolCache.Instance;

            // Act
            var result = cache.GetToolsForServer("any_server");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void HasValidCache_Always_ReturnsFalse()
        {
            // Arrange
            var cache = NullToolCache.Instance;

            // Act
            var result = cache.HasValidCache("any_server");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void SetToolsForServer_DoesNotThrow()
        {
            // Arrange
            var cache = NullToolCache.Instance;
            var tools = CreateToolList("tool1");

            // Act & Assert
            var action = () => cache.SetToolsForServer("server1", tools);
            action.Should().NotThrow();
        }

        [Fact]
        public void InvalidateServer_DoesNotThrow()
        {
            // Arrange
            var cache = NullToolCache.Instance;

            // Act & Assert
            var action = () => cache.InvalidateServer("server1");
            action.Should().NotThrow();
        }

        [Fact]
        public void InvalidateAll_DoesNotThrow()
        {
            // Arrange
            var cache = NullToolCache.Instance;

            // Act & Assert
            var action = () => cache.InvalidateAll();
            action.Should().NotThrow();
        }
    }
}
