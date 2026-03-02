using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using McpProxy.Core.Hooks;
using McpProxy.Core.Hooks.BuiltIn;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks;

public class HookFactoryTests
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly HookFactory _factory;

    public HookFactoryTests()
    {
        _loggerFactory = Substitute.For<ILoggerFactory>();
        _loggerFactory.CreateLogger<LoggingHook>().Returns(Substitute.For<ILogger<LoggingHook>>());
        _factory = new HookFactory(_loggerFactory);
    }

    public class CreateHookTests : HookFactoryTests
    {
        [Fact]
        public void CreateHook_LoggingHook_ReturnsLoggingHook()
        {
            // Arrange
            var definition = new HookDefinition { Type = "logging" };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeOfType<LoggingHook>();
        }

        [Fact]
        public void CreateHook_InputTransformHook_ReturnsInputTransformHook()
        {
            // Arrange
            var definition = new HookDefinition { Type = "inputTransform" };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeOfType<InputTransformHook>();
        }

        [Fact]
        public void CreateHook_OutputTransformHook_ReturnsOutputTransformHook()
        {
            // Arrange
            var definition = new HookDefinition { Type = "outputTransform" };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeOfType<OutputTransformHook>();
        }

        [Fact]
        public void CreateHook_CaseInsensitive_ReturnsHook()
        {
            // Arrange
            var definition = new HookDefinition { Type = "LOGGING" };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeOfType<LoggingHook>();
        }

        [Fact]
        public void CreateHook_UnknownType_ThrowsArgumentException()
        {
            // Arrange
            var definition = new HookDefinition { Type = "unknownHook" };

            // Act
            var act = () => _factory.CreateHook(definition);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Unknown hook type: 'unknownHook'*");
        }
    }

    public class CreatePreInvokeHookTests : HookFactoryTests
    {
        [Fact]
        public void CreatePreInvokeHook_LoggingHook_ReturnsHook()
        {
            // Arrange - LoggingHook implements IToolHook (both pre and post)
            var definition = new HookDefinition { Type = "logging" };

            // Act
            var hook = _factory.CreatePreInvokeHook(definition);

            // Assert
            hook.Should().NotBeNull();
            hook.Should().BeOfType<LoggingHook>();
        }

        [Fact]
        public void CreatePreInvokeHook_InputTransformHook_ReturnsHook()
        {
            // Arrange - InputTransformHook implements IPreInvokeHook
            var definition = new HookDefinition { Type = "inputTransform" };

            // Act
            var hook = _factory.CreatePreInvokeHook(definition);

            // Assert
            hook.Should().NotBeNull();
            hook.Should().BeOfType<InputTransformHook>();
        }

        [Fact]
        public void CreatePreInvokeHook_OutputTransformHook_ReturnsNull()
        {
            // Arrange - OutputTransformHook only implements IPostInvokeHook
            var definition = new HookDefinition { Type = "outputTransform" };

            // Act
            var hook = _factory.CreatePreInvokeHook(definition);

            // Assert
            hook.Should().BeNull();
        }
    }

    public class CreatePostInvokeHookTests : HookFactoryTests
    {
        [Fact]
        public void CreatePostInvokeHook_LoggingHook_ReturnsHook()
        {
            // Arrange - LoggingHook implements IToolHook (both pre and post)
            var definition = new HookDefinition { Type = "logging" };

            // Act
            var hook = _factory.CreatePostInvokeHook(definition);

            // Assert
            hook.Should().NotBeNull();
            hook.Should().BeOfType<LoggingHook>();
        }

        [Fact]
        public void CreatePostInvokeHook_OutputTransformHook_ReturnsHook()
        {
            // Arrange - OutputTransformHook implements IPostInvokeHook
            var definition = new HookDefinition { Type = "outputTransform" };

            // Act
            var hook = _factory.CreatePostInvokeHook(definition);

            // Assert
            hook.Should().NotBeNull();
            hook.Should().BeOfType<OutputTransformHook>();
        }

        [Fact]
        public void CreatePostInvokeHook_InputTransformHook_ReturnsNull()
        {
            // Arrange - InputTransformHook only implements IPreInvokeHook
            var definition = new HookDefinition { Type = "inputTransform" };

            // Act
            var hook = _factory.CreatePostInvokeHook(definition);

            // Assert
            hook.Should().BeNull();
        }
    }

    public class ConfigurePipelineTests : HookFactoryTests
    {
        [Fact]
        public void ConfigurePipeline_EmptyConfig_DoesNotAddHooks()
        {
            // Arrange
            var config = new HooksConfiguration();
            var logger = Substitute.For<ILogger<HookPipeline>>();
            var pipeline = new HookPipeline(logger);

            // Act
            _factory.ConfigurePipeline(config, pipeline);

            // Assert
            pipeline.PreInvokeHookCount.Should().Be(0);
            pipeline.PostInvokeHookCount.Should().Be(0);
        }

        [Fact]
        public void ConfigurePipeline_PreInvokeHooks_AddsToPreInvoke()
        {
            // Arrange
            var config = new HooksConfiguration
            {
                PreInvoke =
                [
                    new HookDefinition { Type = "logging" },
                    new HookDefinition { Type = "inputTransform" }
                ]
            };
            var logger = Substitute.For<ILogger<HookPipeline>>();
            var pipeline = new HookPipeline(logger);

            // Act
            _factory.ConfigurePipeline(config, pipeline);

            // Assert
            pipeline.PreInvokeHookCount.Should().Be(2);
            pipeline.PostInvokeHookCount.Should().Be(0);
        }

        [Fact]
        public void ConfigurePipeline_PostInvokeHooks_AddsToPostInvoke()
        {
            // Arrange
            var config = new HooksConfiguration
            {
                PostInvoke =
                [
                    new HookDefinition { Type = "logging" },
                    new HookDefinition { Type = "outputTransform" }
                ]
            };
            var logger = Substitute.For<ILogger<HookPipeline>>();
            var pipeline = new HookPipeline(logger);

            // Act
            _factory.ConfigurePipeline(config, pipeline);

            // Assert
            pipeline.PreInvokeHookCount.Should().Be(0);
            pipeline.PostInvokeHookCount.Should().Be(2);
        }

        [Fact]
        public void ConfigurePipeline_MixedHooks_AddsBothTypes()
        {
            // Arrange
            var config = new HooksConfiguration
            {
                PreInvoke = [new HookDefinition { Type = "logging" }],
                PostInvoke = [new HookDefinition { Type = "outputTransform" }]
            };
            var logger = Substitute.For<ILogger<HookPipeline>>();
            var pipeline = new HookPipeline(logger);

            // Act
            _factory.ConfigurePipeline(config, pipeline);

            // Assert
            pipeline.PreInvokeHookCount.Should().Be(1);
            pipeline.PostInvokeHookCount.Should().Be(1);
        }

        [Fact]
        public void ConfigurePipeline_IncompatibleHook_SkipsHook()
        {
            // Arrange - outputTransform is not a pre-invoke hook
            var config = new HooksConfiguration
            {
                PreInvoke = [new HookDefinition { Type = "outputTransform" }]
            };
            var logger = Substitute.For<ILogger<HookPipeline>>();
            var pipeline = new HookPipeline(logger);

            // Act
            _factory.ConfigurePipeline(config, pipeline);

            // Assert
            pipeline.PreInvokeHookCount.Should().Be(0);
        }
    }

    public class LoggingHookConfigTests : HookFactoryTests
    {
        [Fact]
        public void CreateHook_LoggingWithConfig_CreatesConfiguredHook()
        {
            // Arrange
            var definition = new HookDefinition
            {
                Type = "logging",
                Config = new Dictionary<string, object?>
                {
                    ["logLevel"] = JsonSerializer.SerializeToElement("Debug"),
                    ["logArguments"] = JsonSerializer.SerializeToElement(true),
                    ["logResult"] = JsonSerializer.SerializeToElement(true)
                }
            };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeOfType<LoggingHook>();
        }

        [Fact]
        public void CreateHook_LoggingWithInvalidLogLevel_UsesDefaultLevel()
        {
            // Arrange
            var definition = new HookDefinition
            {
                Type = "logging",
                Config = new Dictionary<string, object?>
                {
                    ["logLevel"] = JsonSerializer.SerializeToElement("InvalidLevel")
                }
            };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeOfType<LoggingHook>();
        }
    }

    public class OutputTransformHookConfigTests : HookFactoryTests
    {
        private static readonly string[] s_redactPatterns = ["password", "secret"];

        [Fact]
        public void CreateHook_OutputTransformWithRedactPatterns_CreatesConfiguredHook()
        {
            // Arrange
            var definition = new HookDefinition
            {
                Type = "outputTransform",
                Config = new Dictionary<string, object?>
                {
                    ["redactPatterns"] = JsonSerializer.SerializeToElement(s_redactPatterns),
                    ["redactedValue"] = JsonSerializer.SerializeToElement("***")
                }
            };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeOfType<OutputTransformHook>();
        }
    }

    public class InputTransformHookConfigTests : HookFactoryTests
    {
        [Fact]
        public void CreateHook_InputTransformWithDefaults_CreatesConfiguredHook()
        {
            // Arrange
            var definition = new HookDefinition
            {
                Type = "inputTransform",
                Config = new Dictionary<string, object?>
                {
                    ["defaults"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
                    {
                        ["timeout"] = 30,
                        ["enabled"] = true
                    })
                }
            };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeOfType<InputTransformHook>();
        }
    }

    public class RegisterHookTypeTests : HookFactoryTests
    {
        [Fact]
        public void RegisterHookType_CustomHook_CanBeCreated()
        {
            // Arrange
            var customHook = new CustomTestHook();
            _factory.RegisterHookType("custom", (def, _) => customHook);
            var definition = new HookDefinition { Type = "custom" };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeSameAs(customHook);
        }

        [Fact]
        public void RegisterHookType_OverridesBuiltIn_UsesCustomCreator()
        {
            // Arrange
            var customHook = new CustomTestHook();
            _factory.RegisterHookType("logging", (def, _) => customHook);
            var definition = new HookDefinition { Type = "logging" };

            // Act
            var hook = _factory.CreateHook(definition);

            // Assert
            hook.Should().BeSameAs(customHook);
        }

        private class CustomTestHook : IPreInvokeHook
        {
            public int Priority => 0;
            public ValueTask OnPreInvokeAsync(HookContext<ModelContextProtocol.Protocol.CallToolRequestParams> context)
                => ValueTask.CompletedTask;
        }
    }
}
