using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Filtering;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Filtering;

public class PromptFiltersTests
{
    private static Prompt CreatePrompt(string name) => new()
    {
        Name = name,
        Description = $"Test prompt: {name}"
    };

    public class NoPromptFilterTests
    {
        [Fact]
        public void ShouldInclude_Always_Returns_True()
        {
            // Arrange
            var filter = NoPromptFilter.Instance;
            var prompt = CreatePrompt("test_prompt");

            // Act & Assert
            filter.ShouldInclude(prompt, "server").Should().BeTrue();
        }

        [Fact]
        public void Instance_Returns_Same_Singleton()
        {
            // Act & Assert
            NoPromptFilter.Instance.Should().BeSameAs(NoPromptFilter.Instance);
        }
    }

    public class AllowListFilterTests
    {
        [Fact]
        public void Includes_Matching_Prompt()
        {
            // Arrange
            var filter = new PromptAllowListFilter(["greeting"]);
            var prompt = CreatePrompt("greeting");

            // Act & Assert
            filter.ShouldInclude(prompt, "server").Should().BeTrue();
        }

        [Fact]
        public void Excludes_Non_Matching_Prompt()
        {
            // Arrange
            var filter = new PromptAllowListFilter(["greeting"]);
            var prompt = CreatePrompt("farewell");

            // Act & Assert
            filter.ShouldInclude(prompt, "server").Should().BeFalse();
        }

        [Fact]
        public void Supports_Wildcard_Star()
        {
            // Arrange
            var filter = new PromptAllowListFilter(["code_*"]);

            // Act & Assert
            filter.ShouldInclude(CreatePrompt("code_review"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("code_explain"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("doc_review"), "server").Should().BeFalse();
        }

        [Fact]
        public void Supports_Wildcard_Question_Mark()
        {
            // Arrange
            var filter = new PromptAllowListFilter(["test?"]);

            // Act & Assert
            filter.ShouldInclude(CreatePrompt("test1"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("testA"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("test"), "server").Should().BeFalse();
            filter.ShouldInclude(CreatePrompt("test12"), "server").Should().BeFalse();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Respects_Case_Sensitivity(bool caseInsensitive)
        {
            // Arrange
            var filter = new PromptAllowListFilter(["GREETING"], caseInsensitive);
            var prompt = CreatePrompt("greeting");

            // Act
            var result = filter.ShouldInclude(prompt, "server");

            // Assert
            result.Should().Be(caseInsensitive);
        }

        [Fact]
        public void Multiple_Patterns_Uses_OR_Logic()
        {
            // Arrange
            var filter = new PromptAllowListFilter(["greeting", "farewell"]);

            // Act & Assert
            filter.ShouldInclude(CreatePrompt("greeting"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("farewell"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("other"), "server").Should().BeFalse();
        }
    }

    public class DenyListFilterTests
    {
        [Fact]
        public void Excludes_Matching_Prompt()
        {
            // Arrange
            var filter = new PromptDenyListFilter(["internal_*"]);
            var prompt = CreatePrompt("internal_debug");

            // Act & Assert
            filter.ShouldInclude(prompt, "server").Should().BeFalse();
        }

        [Fact]
        public void Includes_Non_Matching_Prompt()
        {
            // Arrange
            var filter = new PromptDenyListFilter(["internal_*"]);
            var prompt = CreatePrompt("greeting");

            // Act & Assert
            filter.ShouldInclude(prompt, "server").Should().BeTrue();
        }

        [Fact]
        public void Supports_Wildcards()
        {
            // Arrange
            var filter = new PromptDenyListFilter(["debug_*", "test_*"]);

            // Act & Assert
            filter.ShouldInclude(CreatePrompt("debug_info"), "server").Should().BeFalse();
            filter.ShouldInclude(CreatePrompt("test_prompt"), "server").Should().BeFalse();
            filter.ShouldInclude(CreatePrompt("production_prompt"), "server").Should().BeTrue();
        }
    }

    public class RegexFilterTests
    {
        [Fact]
        public void Includes_Matching_Pattern()
        {
            // Arrange
            var filter = new PromptRegexFilter("^code_.*");
            var prompt = CreatePrompt("code_review");

            // Act & Assert
            filter.ShouldInclude(prompt, "server").Should().BeTrue();
        }

        [Fact]
        public void Excludes_Non_Matching_Pattern()
        {
            // Arrange
            var filter = new PromptRegexFilter("^code_.*");
            var prompt = CreatePrompt("doc_review");

            // Act & Assert
            filter.ShouldInclude(prompt, "server").Should().BeFalse();
        }

        [Fact]
        public void Exclude_Pattern_Takes_Priority()
        {
            // Arrange
            var filter = new PromptRegexFilter(".*", ".*_debug$");

            // Act & Assert
            filter.ShouldInclude(CreatePrompt("code_review"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("code_debug"), "server").Should().BeFalse();
        }
    }

    public class PromptFilterFactoryTests
    {
        [Fact]
        public void Creates_NoFilter_For_None_Mode()
        {
            // Arrange
            var config = new FilterConfiguration { Mode = FilterMode.None };

            // Act
            var filter = PromptFilterFactory.Create(config);

            // Assert
            filter.Should().BeSameAs(NoPromptFilter.Instance);
        }

        [Fact]
        public void Creates_AllowList_Filter()
        {
            // Arrange
            var config = new FilterConfiguration
            {
                Mode = FilterMode.AllowList,
                Patterns = ["greeting", "farewell"]
            };

            // Act
            var filter = PromptFilterFactory.Create(config);

            // Assert
            filter.Should().BeOfType<PromptAllowListFilter>();
            filter.ShouldInclude(CreatePrompt("greeting"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("other"), "server").Should().BeFalse();
        }

        [Fact]
        public void Creates_DenyList_Filter()
        {
            // Arrange
            var config = new FilterConfiguration
            {
                Mode = FilterMode.DenyList,
                Patterns = ["internal_*"]
            };

            // Act
            var filter = PromptFilterFactory.Create(config);

            // Assert
            filter.Should().BeOfType<PromptDenyListFilter>();
            filter.ShouldInclude(CreatePrompt("internal_debug"), "server").Should().BeFalse();
            filter.ShouldInclude(CreatePrompt("greeting"), "server").Should().BeTrue();
        }

        [Fact]
        public void Creates_Regex_Filter()
        {
            // Arrange
            var config = new FilterConfiguration
            {
                Mode = FilterMode.Regex,
                Patterns = ["^code_.*"]
            };

            // Act
            var filter = PromptFilterFactory.Create(config);

            // Assert
            filter.Should().BeOfType<PromptRegexFilter>();
        }

        [Fact]
        public void Creates_Regex_Filter_With_Exclude_Pattern()
        {
            // Arrange
            var config = new FilterConfiguration
            {
                Mode = FilterMode.Regex,
                Patterns = [".*", ".*_debug$"]
            };

            // Act
            var filter = PromptFilterFactory.Create(config);

            // Assert
            filter.Should().BeOfType<PromptRegexFilter>();
            filter.ShouldInclude(CreatePrompt("code_review"), "server").Should().BeTrue();
            filter.ShouldInclude(CreatePrompt("code_debug"), "server").Should().BeFalse();
        }
    }
}
