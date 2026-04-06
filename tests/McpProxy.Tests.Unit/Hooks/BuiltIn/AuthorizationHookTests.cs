using McpProxy.Abstractions;
using McpProxy.SDK.Exceptions;
using McpProxy.SDK.Hooks.BuiltIn;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class AuthorizationHookTests
{
    private readonly ILogger<AuthorizationHook> _logger;

    public AuthorizationHookTests()
    {
        _logger = Substitute.For<ILogger<AuthorizationHook>>();
    }

    private static HookContext<CallToolRequestParams> CreateContext(
        string toolName = "test_tool",
        string serverName = "test-server",
        string? principalId = null,
        string? roles = null,
        string? scopes = null)
    {
        AuthenticationResult? authResult = null;

        if (principalId is not null)
        {
            var properties = new Dictionary<string, string>();
            if (roles is not null) properties["roles"] = roles;
            if (scopes is not null) properties["scopes"] = scopes;

            authResult = AuthenticationResult.Success(principalId, properties);
        }

        return new HookContext<CallToolRequestParams>
        {
            ServerName = serverName,
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName },
            CancellationToken = TestContext.Current.CancellationToken,
            AuthenticationResult = authResult
        };
    }

    [Fact]
    public void Priority_ReturnsNegativeValue()
    {
        // Arrange
        var config = new AuthorizationConfiguration();
        var hook = new AuthorizationHook(_logger, config);

        // Assert
        hook.Priority.Should().BeLessThan(0);
    }

    [Fact]
    public async Task OnPreInvokeAsync_RequireAuthEnabled_NoAuth_ThrowsAuthorizationException()
    {
        // Arrange
        var config = new AuthorizationConfiguration { RequireAuthentication = true };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: null);

        // Act
        var act = async () => await hook.OnPreInvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_RequireAuthDisabled_NoAuth_Succeeds()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = false,
            DefaultAllow = true
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: null);

        // Act & Assert - should not throw
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_NoMatchingRules_DefaultAllowTrue_Succeeds()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            DefaultAllow = true,
            RequireAuthentication = false,
            Rules = []
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: "user1");

        // Act & Assert
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_NoMatchingRules_DefaultAllowFalse_Throws()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            DefaultAllow = false,
            RequireAuthentication = false,
            Rules = []
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: "user1");

        // Act
        var act = async () => await hook.OnPreInvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_RuleWithRequiredRole_UserHasRole_Succeeds()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "*",
                    RequiredRoles = ["admin"],
                    Allow = true
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: "user1", roles: "admin,user");

        // Act & Assert
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_RuleWithRequiredRole_UserLacksRole_Throws()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "*",
                    RequiredRoles = ["admin"],
                    Allow = true
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: "user1", roles: "user");

        // Act
        var act = async () => await hook.OnPreInvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_RuleWithRequiredScope_UserHasScope_Succeeds()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "*",
                    RequiredScopes = ["tool.invoke"],
                    Allow = true
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: "user1", scopes: "tool.invoke,tool.read");

        // Act & Assert
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_ToolPatternMatch_MatchesPrefixWildcard()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            DefaultAllow = false,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "test_*",
                    RequiredRoles = ["user"],
                    Allow = true
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(toolName: "test_something", principalId: "user1", roles: "user");

        // Act & Assert
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_ToolPatternMatch_MatchesSuffixWildcard()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            DefaultAllow = false,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "*_tool",
                    RequiredRoles = ["user"],
                    Allow = true
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(toolName: "my_tool", principalId: "user1", roles: "user");

        // Act & Assert
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_ServerPatternMatch_FiltersCorrectly()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            DefaultAllow = false,
            Rules =
            [
                new AuthorizationRule
                {
                    ServerPattern = "prod-*",
                    RequiredRoles = ["admin"],
                    Allow = true
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(serverName: "prod-server", principalId: "user1", roles: "admin");

        // Act & Assert
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_ModeAnyOf_OneRuleMatches_Succeeds()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            Mode = AuthorizationMode.AnyOf,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "*",
                    RequiredRoles = ["admin"],
                    Allow = true
                },
                new AuthorizationRule
                {
                    ToolPattern = "*",
                    RequiredRoles = ["user"],
                    Allow = true
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: "user1", roles: "user"); // Has user but not admin

        // Act & Assert - should succeed because at least one rule matches
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_ModeAllOf_NotAllRulesMatch_Throws()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            Mode = AuthorizationMode.AllOf,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "*",
                    RequiredRoles = ["admin"],
                    Allow = true
                },
                new AuthorizationRule
                {
                    ToolPattern = "*",
                    RequiredRoles = ["user"],
                    Allow = true
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(principalId: "user1", roles: "user"); // Has user but not admin

        // Act
        var act = async () => await hook.OnPreInvokeAsync(context);

        // Assert - should fail because not all rules match
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_DenyRule_BlocksAccess()
    {
        // Arrange
        var config = new AuthorizationConfiguration
        {
            RequireAuthentication = true,
            DefaultAllow = true,
            Rules =
            [
                new AuthorizationRule
                {
                    ToolPattern = "dangerous_*",
                    RequiredRoles = [],
                    Allow = false // Deny rule
                }
            ]
        };
        var hook = new AuthorizationHook(_logger, config);
        var context = CreateContext(toolName: "dangerous_tool", principalId: "user1", roles: "admin");

        // Act
        var act = async () => await hook.OnPreInvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<AuthorizationException>();
    }

}
