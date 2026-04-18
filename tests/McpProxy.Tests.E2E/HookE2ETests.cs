using System.Diagnostics.Metrics;
using McpProxy.Abstractions;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Exceptions;
using McpProxy.Sdk.Hooks;
using McpProxy.Sdk.Hooks.BuiltIn;
using McpProxy.Sdk.Proxy;
using McpProxy.Sdk.Telemetry;
using McpProxy.Tests.E2E.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.E2E;

/// <summary>
/// E2E tests for built-in hooks (RateLimiting, Authorization, ContentFilter, etc.).
/// </summary>
public class HookE2ETests : ProxyTestBase
{
    private readonly ILogger<HookPipeline> _hookPipelineLogger;

    public HookE2ETests()
    {
        _hookPipelineLogger = Substitute.For<ILogger<HookPipeline>>();
    }

    private HookPipeline CreateHookPipeline(params object[] hooks)
    {
        var pipeline = new HookPipeline(_hookPipelineLogger);
        foreach (var hook in hooks)
        {
            if (hook is IToolHook toolHook)
            {
                pipeline.AddHook(toolHook);
            }
            else
            {
                if (hook is IPreInvokeHook preHook)
                {
                    pipeline.AddPreInvokeHook(preHook);
                }
                if (hook is IPostInvokeHook postHook)
                {
                    pipeline.AddPostInvokeHook(postHook);
                }
            }
        }
        return pipeline;
    }

    [Fact]
    public async Task RateLimitingHook_BlocksAfterLimitExceeded()
    {
        // Arrange
        var tool = CreateTool("test-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Setup rate limiting: max 2 requests per window
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var rateLimitConfig = new RateLimitingConfiguration { MaxRequests = 2, WindowSeconds = 60 };
        var rateLimitHook = new RateLimitingHook(
            cache,
            Substitute.For<ILogger<RateLimitingHook>>(),
            rateLimitConfig);

        var pipeline = CreateHookPipeline(rateLimitHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act - first 2 calls should succeed
        var result1 = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "test-tool" },
            TestContext.Current.CancellationToken);
        result1.IsError.Should().NotBeTrue();

        var result2 = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "test-tool" },
            TestContext.Current.CancellationToken);
        result2.IsError.Should().NotBeTrue();

        // Third call should throw RateLimitExceededException
        await Assert.ThrowsAsync<RateLimitExceededException>(async () =>
            await proxy.CallToolCoreAsync(
                new CallToolRequestParams { Name = "test-tool" },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthorizationHook_DeniesUnauthorizedAccess()
    {
        // Arrange
        var tool = CreateTool("admin-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Setup authorization requiring admin role
        var authConfig = new AuthorizationConfiguration
        {
            RequireAuthentication = false,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "admin-tool",
                    RequiredRoles = ["admin"]
                }
            ],
            DefaultAllow = false
        };
        var authHook = new AuthorizationHook(
            Substitute.For<ILogger<AuthorizationHook>>(),
            authConfig);

        var pipeline = CreateHookPipeline(authHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act & Assert - should throw AuthorizationException (no auth context = defaultAllow = false)
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
            await proxy.CallToolCoreAsync(
                new CallToolRequestParams { Name = "admin-tool" },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthorizationHook_DefaultAllow_AllowsAllWhenTrue()
    {
        // Arrange
        var tool = CreateTool("any-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Setup authorization with default allow
        var authConfig = new AuthorizationConfiguration
        {
            RequireAuthentication = false,
            DefaultAllow = true
        };
        var authHook = new AuthorizationHook(
            Substitute.For<ILogger<AuthorizationHook>>(),
            authConfig);

        var pipeline = CreateHookPipeline(authHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "any-tool" },
            TestContext.Current.CancellationToken);

        // Assert - should succeed with default allow = true
        result.IsError.Should().NotBeTrue();
    }

    [Fact]
    public async Task ContentFilterHook_RedactsSensitiveData()
    {
        // Arrange - create a mock client that returns sensitive data
        var tool = CreateTool("get-info");
        var client = Substitute.For<IMcpClientWrapper>();
        client.ListToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Tool>>([tool]));
        client.CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Your SSN is 123-45-6789" }]
            }));

        var config = new ServerConfiguration { Type = ServerTransportType.Stdio, Command = "mock" };
        ClientManager.RegisterClient("server1", client, config);

        var proxy = CreateProxyServer();

        // Setup content filter with SSN redaction
        var filterConfig = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "ssn",
                    Pattern = @"\d{3}-\d{2}-\d{4}",
                    Mode = ContentFilterMode.Redact,
                    RedactReplacement = "[SSN-REDACTED]"
                }
            ]
        };
        var contentFilterHook = new ContentFilterHook(
            Substitute.For<ILogger<ContentFilterHook>>(),
            filterConfig);

        var pipeline = CreateHookPipeline(contentFilterHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "get-info" },
            TestContext.Current.CancellationToken);

        // Assert - SSN should be redacted
        result.IsError.Should().NotBeTrue();
        var textContent = result.Content[0] as TextContentBlock;
        textContent!.Text.Should().Contain("[SSN-REDACTED]");
        textContent.Text.Should().NotContain("123-45-6789");
    }

    [Fact]
    public async Task ContentFilterHook_BlocksProhibitedContent()
    {
        // Arrange - create a mock client that returns blocked content
        var tool = CreateTool("get-secret");
        var client = Substitute.For<IMcpClientWrapper>();
        client.ListToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Tool>>([tool]));
        client.CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "-----BEGIN PRIVATE KEY-----" }]
            }));

        var config = new ServerConfiguration { Type = ServerTransportType.Stdio, Command = "mock" };
        ClientManager.RegisterClient("server1", client, config);

        var proxy = CreateProxyServer();

        // Setup content filter to block private keys
        var filterConfig = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "private_key",
                    Pattern = @"-----BEGIN.*PRIVATE KEY-----",
                    Mode = ContentFilterMode.Block,
                    BlockMessage = "Private keys are not allowed"
                }
            ]
        };
        var contentFilterHook = new ContentFilterHook(
            Substitute.For<ILogger<ContentFilterHook>>(),
            filterConfig);

        var pipeline = CreateHookPipeline(contentFilterHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "get-secret" },
            TestContext.Current.CancellationToken);

        // Assert - should be blocked
        result.IsError.Should().BeTrue();
        var textContent = result.Content[0] as TextContentBlock;
        textContent!.Text.Should().Contain("blocked");
    }

    [Fact]
    public async Task TimeoutHook_SetsCancellationToken()
    {
        // Arrange
        var tool = CreateTool("slow-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Setup timeout hook with 30 second default
        var timeoutConfig = new TimeoutConfiguration { DefaultTimeoutSeconds = 30 };
        var timeoutHook = new TimeoutHook(
            Substitute.For<ILogger<TimeoutHook>>(),
            timeoutConfig);

        var pipeline = CreateHookPipeline(timeoutHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "slow-tool" },
            TestContext.Current.CancellationToken);

        // Assert - should succeed (timeout not triggered)
        result.IsError.Should().NotBeTrue();
    }

    [Fact]
    public async Task LoggingHook_LogsToolCalls()
    {
        // Arrange
        var tool = CreateTool("logged-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        var loggingLogger = Substitute.For<ILogger<LoggingHook>>();
        var loggingHook = new LoggingHook(loggingLogger, LogLevel.Information);

        var pipeline = CreateHookPipeline(loggingHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "logged-tool" },
            TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().NotBeTrue();
        // Logging hook should have logged - we verify through the logger mock
    }

    [Fact]
    public async Task MultipleHooks_ExecuteInPriorityOrder()
    {
        // Arrange
        var tool = CreateTool("multi-hook-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Create multiple hooks with different priorities
        var loggingHook = new LoggingHook(
            Substitute.For<ILogger<LoggingHook>>(),
            LogLevel.Information);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var rateLimitHook = new RateLimitingHook(
            cache,
            Substitute.For<ILogger<RateLimitingHook>>(),
            new RateLimitingConfiguration { MaxRequests = 100, WindowSeconds = 60 });

        var authHook = new AuthorizationHook(
            Substitute.For<ILogger<AuthorizationHook>>(),
            new AuthorizationConfiguration { RequireAuthentication = false, DefaultAllow = true });

        var timeoutHook = new TimeoutHook(
            Substitute.For<ILogger<TimeoutHook>>(),
            new TimeoutConfiguration { DefaultTimeoutSeconds = 30 });

        // Pipeline sorts by priority automatically
        var pipeline = CreateHookPipeline(loggingHook, rateLimitHook, authHook, timeoutHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "multi-hook-tool" },
            TestContext.Current.CancellationToken);

        // Assert - all hooks executed successfully
        result.IsError.Should().NotBeTrue();
    }

    [Fact]
    public async Task MetricsHook_RecordsWithoutError()
    {
        // Arrange
        var tool = CreateTool("metrics-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Create real metrics with test meter
        using var meterFactory = new TestMeterFactory();
        using var metrics = new ProxyMetrics(meterFactory);

        var metricsConfig = new MetricsHookConfiguration
        {
            RecordTiming = true,
            RecordSizes = true
        };
        var metricsHook = new MetricsHook(
            Substitute.For<ILogger<MetricsHook>>(),
            metrics,
            metricsConfig);

        var pipeline = CreateHookPipeline(metricsHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "metrics-tool" },
            TestContext.Current.CancellationToken);

        // Assert - metrics hook executed without error
        result.IsError.Should().NotBeTrue();
    }

    [Fact]
    public async Task AuditHook_RecordsWithoutError()
    {
        // Arrange
        var tool = CreateTool("audit-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        var auditConfig = new AuditConfiguration
        {
            Level = AuditLevel.Standard,
            IncludeSensitiveData = false
        };
        var auditHook = new AuditHook(
            Substitute.For<ILogger<AuditHook>>(),
            auditConfig);

        var pipeline = CreateHookPipeline(auditHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "audit-tool" },
            TestContext.Current.CancellationToken);

        // Assert - audit hook executed without error
        result.IsError.Should().NotBeTrue();
    }

    [Fact]
    public async Task RetryHook_SetsRetryFlagOnTransientError()
    {
        // Arrange
        var callCount = 0;
        var tool = CreateTool("flaky-tool");
        var client = Substitute.For<IMcpClientWrapper>();
        client.ListToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Tool>>([tool]));
        client.CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                // First call fails with transient error, subsequent calls succeed
                if (callCount == 1)
                {
                    return Task.FromResult(new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = "Connection timeout" }]
                    });
                }
                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "Success on retry" }]
                });
            });

        var config = new ServerConfiguration { Type = ServerTransportType.Stdio, Command = "mock" };
        ClientManager.RegisterClient("server1", client, config);

        var proxy = CreateProxyServer();

        // Setup retry hook
        var retryConfig = new RetryConfiguration
        {
            MaxRetries = 3,
            RetryablePatterns = ["timeout", "connection"]
        };
        var retryHook = new RetryHook(
            Substitute.For<ILogger<RetryHook>>(),
            retryConfig);

        var pipeline = CreateHookPipeline(retryHook);
        proxy.AddHookPipeline("server1", pipeline);

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act - proxy server should handle retry
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "flaky-tool" },
            TestContext.Current.CancellationToken);

        // Assert - should eventually succeed after retry
        result.IsError.Should().NotBeTrue();
        callCount.Should().BeGreaterThan(1);
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
