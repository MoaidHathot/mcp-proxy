using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration.Cache;
using McpProxy.Samples.TeamsIntegration.Hooks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.E2E;

public class TeamsCachePopulateHookTests : IDisposable
{
    private readonly TeamsCacheService _cache;
    private readonly TeamsCachePopulateHook _hook;

    public TeamsCachePopulateHookTests()
    {
        _cache = new TeamsCacheService(
            Path.Combine(Path.GetTempPath(), $"teams-populate-test-{Guid.NewGuid():N}.json"),
            TimeSpan.FromHours(4));
        _hook = new TeamsCachePopulateHook(Substitute.For<ILogger<TeamsCachePopulateHook>>(), _cache, autoSave: false);
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    private static HookContext<CallToolRequestParams> Ctx(string tool) => new()
    {
        ServerName = "teams",
        ToolName = tool,
        Request = new CallToolRequestParams { Name = tool },
        CancellationToken = TestContext.Current.CancellationToken
    };

    private static CallToolResult TextResult(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }]
    };

    [Fact]
    public async Task PopulatesChatsWithMembers_FromValueEnvelope()
    {
        const string json = """
        {"value":[
          {"id":"19:abc@thread.v2","chatType":"Group","topic":"Standup",
           "members":[{"id":"u1","displayName":"Alice","userPrincipalName":"alice@contoso.com"}]}
        ]}
        """;

        await _hook.OnPostInvokeAsync(Ctx("ListChats"), TextResult(json));

        var list = _cache.GetAllChats();
        list.Should().NotBeNull();
        list!.Should().HaveCount(1);
        list![0].Id.Should().Be("19:abc@thread.v2");
        list![0].Members.Should().ContainSingle(m => m.Upn == "alice@contoso.com");
    }

    [Fact]
    public async Task PopulatesChats_FromRawArray()
    {
        const string json = """[{"id":"19:raw@thread.v2","chatType":"OneOnOne","topic":""}]""";

        await _hook.OnPostInvokeAsync(Ctx("teams_ListChats"), TextResult(json));

        var list = _cache.GetAllChats();
        list.Should().NotBeNull();
        list!.Should().ContainSingle(c => c.Id == "19:raw@thread.v2");
    }

    [Fact]
    public async Task NonJsonResponse_DoesNotThrow_AndCachesNothing()
    {
        var act = async () => await _hook.OnPostInvokeAsync(Ctx("ListChats"), TextResult("Search results: ...markdown, not JSON..."));

        await act.Should().NotThrowAsync();
        _cache.GetAllChats().Should().BeNull();
    }

    [Fact]
    public async Task EmptyValueArray_CachesNothing()
    {
        await _hook.OnPostInvokeAsync(Ctx("ListChats"), TextResult("""{"value":[]}"""));

        // GetAllChats returns null when the cache is empty.
        _cache.GetAllChats().Should().BeNull();
    }

    [Fact]
    public async Task ErrorResult_IsIgnored()
    {
        var errorResult = new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = """{"value":[{"id":"x","chatType":"Group"}]}""" }]
        };

        await _hook.OnPostInvokeAsync(Ctx("ListChats"), errorResult);

        _cache.GetAllChats().Should().BeNull();
    }
}
