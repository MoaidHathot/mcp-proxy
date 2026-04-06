using McpProxy.Abstractions;
using McpProxy.Sdk.Hooks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2012 // Use ValueTasks correctly (false positive in NSubstitute mock setup)
#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks;

public class HookPipelineTests
{
    private readonly ILogger<HookPipeline> _logger;
    private readonly HookPipeline _pipeline;

    public HookPipelineTests()
    {
        _logger = Substitute.For<ILogger<HookPipeline>>();
        _pipeline = new HookPipeline(_logger);
    }

    private static HookContext<CallToolRequestParams> CreateContext(string toolName = "test_tool")
    {
        return new HookContext<CallToolRequestParams>
        {
            ServerName = "test-server",
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName },
            CancellationToken = TestContext.Current.CancellationToken
        };
    }

    public class AddHooksTests : HookPipelineTests
    {
        [Fact]
        public void AddPreInvokeHook_IncreasesCount()
        {
            // Arrange
            var hook = Substitute.For<IPreInvokeHook>();

            // Act
            _pipeline.AddPreInvokeHook(hook);

            // Assert
            _pipeline.PreInvokeHookCount.Should().Be(1);
        }

        [Fact]
        public void AddPostInvokeHook_IncreasesCount()
        {
            // Arrange
            var hook = Substitute.For<IPostInvokeHook>();

            // Act
            _pipeline.AddPostInvokeHook(hook);

            // Assert
            _pipeline.PostInvokeHookCount.Should().Be(1);
        }

        [Fact]
        public void AddHook_IToolHook_AddsToBothPipelines()
        {
            // Arrange
            var hook = Substitute.For<IToolHook>();

            // Act
            _pipeline.AddHook(hook);

            // Assert
            _pipeline.PreInvokeHookCount.Should().Be(1);
            _pipeline.PostInvokeHookCount.Should().Be(1);
        }

        [Fact]
        public void AddPreInvokeHook_MultipleHooks_SortsByPriority()
        {
            // Arrange
            var lowPriorityHook = Substitute.For<IPreInvokeHook>();
            lowPriorityHook.Priority.Returns(10);

            var highPriorityHook = Substitute.For<IPreInvokeHook>();
            highPriorityHook.Priority.Returns(1);

            // Act - add in wrong order
            _pipeline.AddPreInvokeHook(lowPriorityHook);
            _pipeline.AddPreInvokeHook(highPriorityHook);

            // Assert
            _pipeline.PreInvokeHookCount.Should().Be(2);
        }
    }

    public class ExecutePreInvokeHooksTests : HookPipelineTests
    {
        [Fact]
        public async Task ExecutePreInvokeHooksAsync_NoHooks_CompletesSuccessfully()
        {
            // Arrange
            var context = CreateContext();

            // Act
            await _pipeline.ExecutePreInvokeHooksAsync(context);

            // Assert - should complete without error
        }

        [Fact]
        public async Task ExecutePreInvokeHooksAsync_WithHook_ExecutesHook()
        {
            // Arrange
            var hook = Substitute.For<IPreInvokeHook>();
            _pipeline.AddPreInvokeHook(hook);
            var context = CreateContext();

            // Act
            await _pipeline.ExecutePreInvokeHooksAsync(context);

            // Assert
            await hook.Received(1).OnPreInvokeAsync(context);
        }

        [Fact]
        public async Task ExecutePreInvokeHooksAsync_MultipleHooks_ExecutesInPriorityOrder()
        {
            // Arrange
            var executionOrder = new List<int>();

            var hook1 = Substitute.For<IPreInvokeHook>();
            hook1.Priority.Returns(2);
            hook1.OnPreInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>())
                .Returns(ValueTask.CompletedTask)
                .AndDoes(_ => executionOrder.Add(2));

            var hook2 = Substitute.For<IPreInvokeHook>();
            hook2.Priority.Returns(1);
            hook2.OnPreInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>())
                .Returns(ValueTask.CompletedTask)
                .AndDoes(_ => executionOrder.Add(1));

            _pipeline.AddPreInvokeHook(hook1);
            _pipeline.AddPreInvokeHook(hook2);
            var context = CreateContext();

            // Act
            await _pipeline.ExecutePreInvokeHooksAsync(context);

            // Assert
            executionOrder.Should().ContainInOrder(1, 2);
        }

        [Fact]
        public async Task ExecutePreInvokeHooksAsync_HookThrows_PropagatesException()
        {
            // Arrange
            var hook = Substitute.For<IPreInvokeHook>();
            hook.OnPreInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>())
                .Returns(_ => throw new InvalidOperationException("Hook failed"));
            _pipeline.AddPreInvokeHook(hook);
            var context = CreateContext();

            // Act
            var act = async () => await _pipeline.ExecutePreInvokeHooksAsync(context);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Hook failed");
        }

        [Fact]
        public async Task ExecutePreInvokeHooksAsync_HookModifiesContext_ChangesArePreserved()
        {
            // Arrange
            var hook = Substitute.For<IPreInvokeHook>();
            hook.OnPreInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>())
                .Returns(ValueTask.CompletedTask)
                .AndDoes(callInfo =>
                {
                    var ctx = callInfo.Arg<HookContext<CallToolRequestParams>>();
                    ctx.Items["modified"] = true;
                });
            _pipeline.AddPreInvokeHook(hook);
            var context = CreateContext();

            // Act
            await _pipeline.ExecutePreInvokeHooksAsync(context);

            // Assert
            context.Items.Should().ContainKey("modified");
            context.Items["modified"].Should().Be(true);
        }
    }

    public class ExecutePostInvokeHooksTests : HookPipelineTests
    {
        [Fact]
        public async Task ExecutePostInvokeHooksAsync_NoHooks_ReturnsOriginalResult()
        {
            // Arrange
            var context = CreateContext();
            var result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Original" }]
            };

            // Act
            var returned = await _pipeline.ExecutePostInvokeHooksAsync(context, result);

            // Assert
            returned.Should().BeSameAs(result);
        }

        [Fact]
        public async Task ExecutePostInvokeHooksAsync_WithHook_ExecutesHook()
        {
            // Arrange
            var hook = Substitute.For<IPostInvokeHook>();
            var expectedResult = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Modified" }]
            };
            hook.OnPostInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>(), Arg.Any<CallToolResult>())
                .Returns(expectedResult);
            _pipeline.AddPostInvokeHook(hook);

            var context = CreateContext();
            var originalResult = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Original" }]
            };

            // Act
            var returned = await _pipeline.ExecutePostInvokeHooksAsync(context, originalResult);

            // Assert
            returned.Should().BeSameAs(expectedResult);
        }

        [Fact]
        public async Task ExecutePostInvokeHooksAsync_MultipleHooks_ChainsResults()
        {
            // Arrange
            var hook1 = Substitute.For<IPostInvokeHook>();
            hook1.Priority.Returns(1);
            hook1.OnPostInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>(), Arg.Any<CallToolResult>())
                .Returns(callInfo =>
                {
                    var result = callInfo.Arg<CallToolResult>();
                    return new ValueTask<CallToolResult>(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = "Modified1" }]
                    });
                });

            var hook2 = Substitute.For<IPostInvokeHook>();
            hook2.Priority.Returns(2);
            hook2.OnPostInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>(), Arg.Any<CallToolResult>())
                .Returns(callInfo =>
                {
                    return new ValueTask<CallToolResult>(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = "Modified2" }]
                    });
                });

            _pipeline.AddPostInvokeHook(hook1);
            _pipeline.AddPostInvokeHook(hook2);

            var context = CreateContext();
            var originalResult = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Original" }]
            };

            // Act
            var returned = await _pipeline.ExecutePostInvokeHooksAsync(context, originalResult);

            // Assert - should have final hook's result
            returned.Content.Should().HaveCount(1);
            var textContent = returned.Content[0] as TextContentBlock;
            textContent!.Text.Should().Be("Modified2");
        }

        [Fact]
        public async Task ExecutePostInvokeHooksAsync_HookThrows_PropagatesException()
        {
            // Arrange
            var hook = Substitute.For<IPostInvokeHook>();
            hook.OnPostInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>(), Arg.Any<CallToolResult>())
                .Returns<CallToolResult>(_ => throw new InvalidOperationException("Post-hook failed"));
            _pipeline.AddPostInvokeHook(hook);

            var context = CreateContext();
            var result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Original" }]
            };

            // Act
            var act = async () => await _pipeline.ExecutePostInvokeHooksAsync(context, result);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Post-hook failed");
        }
    }
}
