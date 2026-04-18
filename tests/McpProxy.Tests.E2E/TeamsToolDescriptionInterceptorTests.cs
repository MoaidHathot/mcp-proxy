using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration;
using McpProxy.Samples.TeamsIntegration.Interceptors;
using ModelContextProtocol.Protocol;
using System.Text.Json;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.E2E;

/// <summary>
/// Tests for <see cref="TeamsToolDescriptionInterceptor"/> — verifies that Teams tool
/// descriptions are enhanced with proxy capability information.
/// </summary>
public class TeamsToolDescriptionInterceptorTests
{
    private static Tool CreateTool(string name, string? description = null)
    {
        return new Tool
        {
            Name = name,
            Description = description ?? $"Original description for {name}",
            InputSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement
        };
    }

    private static ToolWithServer CreateToolWithServer(string name, string serverName = "teams", string? description = null)
    {
        return new ToolWithServer
        {
            Tool = CreateTool(name, description),
            OriginalName = name,
            ServerName = serverName,
            Include = true
        };
    }

    public class InterceptToolsTests : TeamsToolDescriptionInterceptorTests
    {
        [Fact]
        public void Enhances_Known_Teams_Tool_Description()
        {
            // Arrange
            var options = new TeamsIntegrationOptions
            {
                EnableCacheShortCircuit = true,
                EnableAutoPagination = true,
                EnableCredentialScanning = true
            };
            var interceptor = new TeamsToolDescriptionInterceptor(options);
            var tools = new List<ToolWithServer>
            {
                CreateToolWithServer("teams_ListChats")
            };

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            result.Should().HaveCount(1);
            result[0].Tool.Description.Should().Contain("pagination");
            result[0].Tool.Description.Should().Contain("caches");
            result[0].Tool.Description.Should().Contain("forceRefresh");
            result[0].Tool.Name.Should().Be("teams_ListChats");
        }

        [Fact]
        public void Enhances_PostMessage_With_Credential_Scanning_Info()
        {
            // Arrange
            var options = new TeamsIntegrationOptions { EnableCredentialScanning = true };
            var interceptor = new TeamsToolDescriptionInterceptor(options);
            var tools = new List<ToolWithServer>
            {
                CreateToolWithServer("teams_PostMessage")
            };

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            result[0].Tool.Description.Should().Contain("credential");
            result[0].Tool.Description.Should().Contain("block");
        }

        [Fact]
        public void Does_Not_Modify_Non_Teams_Tools()
        {
            // Arrange
            var options = new TeamsIntegrationOptions();
            var interceptor = new TeamsToolDescriptionInterceptor(options);
            var tools = new List<ToolWithServer>
            {
                CreateToolWithServer("some_other_tool", serverName: "other-server", description: "Original description")
            };

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            result.Should().HaveCount(1);
            result[0].Tool.Description.Should().Be("Original description");
        }

        [Fact]
        public void Appends_Proxy_Suffix_To_Unknown_Teams_Tools()
        {
            // Arrange
            var options = new TeamsIntegrationOptions
            {
                EnableCacheShortCircuit = true,
                EnableAutoPagination = true,
                EnableCredentialScanning = true
            };
            var interceptor = new TeamsToolDescriptionInterceptor(options);
            var tools = new List<ToolWithServer>
            {
                CreateToolWithServer("teams_SomeNewTool", description: "A new tool")
            };

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            result[0].Tool.Description.Should().StartWith("A new tool");
            result[0].Tool.Description.Should().Contain("[Proxy:");
            result[0].Tool.Description.Should().Contain("auto-caching");
            result[0].Tool.Description.Should().Contain("auto-pagination");
            result[0].Tool.Description.Should().Contain("credential scanning");
        }

        [Fact]
        public void No_Proxy_Suffix_When_All_Features_Disabled()
        {
            // Arrange
            var options = new TeamsIntegrationOptions
            {
                EnableCacheShortCircuit = false,
                EnableAutoPagination = false,
                EnableCredentialScanning = false
            };
            var interceptor = new TeamsToolDescriptionInterceptor(options);
            var tools = new List<ToolWithServer>
            {
                CreateToolWithServer("teams_SomeNewTool", description: "A new tool")
            };

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            result[0].Tool.Description.Should().Be("A new tool");
        }

        [Fact]
        public void Preserves_Tool_Metadata()
        {
            // Arrange
            var options = new TeamsIntegrationOptions();
            var interceptor = new TeamsToolDescriptionInterceptor(options);
            var tool = new Tool
            {
                Name = "teams_ListChats",
                Title = "List Chats",
                Description = "Original",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"top":{"type":"integer"}}}""").RootElement,
            };
            var tools = new List<ToolWithServer>
            {
                new ToolWithServer
                {
                    Tool = tool,
                    OriginalName = "ListChats",
                    ServerName = "teams",
                    Include = true
                }
            };

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            result[0].Tool.Name.Should().Be("teams_ListChats");
            result[0].Tool.Title.Should().Be("List Chats");
            result[0].Tool.InputSchema.GetProperty("properties").GetProperty("top").GetProperty("type").GetString()
                .Should().Be("integer");
            result[0].OriginalName.Should().Be("ListChats");
        }

        [Fact]
        public void Matches_Tools_By_Custom_Server_Name()
        {
            // Arrange
            var options = new TeamsIntegrationOptions { TeamsServerName = "my-teams" };
            var interceptor = new TeamsToolDescriptionInterceptor(options);
            var tools = new List<ToolWithServer>
            {
                CreateToolWithServer("ListChats", serverName: "my-teams")
            };

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            // "ListChats" should match because the server name matches
            result[0].Tool.Description.Should().Contain("pagination");
        }

        [Fact]
        public void Handles_Multiple_Prefix_Formats()
        {
            // Arrange
            var options = new TeamsIntegrationOptions();
            var interceptor = new TeamsToolDescriptionInterceptor(options);
            var tools = new List<ToolWithServer>
            {
                CreateToolWithServer("teams_ListTeams"),
                CreateToolWithServer("teams_PostMessage"),
                CreateToolWithServer("teams_ListChatMessages"),
            };

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            result.Should().HaveCount(3);
            result[0].Tool.Description.Should().Contain("teams"); // "List the user's teams"
            result[1].Tool.Description.Should().Contain("credential"); // PostMessage mentions credential scanning
            result[2].Tool.Description.Should().Contain("messages"); // ListChatMessages mentions messages
        }

        [Fact]
        public void Descriptions_Do_Not_Reference_Virtual_Tools()
        {
            // Arrange — this is a critical requirement: enhanced descriptions should NOT
            // direct the LLM to call virtual tools like teams_resolve
            var options = new TeamsIntegrationOptions
            {
                EnableCacheShortCircuit = true,
                EnableAutoPagination = true,
                EnableCredentialScanning = true
            };
            var interceptor = new TeamsToolDescriptionInterceptor(options);

            // Create all known enhanced tools
            var toolNames = new[]
            {
                "teams_ListChats", "teams_ListTeams", "teams_ListChannels",
                "teams_PostMessage", "teams_SendChatMessage", "teams_PostChannelMessage",
                "teams_ListChatMessages", "teams_ListChannelMessages",
                "teams_GetChat", "teams_GetTeam", "teams_GetChannel",
                "teams_CreateChat", "teams_SearchTeamsMessages"
            };
            var tools = toolNames.Select(n => CreateToolWithServer(n)).ToList();

            // Act
            var result = interceptor.InterceptTools(tools).ToList();

            // Assert
            foreach (var tool in result)
            {
                tool.Tool.Description.Should().NotContain("teams_resolve",
                    because: $"tool '{tool.Tool.Name}' description should not reference virtual tools");
                tool.Tool.Description.Should().NotContain("teams_cache_",
                    because: $"tool '{tool.Tool.Name}' description should not reference virtual tools");
                tool.Tool.Description.Should().NotContain("teams_lookup_",
                    because: $"tool '{tool.Tool.Name}' description should not reference virtual tools");
            }
        }
    }
}
