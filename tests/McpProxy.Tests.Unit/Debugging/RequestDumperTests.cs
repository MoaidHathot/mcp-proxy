using McpProxy.SDK.Configuration;
using McpProxy.SDK.Debugging;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Debugging;

public class RequestDumperTests
{
    private readonly ILogger<RequestDumper> _logger;
    private readonly string _tempDirectory;

    public RequestDumperTests()
    {
        _logger = Substitute.For<ILogger<RequestDumper>>();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mcp-proxy-test-" + Guid.NewGuid().ToString("N"));
    }

    private DumpConfiguration CreateConfig(
        bool enabled = true,
        string? outputDirectory = null,
        string[]? serverFilter = null,
        string[]? toolFilter = null,
        bool prettyPrint = true,
        int maxPayloadSizeKb = 1024)
    {
        return new DumpConfiguration
        {
            Enabled = enabled,
            OutputDirectory = outputDirectory,
            ServerFilter = serverFilter,
            ToolFilter = toolFilter,
            PrettyPrint = prettyPrint,
            MaxPayloadSizeKb = maxPayloadSizeKb
        };
    }

    public class DumpRequestAsyncTests : RequestDumperTests, IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task DumpRequestAsync_WhenDisabled_DoesNothing()
        {
            // Arrange
            var config = CreateConfig(enabled: false, outputDirectory: _tempDirectory);
            var dumper = new RequestDumper(_logger, config);
            var request = new { Name = "test_tool", Arguments = new { foo = "bar" } };

            // Act
            await dumper.DumpRequestAsync("server", "tool", request, TestContext.Current.CancellationToken);

            // Assert
            Directory.Exists(_tempDirectory).Should().BeFalse();
        }

        [Fact]
        public async Task DumpRequestAsync_WithOutputDirectory_CreatesFile()
        {
            // Arrange
            var config = CreateConfig(enabled: true, outputDirectory: _tempDirectory);
            var dumper = new RequestDumper(_logger, config);
            var request = new { Name = "test_tool", Arguments = new { foo = "bar" } };

            // Act
            await dumper.DumpRequestAsync("test-server", "test_tool", request, TestContext.Current.CancellationToken);

            // Assert
            Directory.Exists(_tempDirectory).Should().BeTrue();
            var files = Directory.GetFiles(_tempDirectory, "*.json");
            files.Should().HaveCount(1);
            var filename = Path.GetFileName(files[0]);
            filename.Should().Contain("test-server");
            filename.Should().Contain("test_tool");
            filename.Should().Contain("request");
        }

        [Fact]
        public async Task DumpRequestAsync_WithServerFilter_OnlyDumpsMatchingServer()
        {
            // Arrange
            var config = CreateConfig(
                enabled: true,
                outputDirectory: _tempDirectory,
                serverFilter: ["allowed-server"]);
            var dumper = new RequestDumper(_logger, config);
            var request = new { Name = "test_tool" };

            // Act
            await dumper.DumpRequestAsync("other-server", "test_tool", request, TestContext.Current.CancellationToken);

            // Assert
            if (Directory.Exists(_tempDirectory))
            {
                var files = Directory.GetFiles(_tempDirectory, "*.json");
                files.Should().BeEmpty();
            }
        }

        [Fact]
        public async Task DumpRequestAsync_WithToolFilter_OnlyDumpsMatchingTool()
        {
            // Arrange
            var config = CreateConfig(
                enabled: true,
                outputDirectory: _tempDirectory,
                toolFilter: ["allowed_tool"]);
            var dumper = new RequestDumper(_logger, config);
            var request = new { Name = "other_tool" };

            // Act
            await dumper.DumpRequestAsync("server", "other_tool", request, TestContext.Current.CancellationToken);

            // Assert
            if (Directory.Exists(_tempDirectory))
            {
                var files = Directory.GetFiles(_tempDirectory, "*.json");
                files.Should().BeEmpty();
            }
        }

        [Fact]
        public async Task DumpRequestAsync_ServerFilterMatch_DumpsRequest()
        {
            // Arrange
            var config = CreateConfig(
                enabled: true,
                outputDirectory: _tempDirectory,
                serverFilter: ["allowed-server"]);
            var dumper = new RequestDumper(_logger, config);
            var request = new { Name = "test_tool" };

            // Act
            await dumper.DumpRequestAsync("allowed-server", "test_tool", request, TestContext.Current.CancellationToken);

            // Assert
            Directory.Exists(_tempDirectory).Should().BeTrue();
            var files = Directory.GetFiles(_tempDirectory, "*.json");
            files.Should().HaveCount(1);
        }

        [Fact]
        public async Task DumpRequestAsync_WithPrettyPrint_FormatsJson()
        {
            // Arrange
            var config = CreateConfig(enabled: true, outputDirectory: _tempDirectory, prettyPrint: true);
            var dumper = new RequestDumper(_logger, config);
            var request = new { Name = "test_tool", Value = 123 };

            // Act
            await dumper.DumpRequestAsync("server", "tool", request, TestContext.Current.CancellationToken);

            // Assert
            var files = Directory.GetFiles(_tempDirectory, "*.json");
            var content = await File.ReadAllTextAsync(files[0], TestContext.Current.CancellationToken);
            content.Should().Contain("\n"); // Pretty printed has newlines
        }

        [Fact]
        public async Task DumpRequestAsync_WithMaxSize_TruncatesLargePayload()
        {
            // Arrange
            var config = CreateConfig(
                enabled: true,
                outputDirectory: _tempDirectory,
                maxPayloadSizeKb: 1); // 1 KB limit
            var dumper = new RequestDumper(_logger, config);
            var largeData = new string('x', 2048); // > 1 KB
            var request = new { Data = largeData };

            // Act
            await dumper.DumpRequestAsync("server", "tool", request, TestContext.Current.CancellationToken);

            // Assert
            var files = Directory.GetFiles(_tempDirectory, "*.json");
            var content = await File.ReadAllTextAsync(files[0], TestContext.Current.CancellationToken);
            content.Should().Contain("[TRUNCATED]");
            content.Length.Should().BeLessThan(2048);
        }
    }

    public class DumpResponseAsyncTests : RequestDumperTests, IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task DumpResponseAsync_WithOutputDirectory_CreatesFile()
        {
            // Arrange
            var config = CreateConfig(enabled: true, outputDirectory: _tempDirectory);
            var dumper = new RequestDumper(_logger, config);
            var response = new { Result = "success", Data = new { items = new[] { 1, 2, 3 } } };

            // Act
            await dumper.DumpResponseAsync("test-server", "test_tool", response, TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

            // Assert
            Directory.Exists(_tempDirectory).Should().BeTrue();
            var files = Directory.GetFiles(_tempDirectory, "*.json");
            files.Should().HaveCount(1);
            var filename = Path.GetFileName(files[0]);
            filename.Should().Contain("response");
        }

        [Fact]
        public async Task DumpResponseAsync_WhenDisabled_DoesNothing()
        {
            // Arrange
            var config = CreateConfig(enabled: false, outputDirectory: _tempDirectory);
            var dumper = new RequestDumper(_logger, config);
            var response = new { Result = "success" };

            // Act
            await dumper.DumpResponseAsync("server", "tool", response, TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);

            // Assert
            Directory.Exists(_tempDirectory).Should().BeFalse();
        }
    }
}

public class NullRequestDumperTests
{
    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Act
        var instance1 = NullRequestDumper.Instance;
        var instance2 = NullRequestDumper.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public async Task DumpRequestAsync_DoesNotThrow()
    {
        // Arrange
        var dumper = NullRequestDumper.Instance;

        // Act
        var act = async () => await dumper.DumpRequestAsync("server", "tool", new { }, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DumpResponseAsync_DoesNotThrow()
    {
        // Arrange
        var dumper = NullRequestDumper.Instance;

        // Act
        var act = async () => await dumper.DumpResponseAsync("server", "tool", new { }, TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
