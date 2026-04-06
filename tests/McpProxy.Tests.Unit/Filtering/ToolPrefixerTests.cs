using McpProxy.SDK.Filtering;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Filtering;

public class ToolPrefixerTests
{
    private static Tool CreateTool(string name) => new()
    {
        Name = name,
        Description = $"Test tool: {name}"
    };

    public class TransformTests
    {
        [Fact]
        public void Transform_DefaultSeparator_AddsPrefixWithUnderscore()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver");
            var tool = CreateTool("get_users");

            // Act
            var result = prefixer.Transform(tool, "server");

            // Assert
            result.Name.Should().Be("myserver_get_users");
        }

        [Fact]
        public void Transform_CustomSeparator_UsesCustomSeparator()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver", "::");
            var tool = CreateTool("get_users");

            // Act
            var result = prefixer.Transform(tool, "server");

            // Assert
            result.Name.Should().Be("myserver::get_users");
        }

        [Fact]
        public void Transform_PreservesOtherProperties()
        {
            // Arrange
            var prefixer = new ToolPrefixer("prefix");
            var tool = new Tool
            {
                Name = "original",
                Title = "Original Tool",
                Description = "A test tool"
            };

            // Act
            var result = prefixer.Transform(tool, "server");

            // Assert
            result.Name.Should().Be("prefix_original");
            result.Title.Should().Be("Original Tool");
            result.Description.Should().Be("A test tool");
        }

        [Fact]
        public void Transform_DoesNotMutateOriginalTool()
        {
            // Arrange
            var prefixer = new ToolPrefixer("prefix");
            var tool = CreateTool("original");

            // Act
            var result = prefixer.Transform(tool, "server");

            // Assert
            tool.Name.Should().Be("original");
            result.Name.Should().Be("prefix_original");
        }
    }

    public class RemovePrefixTests
    {
        [Fact]
        public void RemovePrefix_WithMatchingPrefix_RemovesPrefix()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver");

            // Act
            var result = prefixer.RemovePrefix("myserver_get_users");

            // Assert
            result.Should().Be("get_users");
        }

        [Fact]
        public void RemovePrefix_WithCustomSeparator_RemovesPrefixCorrectly()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver", "::");

            // Act
            var result = prefixer.RemovePrefix("myserver::get_users");

            // Assert
            result.Should().Be("get_users");
        }

        [Fact]
        public void RemovePrefix_NoMatchingPrefix_ReturnsOriginal()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver");

            // Act
            var result = prefixer.RemovePrefix("otherserver_get_users");

            // Assert
            result.Should().Be("otherserver_get_users");
        }

        [Fact]
        public void RemovePrefix_PartialPrefix_ReturnsOriginal()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver");

            // Act
            var result = prefixer.RemovePrefix("myserverget_users"); // missing separator

            // Assert
            result.Should().Be("myserverget_users");
        }

        [Fact]
        public void RemovePrefix_EmptyString_ReturnsEmpty()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver");

            // Act
            var result = prefixer.RemovePrefix("");

            // Assert
            result.Should().BeEmpty();
        }
    }

    public class HasPrefixTests
    {
        [Fact]
        public void HasPrefix_WithMatchingPrefix_ReturnsTrue()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver");

            // Act
            var result = prefixer.HasPrefix("myserver_get_users");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void HasPrefix_WithoutPrefix_ReturnsFalse()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver");

            // Act
            var result = prefixer.HasPrefix("otherserver_get_users");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void HasPrefix_WithCustomSeparator_ChecksCorrectly()
        {
            // Arrange
            var prefixer = new ToolPrefixer("myserver", "::");

            // Act & Assert
            prefixer.HasPrefix("myserver::get_users").Should().BeTrue();
            prefixer.HasPrefix("myserver_get_users").Should().BeFalse();
        }

        [Fact]
        public void HasPrefix_CaseSensitive_RequiresExactCase()
        {
            // Arrange
            var prefixer = new ToolPrefixer("MyServer");

            // Act & Assert
            prefixer.HasPrefix("MyServer_tool").Should().BeTrue();
            prefixer.HasPrefix("myserver_tool").Should().BeFalse();
            prefixer.HasPrefix("MYSERVER_tool").Should().BeFalse();
        }
    }

    public class NoTransformTests
    {
        [Fact]
        public void Transform_ReturnsSameTool()
        {
            // Arrange
            var transformer = NoTransform.Instance;
            var tool = CreateTool("test_tool");

            // Act
            var result = transformer.Transform(tool, "server");

            // Assert
            result.Should().BeSameAs(tool);
        }

        [Fact]
        public void Instance_IsSingleton()
        {
            // Assert
            NoTransform.Instance.Should().BeSameAs(NoTransform.Instance);
        }
    }
}
