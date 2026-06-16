using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration.Cache;
using McpProxy.Samples.TeamsIntegration.Cache.Models;
using McpProxy.Samples.TeamsIntegration.Interceptors;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.E2E;

public class TeamsCacheInterceptorTests
{
    private readonly TeamsCacheInterceptor _interceptor;

    public TeamsCacheInterceptorTests()
    {
        var cache = new TeamsCacheService(
            Path.Combine(Path.GetTempPath(), $"teams-cache-test-{Guid.NewGuid():N}.json"),
            TimeSpan.FromHours(4));

        var alice = new CachedPerson { DisplayName = "Alice", Upn = "alice@contoso.com", UserId = "u-alice" };
        var bob = new CachedPerson { DisplayName = "Bob", Upn = "bob@contoso.com", UserId = "u-bob" };

        var chats = new[]
        {
            new CachedChat { Id = "19:alicebob@thread.v2", ChatType = "Group", Topic = "Project Standup", Members = [alice, bob] },
            new CachedChat { Id = "19:bobonly@unq.gbl.spaces", ChatType = "OneOnOne", Topic = "", Members = [bob] },
        };
        cache.CacheChats(chats);
        cache.MarkRefreshed(CacheScope.Chats);

        _interceptor = new TeamsCacheInterceptor(Substitute.For<ILogger<TeamsCacheInterceptor>>(), cache);
    }

    private static ToolCallContext Ctx(string tool, Dictionary<string, JsonElement>? args = null) => new()
    {
        ToolName = tool,
        OriginalToolName = tool,
        ServerName = "teams",
        Request = new CallToolRequestParams { Name = tool, Arguments = args }
    };

    private static JsonElement CachedValue(CallToolResult? result)
    {
        result.Should().NotBeNull();
        var text = ((TextContentBlock)result!.Content![0]).Text;
        return JsonDocument.Parse(text).RootElement.GetProperty("value");
    }

    [Fact]
    public async Task UnfilteredListChats_ReturnsAllCached()
    {
        var result = await _interceptor.InterceptAsync(Ctx("ListChats"), TestContext.Current.CancellationToken);

        CachedValue(result).GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task EmptyMemberUpns_IsTreatedAsUnfiltered()
    {
        var args = new Dictionary<string, JsonElement> { ["memberUpns"] = JsonSerializer.SerializeToElement(Array.Empty<string>()) };

        var result = await _interceptor.InterceptAsync(Ctx("ListChats", args), TestContext.Current.CancellationToken);

        CachedValue(result).GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task MemberUpnsFilter_ReturnsOnlyMatchingChats()
    {
        var members = new[] { "alice@contoso.com" };
        var args = new Dictionary<string, JsonElement> { ["memberUpns"] = JsonSerializer.SerializeToElement(members) };

        var result = await _interceptor.InterceptAsync(Ctx("ListChats", args), TestContext.Current.CancellationToken);

        // Only the group chat contains Alice.
        CachedValue(result).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task TopicFilter_MatchesSubstringCaseInsensitive()
    {
        var args = new Dictionary<string, JsonElement> { ["topic"] = JsonSerializer.SerializeToElement("standup") };

        var result = await _interceptor.InterceptAsync(Ctx("ListChats", args), TestContext.Current.CancellationToken);

        CachedValue(result).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task FilteredButNoCacheMatch_FallsThroughToBackend()
    {
        var members = new[] { "nobody@contoso.com" };
        var args = new Dictionary<string, JsonElement> { ["memberUpns"] = JsonSerializer.SerializeToElement(members) };

        var result = await _interceptor.InterceptAsync(Ctx("ListChats", args), TestContext.Current.CancellationToken);

        // No cache short-circuit — the call must reach the backend rather than assert "no chats".
        result.Should().BeNull();
    }

    [Fact]
    public async Task ForceRefresh_BypassesCache_AndStripsArgument()
    {
        var args = new Dictionary<string, JsonElement> { ["forceRefresh"] = JsonSerializer.SerializeToElement(true) };
        var context = Ctx("ListChats", args);

        var result = await _interceptor.InterceptAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        context.Request.Arguments.Should().NotContainKey("forceRefresh");
    }

    [Fact]
    public async Task CachedChats_IncludeMembers()
    {
        var members = new[] { "alice@contoso.com" };
        var args = new Dictionary<string, JsonElement> { ["memberUpns"] = JsonSerializer.SerializeToElement(members) };

        var result = await _interceptor.InterceptAsync(Ctx("ListChats", args), TestContext.Current.CancellationToken);

        CachedValue(result)[0].GetProperty("members").GetArrayLength().Should().BeGreaterThan(0);
    }
}
