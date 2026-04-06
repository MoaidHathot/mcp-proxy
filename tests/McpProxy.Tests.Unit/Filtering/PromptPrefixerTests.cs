using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Filtering;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Filtering;

public sealed class PromptPrefixerTests
{
    [Fact]
    public void Transform_AddsPrefix_ToPromptName()
    {
        // Arrange
        var prefixer = new PromptPrefixer("server1", "_");
        var prompt = new Prompt
        {
            Name = "summarize",
            Description = "Summarizes text"
        };

        // Act
        var result = prefixer.Transform(prompt, "server1");

        // Assert
        Assert.Equal("server1_summarize", result.Name);
        Assert.Equal("Summarizes text", result.Description);
    }

    [Fact]
    public void Transform_PreservesAllPromptProperties()
    {
        // Arrange
        var prefixer = new PromptPrefixer("backend");
        var prompt = new Prompt
        {
            Name = "analyze",
            Description = "Analyzes data",
            Arguments =
            [
                new PromptArgument { Name = "input", Description = "Input data", Required = true }
            ]
        };

        // Act
        var result = prefixer.Transform(prompt, "server");

        // Assert
        Assert.Equal("backend_analyze", result.Name);
        Assert.Equal("Analyzes data", result.Description);
        Assert.Single(result.Arguments!);
        Assert.Equal("input", result.Arguments![0].Name);
    }

    [Fact]
    public void RemovePrefix_RemovesPrefixFromName()
    {
        // Arrange
        var prefixer = new PromptPrefixer("server1", "_");

        // Act
        var result = prefixer.RemovePrefix("server1_summarize");

        // Assert
        Assert.Equal("summarize", result);
    }

    [Fact]
    public void RemovePrefix_ReturnsOriginalName_WhenPrefixNotFound()
    {
        // Arrange
        var prefixer = new PromptPrefixer("server1", "_");

        // Act
        var result = prefixer.RemovePrefix("summarize");

        // Assert
        Assert.Equal("summarize", result);
    }

    [Fact]
    public void HasPrefix_ReturnsTrue_WhenNameHasPrefix()
    {
        // Arrange
        var prefixer = new PromptPrefixer("server1", "_");

        // Act
        var result = prefixer.HasPrefix("server1_summarize");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasPrefix_ReturnsFalse_WhenNameDoesNotHavePrefix()
    {
        // Arrange
        var prefixer = new PromptPrefixer("server1", "_");

        // Act
        var result = prefixer.HasPrefix("summarize");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AddPrefix_AddsPrefixToName()
    {
        // Arrange
        var prefixer = new PromptPrefixer("server1", "_");

        // Act
        var result = prefixer.AddPrefix("summarize");

        // Assert
        Assert.Equal("server1_summarize", result);
    }

    [Fact]
    public void Transform_UsesCustomSeparator()
    {
        // Arrange
        var prefixer = new PromptPrefixer("server1", "::");
        var prompt = new Prompt
        {
            Name = "summarize",
            Description = "Summarizes text"
        };

        // Act
        var result = prefixer.Transform(prompt, "server1");

        // Assert
        Assert.Equal("server1::summarize", result.Name);
    }
}

public sealed class NoPromptTransformTests
{
    [Fact]
    public void Transform_ReturnsSamePrompt()
    {
        // Arrange
        var prompt = new Prompt
        {
            Name = "summarize",
            Description = "Summarizes text"
        };

        // Act
        var result = NoPromptTransform.Instance.Transform(prompt, "server");

        // Assert
        Assert.Same(prompt, result);
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        // Act & Assert
        Assert.Same(NoPromptTransform.Instance, NoPromptTransform.Instance);
    }
}

public sealed class PromptTransformerFactoryTests
{
    [Fact]
    public void Create_ReturnsNoTransform_WhenPrefixIsNull()
    {
        // Arrange
        var config = new PromptsConfiguration { Prefix = null };

        // Act
        var result = PromptTransformerFactory.Create(config);

        // Assert
        Assert.IsType<NoPromptTransform>(result);
    }

    [Fact]
    public void Create_ReturnsNoTransform_WhenPrefixIsEmpty()
    {
        // Arrange
        var config = new PromptsConfiguration { Prefix = "" };

        // Act
        var result = PromptTransformerFactory.Create(config);

        // Assert
        Assert.IsType<NoPromptTransform>(result);
    }

    [Fact]
    public void Create_ReturnsPromptPrefixer_WhenPrefixIsSet()
    {
        // Arrange
        var config = new PromptsConfiguration { Prefix = "server1" };

        // Act
        var result = PromptTransformerFactory.Create(config);

        // Assert
        Assert.IsType<PromptPrefixer>(result);
    }

    [Fact]
    public void Create_UsesDefaultSeparator()
    {
        // Arrange
        var config = new PromptsConfiguration { Prefix = "server1" };
        var prompt = new Prompt { Name = "summarize" };

        // Act
        var transformer = PromptTransformerFactory.Create(config);
        var result = transformer.Transform(prompt, "server");

        // Assert
        Assert.Equal("server1_summarize", result.Name);
    }

    [Fact]
    public void Create_UsesCustomSeparator()
    {
        // Arrange
        var config = new PromptsConfiguration { Prefix = "server1", PrefixSeparator = "::" };
        var prompt = new Prompt { Name = "summarize" };

        // Act
        var transformer = PromptTransformerFactory.Create(config);
        var result = transformer.Transform(prompt, "server");

        // Assert
        Assert.Equal("server1::summarize", result.Name);
    }
}
