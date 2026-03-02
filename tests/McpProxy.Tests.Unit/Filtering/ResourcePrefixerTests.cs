using McpProxy.Core.Configuration;
using McpProxy.Core.Filtering;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Filtering;

public sealed class ResourcePrefixerTests
{
    [Fact]
    public void Transform_AddsPrefix_ToResourceUri()
    {
        // Arrange
        var prefixer = new ResourcePrefixer("server1", "://");
        var resource = new Resource
        {
            Uri = "file:///path/to/file.txt",
            Name = "file.txt",
            Description = "A text file",
            MimeType = "text/plain"
        };

        // Act
        var result = prefixer.Transform(resource, "server1");

        // Assert
        Assert.Equal("server1://file:///path/to/file.txt", result.Uri);
        Assert.Equal("file.txt", result.Name);
        Assert.Equal("A text file", result.Description);
        Assert.Equal("text/plain", result.MimeType);
    }

    [Fact]
    public void Transform_PreservesAllResourceProperties()
    {
        // Arrange
        var prefixer = new ResourcePrefixer("backend");
        var resource = new Resource
        {
            Uri = "db://table/users",
            Name = "users",
            Description = "User table",
            MimeType = "application/json",
            Size = 1024,
            Annotations = new() { Audience = [Role.User] }
        };

        // Act
        var result = prefixer.Transform(resource, "server");

        // Assert
        Assert.Equal("backend://db://table/users", result.Uri);
        Assert.Equal("users", result.Name);
        Assert.Equal("User table", result.Description);
        Assert.Equal("application/json", result.MimeType);
        Assert.Equal(1024, result.Size);
        Assert.NotNull(result.Annotations);
    }

    [Fact]
    public void RemovePrefix_RemovesPrefixFromUri()
    {
        // Arrange
        var prefixer = new ResourcePrefixer("server1", "://");

        // Act
        var result = prefixer.RemovePrefix("server1://file:///path/to/file.txt");

        // Assert
        Assert.Equal("file:///path/to/file.txt", result);
    }

    [Fact]
    public void RemovePrefix_ReturnsOriginalUri_WhenPrefixNotFound()
    {
        // Arrange
        var prefixer = new ResourcePrefixer("server1", "://");

        // Act
        var result = prefixer.RemovePrefix("file:///path/to/file.txt");

        // Assert
        Assert.Equal("file:///path/to/file.txt", result);
    }

    [Fact]
    public void HasPrefix_ReturnsTrue_WhenUriHasPrefix()
    {
        // Arrange
        var prefixer = new ResourcePrefixer("server1", "://");

        // Act
        var result = prefixer.HasPrefix("server1://file:///path/to/file.txt");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasPrefix_ReturnsFalse_WhenUriDoesNotHavePrefix()
    {
        // Arrange
        var prefixer = new ResourcePrefixer("server1", "://");

        // Act
        var result = prefixer.HasPrefix("file:///path/to/file.txt");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AddPrefix_AddsPrefixToUri()
    {
        // Arrange
        var prefixer = new ResourcePrefixer("server1", "://");

        // Act
        var result = prefixer.AddPrefix("file:///path/to/file.txt");

        // Assert
        Assert.Equal("server1://file:///path/to/file.txt", result);
    }

    [Fact]
    public void Transform_UsesCustomSeparator()
    {
        // Arrange
        var prefixer = new ResourcePrefixer("server1", "::");
        var resource = new Resource
        {
            Uri = "file:///path/to/file.txt",
            Name = "file.txt"
        };

        // Act
        var result = prefixer.Transform(resource, "server1");

        // Assert
        Assert.Equal("server1::file:///path/to/file.txt", result.Uri);
    }
}

public sealed class NoResourceTransformTests
{
    [Fact]
    public void Transform_ReturnsSameResource()
    {
        // Arrange
        var resource = new Resource
        {
            Uri = "file:///path/to/file.txt",
            Name = "file.txt",
            Description = "A text file"
        };

        // Act
        var result = NoResourceTransform.Instance.Transform(resource, "server");

        // Assert
        Assert.Same(resource, result);
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        // Act & Assert
        Assert.Same(NoResourceTransform.Instance, NoResourceTransform.Instance);
    }
}

public sealed class ResourceTransformerFactoryTests
{
    [Fact]
    public void Create_ReturnsNoTransform_WhenPrefixIsNull()
    {
        // Arrange
        var config = new ResourcesConfiguration { Prefix = null };

        // Act
        var result = ResourceTransformerFactory.Create(config);

        // Assert
        Assert.IsType<NoResourceTransform>(result);
    }

    [Fact]
    public void Create_ReturnsNoTransform_WhenPrefixIsEmpty()
    {
        // Arrange
        var config = new ResourcesConfiguration { Prefix = "" };

        // Act
        var result = ResourceTransformerFactory.Create(config);

        // Assert
        Assert.IsType<NoResourceTransform>(result);
    }

    [Fact]
    public void Create_ReturnsResourcePrefixer_WhenPrefixIsSet()
    {
        // Arrange
        var config = new ResourcesConfiguration { Prefix = "server1" };

        // Act
        var result = ResourceTransformerFactory.Create(config);

        // Assert
        Assert.IsType<ResourcePrefixer>(result);
    }

    [Fact]
    public void Create_UsesDefaultSeparator()
    {
        // Arrange
        var config = new ResourcesConfiguration { Prefix = "server1" };
        var resource = new Resource { Uri = "file:///test.txt", Name = "test" };

        // Act
        var transformer = ResourceTransformerFactory.Create(config);
        var result = transformer.Transform(resource, "server");

        // Assert
        Assert.Equal("server1://file:///test.txt", result.Uri);
    }

    [Fact]
    public void Create_UsesCustomSeparator()
    {
        // Arrange
        var config = new ResourcesConfiguration { Prefix = "server1", PrefixSeparator = "::" };
        var resource = new Resource { Uri = "file:///test.txt", Name = "test" };

        // Act
        var transformer = ResourceTransformerFactory.Create(config);
        var result = transformer.Transform(resource, "server");

        // Assert
        Assert.Equal("server1::file:///test.txt", result.Uri);
    }
}
