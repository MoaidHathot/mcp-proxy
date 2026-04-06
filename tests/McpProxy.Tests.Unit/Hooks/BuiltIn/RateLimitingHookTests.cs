using McpProxy.Abstractions;
using McpProxy.SDK.Exceptions;
using McpProxy.SDK.Hooks.BuiltIn;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class RateLimitingHookTests : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly ILogger<RateLimitingHook> _logger;

    public RateLimitingHookTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = Substitute.For<ILogger<RateLimitingHook>>();
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    private static HookContext<CallToolRequestParams> CreateContext(
        string toolName = "test_tool",
        string serverName = "test-server",
        string? principalId = null)
    {
        var authResult = principalId is not null
            ? AuthenticationResult.Success(principalId)
            : null;

        return new HookContext<CallToolRequestParams>
        {
            ServerName = serverName,
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName },
            CancellationToken = TestContext.Current.CancellationToken,
            AuthenticationResult = authResult
        };
    }

    [Fact]
    public void Priority_ReturnsNegativeValue()
    {
        // Arrange
        var config = new RateLimitingConfiguration();
        var hook = new RateLimitingHook(_cache, _logger, config);

        // Assert - rate limiting should execute early
        hook.Priority.Should().BeLessThan(0);
    }

    [Fact]
    public async Task OnPreInvokeAsync_BelowLimit_Succeeds()
    {
        // Arrange
        var config = new RateLimitingConfiguration { MaxRequests = 10, WindowSeconds = 60 };
        var hook = new RateLimitingHook(_cache, _logger, config);
        var context = CreateContext(principalId: "user1");

        // Act & Assert - should not throw
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_AtLimit_Succeeds()
    {
        // Arrange
        var config = new RateLimitingConfiguration { MaxRequests = 3, WindowSeconds = 60 };
        var hook = new RateLimitingHook(_cache, _logger, config);
        var context = CreateContext(principalId: "user1");

        // Act - make exactly MaxRequests calls
        for (int i = 0; i < config.MaxRequests; i++)
        {
            await hook.OnPreInvokeAsync(context);
        }

        // Assert - should have completed without throwing
    }

    [Fact]
    public async Task OnPreInvokeAsync_ExceedsLimit_ThrowsRateLimitExceededException()
    {
        // Arrange
        var config = new RateLimitingConfiguration { MaxRequests = 3, WindowSeconds = 60 };
        var hook = new RateLimitingHook(_cache, _logger, config);
        var context = CreateContext(principalId: "user1");

        // Make MaxRequests calls first
        for (int i = 0; i < config.MaxRequests; i++)
        {
            await hook.OnPreInvokeAsync(context);
        }

        // Act - one more should exceed the limit
        var act = async () => await hook.OnPreInvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<RateLimitExceededException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_DifferentClients_TrackedSeparately()
    {
        // Arrange
        var config = new RateLimitingConfiguration
        {
            MaxRequests = 2,
            WindowSeconds = 60,
            KeyType = RateLimitKeyType.Client
        };
        var hook = new RateLimitingHook(_cache, _logger, config);
        var context1 = CreateContext(principalId: "user1");
        var context2 = CreateContext(principalId: "user2");

        // Act - exhaust limit for user1
        await hook.OnPreInvokeAsync(context1);
        await hook.OnPreInvokeAsync(context1);

        // Assert - user2 should still have quota
        await hook.OnPreInvokeAsync(context2);
        await hook.OnPreInvokeAsync(context2);

        // user1 should now be limited
        var act = async () => await hook.OnPreInvokeAsync(context1);
        await act.Should().ThrowAsync<RateLimitExceededException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_KeyTypeTool_LimitsByToolName()
    {
        // Arrange
        var config = new RateLimitingConfiguration
        {
            MaxRequests = 2,
            WindowSeconds = 60,
            KeyType = RateLimitKeyType.Tool
        };
        var hook = new RateLimitingHook(_cache, _logger, config);
        var context1 = CreateContext(toolName: "tool_a", principalId: "user1");
        var context2 = CreateContext(toolName: "tool_b", principalId: "user1");

        // Act - exhaust limit for tool_a
        await hook.OnPreInvokeAsync(context1);
        await hook.OnPreInvokeAsync(context1);

        // Assert - tool_b should still have quota
        await hook.OnPreInvokeAsync(context2);

        // tool_a should now be limited
        var act = async () => await hook.OnPreInvokeAsync(context1);
        await act.Should().ThrowAsync<RateLimitExceededException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_KeyTypeServer_LimitsByServerName()
    {
        // Arrange
        var config = new RateLimitingConfiguration
        {
            MaxRequests = 2,
            WindowSeconds = 60,
            KeyType = RateLimitKeyType.Server
        };
        var hook = new RateLimitingHook(_cache, _logger, config);
        var context1 = CreateContext(serverName: "server_a", principalId: "user1");
        var context2 = CreateContext(serverName: "server_b", principalId: "user1");

        // Act - exhaust limit for server_a
        await hook.OnPreInvokeAsync(context1);
        await hook.OnPreInvokeAsync(context1);

        // Assert - server_b should still have quota
        await hook.OnPreInvokeAsync(context2);

        // server_a should now be limited
        var act = async () => await hook.OnPreInvokeAsync(context1);
        await act.Should().ThrowAsync<RateLimitExceededException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_KeyTypeCombined_LimitsByAllFactors()
    {
        // Arrange
        var config = new RateLimitingConfiguration
        {
            MaxRequests = 2,
            WindowSeconds = 60,
            KeyType = RateLimitKeyType.Combined
        };
        var hook = new RateLimitingHook(_cache, _logger, config);
        var context1 = CreateContext(serverName: "server_a", toolName: "tool_a", principalId: "user1");
        var context2 = CreateContext(serverName: "server_a", toolName: "tool_b", principalId: "user1");

        // Act - exhaust limit for user1:server_a:tool_a
        await hook.OnPreInvokeAsync(context1);
        await hook.OnPreInvokeAsync(context1);

        // Assert - different tool should still have quota
        await hook.OnPreInvokeAsync(context2);

        // Original combination should be limited
        var act = async () => await hook.OnPreInvokeAsync(context1);
        await act.Should().ThrowAsync<RateLimitExceededException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_AnonymousUser_UsesAnonymousKey()
    {
        // Arrange
        var config = new RateLimitingConfiguration { MaxRequests = 2, WindowSeconds = 60 };
        var hook = new RateLimitingHook(_cache, _logger, config);
        var context = CreateContext(principalId: null); // No auth

        // Act - should work for anonymous users
        await hook.OnPreInvokeAsync(context);
        await hook.OnPreInvokeAsync(context);

        // Assert - should be limited after max
        var act = async () => await hook.OnPreInvokeAsync(context);
        await act.Should().ThrowAsync<RateLimitExceededException>();
    }
}
