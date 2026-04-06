using McpProxy.SDK.Filtering;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Filtering;

public class ToolFiltersTests
{
    private static Tool CreateTool(string name) => new()
    {
        Name = name,
        Description = $"Test tool: {name}"
    };

    public class NoFilterTests
    {
        [Fact]
        public void ShouldInclude_Always_ReturnsTrue()
        {
            // Arrange
            var filter = NoFilter.Instance;
            var tool = CreateTool("any-tool");

            // Act
            var result = filter.ShouldInclude(tool, "server1");

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData("tool1")]
        [InlineData("another-tool")]
        [InlineData("TEST_TOOL")]
        [InlineData("")]
        public void ShouldInclude_AnyToolName_ReturnsTrue(string toolName)
        {
            // Arrange
            var filter = NoFilter.Instance;
            var tool = CreateTool(toolName);

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeTrue();
        }
    }

    public class AllowListFilterTests
    {
        [Fact]
        public void ShouldInclude_ExactMatch_ReturnsTrue()
        {
            // Arrange
            var filter = new AllowListFilter(["tool1", "tool2"]);
            var tool = CreateTool("tool1");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldInclude_NoMatch_ReturnsFalse()
        {
            // Arrange
            var filter = new AllowListFilter(["tool1", "tool2"]);
            var tool = CreateTool("tool3");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldInclude_CaseInsensitiveMatch_ReturnsTrue()
        {
            // Arrange
            var filter = new AllowListFilter(["MyTool"], caseInsensitive: true);
            var tool = CreateTool("mytool");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldInclude_CaseSensitiveNoMatch_ReturnsFalse()
        {
            // Arrange
            var filter = new AllowListFilter(["MyTool"], caseInsensitive: false);
            var tool = CreateTool("mytool");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeFalse();
        }

        [Theory]
        [InlineData("get*", "getTool", true)]
        [InlineData("get*", "setTool", false)]
        [InlineData("*_tool", "my_tool", true)]
        [InlineData("*_tool", "mytool", false)]
        [InlineData("tool?", "tool1", true)]
        [InlineData("tool?", "tool12", false)]
        [InlineData("*", "anything", true)]
        public void ShouldInclude_WildcardPatterns(string pattern, string toolName, bool expected)
        {
            // Arrange
            var filter = new AllowListFilter([pattern]);
            var tool = CreateTool(toolName);

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ShouldInclude_MixedPatternsAndExact_MatchesCorrectly()
        {
            // Arrange
            var filter = new AllowListFilter(["exact_tool", "prefix_*", "*_suffix"]);

            // Act & Assert
            filter.ShouldInclude(CreateTool("exact_tool"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateTool("prefix_anything"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateTool("anything_suffix"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateTool("no_match"), "server").Should().BeFalse();
        }
    }

    public class DenyListFilterTests
    {
        [Fact]
        public void ShouldInclude_ExactMatch_ReturnsFalse()
        {
            // Arrange
            var filter = new DenyListFilter(["blocked_tool"]);
            var tool = CreateTool("blocked_tool");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldInclude_NoMatch_ReturnsTrue()
        {
            // Arrange
            var filter = new DenyListFilter(["blocked_tool"]);
            var tool = CreateTool("allowed_tool");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData("dangerous_*", "dangerous_delete", false)]
        [InlineData("dangerous_*", "safe_tool", true)]
        [InlineData("*_internal", "api_internal", false)]
        [InlineData("*_internal", "api_public", true)]
        public void ShouldInclude_WildcardPatterns(string pattern, string toolName, bool expected)
        {
            // Arrange
            var filter = new DenyListFilter([pattern]);
            var tool = CreateTool(toolName);

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ShouldInclude_MultiplePatterns_BlocksAll()
        {
            // Arrange
            var filter = new DenyListFilter(["internal_*", "*_debug", "admin"]);

            // Act & Assert
            filter.ShouldInclude(CreateTool("internal_api"), "server").Should().BeFalse();
            filter.ShouldInclude(CreateTool("tool_debug"), "server").Should().BeFalse();
            filter.ShouldInclude(CreateTool("admin"), "server").Should().BeFalse();
            filter.ShouldInclude(CreateTool("public_api"), "server").Should().BeTrue();
        }
    }

    public class RegexFilterTests
    {
        [Fact]
        public void ShouldInclude_MatchesIncludePattern_ReturnsTrue()
        {
            // Arrange
            var filter = new RegexFilter(includePattern: "^get_.*$");
            var tool = CreateTool("get_users");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldInclude_DoesNotMatchIncludePattern_ReturnsFalse()
        {
            // Arrange
            var filter = new RegexFilter(includePattern: "^get_.*$");
            var tool = CreateTool("set_users");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldInclude_MatchesExcludePattern_ReturnsFalse()
        {
            // Arrange
            var filter = new RegexFilter(includePattern: null, excludePattern: ".*_internal$");
            var tool = CreateTool("api_internal");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldInclude_BothPatterns_AppliesCorrectly()
        {
            // Arrange - include api_* but exclude *_internal
            var filter = new RegexFilter(includePattern: "^api_.*$", excludePattern: ".*_internal$");

            // Act & Assert
            filter.ShouldInclude(CreateTool("api_public"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateTool("api_internal"), "server").Should().BeFalse();
            filter.ShouldInclude(CreateTool("other_tool"), "server").Should().BeFalse();
        }

        [Fact]
        public void ShouldInclude_NoPatterns_ReturnsTrue()
        {
            // Arrange
            var filter = new RegexFilter(includePattern: null, excludePattern: null);
            var tool = CreateTool("any_tool");

            // Act
            var result = filter.ShouldInclude(tool, "server");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldInclude_CaseInsensitive_MatchesIgnoringCase()
        {
            // Arrange
            var filter = new RegexFilter(includePattern: "^GET_.*$", caseInsensitive: true);

            // Act & Assert
            filter.ShouldInclude(CreateTool("GET_users"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateTool("get_users"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateTool("Get_Users"), "server").Should().BeTrue();
        }

        [Fact]
        public void ShouldInclude_CaseSensitive_MatchesExactCase()
        {
            // Arrange
            var filter = new RegexFilter(includePattern: "^GET_.*$", caseInsensitive: false);

            // Act & Assert
            filter.ShouldInclude(CreateTool("GET_users"), "server").Should().BeTrue();
            filter.ShouldInclude(CreateTool("get_users"), "server").Should().BeFalse();
        }
    }
}
