using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Filtering;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Filtering;

public class ResourceFiltersTests
{
    private static Resource CreateResource(string uri) => new()
    {
        Uri = uri,
        Name = uri,
        Description = $"Test resource: {uri}"
    };

    public class NoResourceFilterTests
    {
        [Fact]
        public void ShouldInclude_Always_Returns_True()
        {
            // Arrange
            var filter = NoResourceFilter.Instance;
            var resource = CreateResource("file:///test.txt");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeTrue();
        }

        [Fact]
        public void Instance_Returns_Same_Singleton()
        {
            // Act & Assert
            NoResourceFilter.Instance.Should().BeSameAs(NoResourceFilter.Instance);
        }
    }

    public class AllowListFilterTests
    {
        [Fact]
        public void Includes_Matching_URI()
        {
            // Arrange
            var filter = new ResourceAllowListFilter(["file:///test.txt"]);
            var resource = CreateResource("file:///test.txt");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeTrue();
        }

        [Fact]
        public void Excludes_Non_Matching_URI()
        {
            // Arrange
            var filter = new ResourceAllowListFilter(["file:///test.txt"]);
            var resource = CreateResource("file:///other.txt");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeFalse();
        }

        [Fact]
        public void Supports_Wildcard_Star()
        {
            // Arrange
            var filter = new ResourceAllowListFilter(["file:///docs/*"]);
            var resource = CreateResource("file:///docs/readme.md");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeTrue();
        }

        [Fact]
        public void Wildcard_Does_Not_Match_Partial()
        {
            // Arrange
            var filter = new ResourceAllowListFilter(["file:///docs/*"]);
            var resource = CreateResource("file:///images/photo.png");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeFalse();
        }

        [Fact]
        public void Supports_Wildcard_Question_Mark()
        {
            // Arrange
            var filter = new ResourceAllowListFilter(["file:///test?.txt"]);

            // Act & Assert
            filter.ShouldInclude(CreateResource("file:///test1.txt"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateResource("file:///test.txt"), "server").Should().BeFalse();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Respects_Case_Sensitivity(bool caseInsensitive)
        {
            // Arrange
            var filter = new ResourceAllowListFilter(["FILE:///TEST.TXT"], caseInsensitive);
            var resource = CreateResource("file:///test.txt");

            // Act
            var result = filter.ShouldInclude(resource, "server");

            // Assert
            result.Should().Be(caseInsensitive);
        }

        [Fact]
        public void Multiple_Patterns_Uses_OR_Logic()
        {
            // Arrange
            var filter = new ResourceAllowListFilter(["file:///a.txt", "file:///b.txt"]);

            // Act & Assert
            filter.ShouldInclude(CreateResource("file:///a.txt"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateResource("file:///b.txt"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateResource("file:///c.txt"), "server").Should().BeFalse();
        }
    }

    public class DenyListFilterTests
    {
        [Fact]
        public void Excludes_Matching_URI()
        {
            // Arrange
            var filter = new ResourceDenyListFilter(["file:///secret.txt"]);
            var resource = CreateResource("file:///secret.txt");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeFalse();
        }

        [Fact]
        public void Includes_Non_Matching_URI()
        {
            // Arrange
            var filter = new ResourceDenyListFilter(["file:///secret.txt"]);
            var resource = CreateResource("file:///public.txt");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeTrue();
        }

        [Fact]
        public void Supports_Wildcards()
        {
            // Arrange
            var filter = new ResourceDenyListFilter(["file:///secret/*"]);

            // Act & Assert
            filter.ShouldInclude(CreateResource("file:///secret/keys.txt"), "server").Should().BeFalse();
            filter.ShouldInclude(CreateResource("file:///public/readme.md"), "server").Should().BeTrue();
        }
    }

    public class RegexFilterTests
    {
        [Fact]
        public void Includes_Matching_Pattern()
        {
            // Arrange
            var filter = new ResourceRegexFilter("file:///docs/.*");
            var resource = CreateResource("file:///docs/readme.md");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeTrue();
        }

        [Fact]
        public void Excludes_Non_Matching_Pattern()
        {
            // Arrange
            var filter = new ResourceRegexFilter("file:///docs/.*");
            var resource = CreateResource("file:///images/photo.png");

            // Act & Assert
            filter.ShouldInclude(resource, "server").Should().BeFalse();
        }

        [Fact]
        public void Exclude_Pattern_Takes_Priority()
        {
            // Arrange
            var filter = new ResourceRegexFilter("file:///.*", ".*\\.secret$");

            // Act & Assert
            filter.ShouldInclude(CreateResource("file:///readme.md"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateResource("file:///keys.secret"), "server").Should().BeFalse();
        }
    }

    public class ResourceFilterFactoryTests
    {
        [Fact]
        public void Creates_NoFilter_For_None_Mode()
        {
            // Arrange
            var config = new FilterConfiguration { Mode = FilterMode.None };

            // Act
            var filter = ResourceFilterFactory.Create(config);

            // Assert
            filter.Should().BeSameAs(NoResourceFilter.Instance);
        }

        [Fact]
        public void Creates_AllowList_Filter()
        {
            // Arrange
            var config = new FilterConfiguration
            {
                Mode = FilterMode.AllowList,
                Patterns = ["file:///a.txt", "file:///b.txt"]
            };

            // Act
            var filter = ResourceFilterFactory.Create(config);

            // Assert
            filter.Should().BeOfType<ResourceAllowListFilter>();
            filter.ShouldInclude(CreateResource("file:///a.txt"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateResource("file:///c.txt"), "server").Should().BeFalse();
        }

        [Fact]
        public void Creates_DenyList_Filter()
        {
            // Arrange
            var config = new FilterConfiguration
            {
                Mode = FilterMode.DenyList,
                Patterns = ["file:///secret.txt"]
            };

            // Act
            var filter = ResourceFilterFactory.Create(config);

            // Assert
            filter.Should().BeOfType<ResourceDenyListFilter>();
            filter.ShouldInclude(CreateResource("file:///secret.txt"), "server").Should().BeFalse();
            filter.ShouldInclude(CreateResource("file:///public.txt"), "server").Should().BeTrue();
        }

        [Fact]
        public void Creates_Regex_Filter()
        {
            // Arrange
            var config = new FilterConfiguration
            {
                Mode = FilterMode.Regex,
                Patterns = ["file:///docs/.*"]
            };

            // Act
            var filter = ResourceFilterFactory.Create(config);

            // Assert
            filter.Should().BeOfType<ResourceRegexFilter>();
        }

        [Fact]
        public void Creates_Regex_Filter_With_Exclude_Pattern()
        {
            // Arrange
            var config = new FilterConfiguration
            {
                Mode = FilterMode.Regex,
                Patterns = ["file:///.*", ".*\\.secret$"]
            };

            // Act
            var filter = ResourceFilterFactory.Create(config);

            // Assert
            filter.Should().BeOfType<ResourceRegexFilter>();
            filter.ShouldInclude(CreateResource("file:///readme.md"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateResource("file:///keys.secret"), "server").Should().BeFalse();
        }
    }
}
