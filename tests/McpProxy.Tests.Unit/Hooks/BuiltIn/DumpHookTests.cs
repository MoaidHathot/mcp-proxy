using McpProxy.Abstractions;
using McpProxy.SDK.Debugging;
using McpProxy.SDK.Hooks.BuiltIn;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)
#pragma warning disable CA2012 // Use ValueTasks correctly (false positive in NSubstitute mock setup)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class DumpHookTests
{
    private readonly IRequestDumper _dumper;
    private readonly DumpHook _hook;

    public DumpHookTests()
    {
        _dumper = Substitute.For<IRequestDumper>();
        _dumper.DumpRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        _dumper.DumpResponseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        _hook = new DumpHook(_dumper, dumpRequests: true, dumpResponses: true);
    }

    private DumpHook CreateHook(bool dumpRequests = true, bool dumpResponses = true)
    {
        return new DumpHook(_dumper, dumpRequests, dumpResponses);
    }

    private static HookContext<CallToolRequestParams> CreateContext(string serverName = "test-server", string toolName = "test_tool")
    {
        return new HookContext<CallToolRequestParams>
        {
            ServerName = serverName,
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName },
            CancellationToken = CancellationToken.None
        };
    }

    public class PriorityTests : DumpHookTests
    {
        [Fact]
        public void Priority_IsNegative999_ForEarlyPreInvoke()
        {
            // Assert
            _hook.Priority.Should().Be(-999);
        }
    }

    public class OnPreInvokeAsyncTests : DumpHookTests
    {
        [Fact]
        public async Task OnPreInvokeAsync_CallsDumperWithCorrectParameters()
        {
            // Arrange
            var context = CreateContext("my-server", "my_tool");

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            await _dumper.Received(1).DumpRequestAsync(
                "my-server",
                "my_tool",
                context.Request,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task OnPreInvokeAsync_PassesCancellationToken()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var context = new HookContext<CallToolRequestParams>
            {
                ServerName = "server",
                ToolName = "tool",
                Request = new CallToolRequestParams { Name = "tool" },
                CancellationToken = cts.Token
            };

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            await _dumper.Received(1).DumpRequestAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                cts.Token);
        }

        [Fact]
        public async Task OnPreInvokeAsync_WhenDumpRequestsFalse_DoesNotCallDumper()
        {
            // Arrange
            var hook = CreateHook(dumpRequests: false, dumpResponses: true);
            var context = CreateContext();

            // Act
            await hook.OnPreInvokeAsync(context);

            // Assert
            await _dumper.DidNotReceive().DumpRequestAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>());
        }
    }

    public class OnPostInvokeAsyncTests : DumpHookTests
    {
        [Fact]
        public async Task OnPostInvokeAsync_CallsDumperWithCorrectParameters()
        {
            // Arrange
            var context = CreateContext("my-server", "my_tool");
            var result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "result" }]
            };

            // Need to call pre-invoke first to start the stopwatch
            await _hook.OnPreInvokeAsync(context);

            // Act
            await _hook.OnPostInvokeAsync(context, result);

            // Assert
            await _dumper.Received(1).DumpResponseAsync(
                "my-server",
                "my_tool",
                result,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task OnPostInvokeAsync_ReturnsOriginalResult()
        {
            // Arrange
            var context = CreateContext();
            var result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "original" }]
            };

            await _hook.OnPreInvokeAsync(context);

            // Act
            var returnedResult = await _hook.OnPostInvokeAsync(context, result);

            // Assert
            returnedResult.Should().BeSameAs(result);
        }

        [Fact]
        public async Task OnPostInvokeAsync_PassesCancellationToken()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var context = new HookContext<CallToolRequestParams>
            {
                ServerName = "server",
                ToolName = "tool",
                Request = new CallToolRequestParams { Name = "tool" },
                CancellationToken = cts.Token
            };
            var result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "result" }]
            };

            await _hook.OnPreInvokeAsync(context);

            // Act
            await _hook.OnPostInvokeAsync(context, result);

            // Assert
            await _dumper.Received(1).DumpResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<TimeSpan>(),
                cts.Token);
        }

        [Fact]
        public async Task OnPostInvokeAsync_TracksDuration()
        {
            // Arrange
            var context = CreateContext();
            var result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "result" }]
            };

            await _hook.OnPreInvokeAsync(context);

            // Add a small delay to ensure measurable duration
            await Task.Delay(10, TestContext.Current.CancellationToken);

            // Act
            await _hook.OnPostInvokeAsync(context, result);

            // Assert
            await _dumper.Received(1).DumpResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Is<TimeSpan>(ts => ts.TotalMilliseconds > 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task OnPostInvokeAsync_WhenDumpResponsesFalse_DoesNotCallDumper()
        {
            // Arrange
            var hook = CreateHook(dumpRequests: true, dumpResponses: false);
            var context = CreateContext();
            var result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "result" }]
            };

            await hook.OnPreInvokeAsync(context);

            // Act
            await hook.OnPostInvokeAsync(context, result);

            // Assert
            await _dumper.DidNotReceive().DumpResponseAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>());
        }
    }
}

public class DumpHookConfigurationTests
{
    [Fact]
    public void ServerFilter_DefaultsToNull()
    {
        // Arrange
        var config = new DumpHookConfiguration();

        // Assert
        config.ServerFilter.Should().BeNull();
    }

    [Fact]
    public void ToolFilter_DefaultsToNull()
    {
        // Arrange
        var config = new DumpHookConfiguration();

        // Assert
        config.ToolFilter.Should().BeNull();
    }

    [Fact]
    public void ServerFilter_CanBeSet()
    {
        // Arrange
        var config = new DumpHookConfiguration
        {
            ServerFilter = ["server1", "server2"]
        };

        // Assert
        config.ServerFilter.Should().ContainInOrder("server1", "server2");
    }

    [Fact]
    public void ToolFilter_CanBeSet()
    {
        // Arrange
        var config = new DumpHookConfiguration
        {
            ToolFilter = ["tool1", "tool2"]
        };

        // Assert
        config.ToolFilter.Should().ContainInOrder("tool1", "tool2");
    }
}
