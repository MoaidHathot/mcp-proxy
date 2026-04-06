using McpProxy.SDK.Configuration;

namespace McpProxy.Tests.Unit.Configuration;

public class ConfigurationLoaderTests
{
    private const string ValidStdioConfig = """
        {
            "mcp": {
                "test-server": {
                    "type": "stdio",
                    "command": "node",
                    "arguments": ["server.js"]
                }
            }
        }
        """;

    private const string ValidHttpConfig = """
        {
            "mcp": {
                "test-server": {
                    "type": "http",
                    "url": "http://localhost:8080"
                }
            }
        }
        """;

    public class LoadFromStringTests
    {
        [Fact]
        public void LoadFromString_ValidStdioConfig_ReturnsConfiguration()
        {
            // Act
            var config = ConfigurationLoader.LoadFromString(ValidStdioConfig);

            // Assert
            config.Should().NotBeNull();
            config.Mcp.Should().ContainKey("test-server");
            config.Mcp["test-server"].Type.Should().Be(ServerTransportType.Stdio);
            config.Mcp["test-server"].Command.Should().Be("node");
            config.Mcp["test-server"].Arguments.Should().Contain("server.js");
        }

        [Fact]
        public void LoadFromString_ValidHttpConfig_ReturnsConfiguration()
        {
            // Act
            var config = ConfigurationLoader.LoadFromString(ValidHttpConfig);

            // Assert
            config.Should().NotBeNull();
            config.Mcp.Should().ContainKey("test-server");
            config.Mcp["test-server"].Type.Should().Be(ServerTransportType.Http);
            config.Mcp["test-server"].Url.Should().Be("http://localhost:8080");
        }

        [Fact]
        public void LoadFromString_WithComments_ParsesSuccessfully()
        {
            // Arrange
            var jsonWithComments = """
                {
                    // This is a comment
                    "mcp": {
                        "test-server": {
                            "type": "stdio",
                            "command": "node"
                        }
                    }
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(jsonWithComments);

            // Assert
            config.Mcp.Should().ContainKey("test-server");
        }

        [Fact]
        public void LoadFromString_WithTrailingCommas_ParsesSuccessfully()
        {
            // Arrange
            var jsonWithTrailingComma = """
                {
                    "mcp": {
                        "test-server": {
                            "type": "stdio",
                            "command": "node",
                        },
                    },
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(jsonWithTrailingComma);

            // Assert
            config.Mcp.Should().ContainKey("test-server");
        }

        [Fact]
        public void LoadFromString_CaseInsensitiveProperties_ParsesSuccessfully()
        {
            // Arrange
            var jsonWithUpperCase = """
                {
                    "MCP": {
                        "test-server": {
                            "TYPE": "stdio",
                            "COMMAND": "node"
                        }
                    }
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(jsonWithUpperCase);

            // Assert
            config.Mcp.Should().ContainKey("test-server");
            config.Mcp["test-server"].Command.Should().Be("node");
        }

        [Fact]
        public void LoadFromString_StdioWithoutCommand_ThrowsException()
        {
            // Arrange
            var invalidConfig = """
                {
                    "mcp": {
                        "test-server": {
                            "type": "stdio"
                        }
                    }
                }
                """;

            // Act
            var act = () => ConfigurationLoader.LoadFromString(invalidConfig);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*STDIO transport requires a 'command' property*");
        }

        [Fact]
        public void LoadFromString_HttpWithoutUrl_ThrowsException()
        {
            // Arrange
            var invalidConfig = """
                {
                    "mcp": {
                        "test-server": {
                            "type": "http"
                        }
                    }
                }
                """;

            // Act
            var act = () => ConfigurationLoader.LoadFromString(invalidConfig);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*HTTP/SSE transport requires a 'url' property*");
        }

        [Fact]
        public void LoadFromString_HttpWithInvalidUrl_ThrowsException()
        {
            // Arrange
            var invalidConfig = """
                {
                    "mcp": {
                        "test-server": {
                            "type": "http",
                            "url": "not-a-valid-url"
                        }
                    }
                }
                """;

            // Act
            var act = () => ConfigurationLoader.LoadFromString(invalidConfig);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Invalid URL*");
        }

        [Fact]
        public void LoadFromString_MultipleServers_LoadsAll()
        {
            // Arrange
            var multiServerConfig = """
                {
                    "mcp": {
                        "server1": {
                            "type": "stdio",
                            "command": "node"
                        },
                        "server2": {
                            "type": "http",
                            "url": "http://localhost:8080"
                        },
                        "server3": {
                            "type": "sse",
                            "url": "http://localhost:9090"
                        }
                    }
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(multiServerConfig);

            // Assert
            config.Mcp.Should().HaveCount(3);
            config.Mcp["server1"].Type.Should().Be(ServerTransportType.Stdio);
            config.Mcp["server2"].Type.Should().Be(ServerTransportType.Http);
            config.Mcp["server3"].Type.Should().Be(ServerTransportType.Sse);
        }

        [Fact]
        public void LoadFromString_WithToolFilters_LoadsFilterConfig()
        {
            // Arrange
            var configWithFilters = """
                {
                    "mcp": {
                        "test-server": {
                            "type": "stdio",
                            "command": "node",
                            "tools": {
                                "filter": {
                                    "mode": "allowlist",
                                    "patterns": ["allowed_*", "public_*"]
                                }
                            }
                        }
                    }
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(configWithFilters);

            // Assert
            config.Mcp["test-server"].Tools.Filter.Mode.Should().Be(FilterMode.AllowList);
            config.Mcp["test-server"].Tools.Filter.Patterns.Should().Contain("allowed_*");
            config.Mcp["test-server"].Tools.Filter.Patterns.Should().Contain("public_*");
        }

        [Fact]
        public void LoadFromString_WithPrefix_LoadsPrefixConfig()
        {
            // Arrange
            var configWithPrefix = """
                {
                    "mcp": {
                        "test-server": {
                            "type": "stdio",
                            "command": "node",
                            "tools": {
                                "prefix": "my_server",
                                "prefixSeparator": "_"
                            }
                        }
                    }
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(configWithPrefix);

            // Assert
            config.Mcp["test-server"].Tools.Prefix.Should().Be("my_server");
            config.Mcp["test-server"].Tools.PrefixSeparator.Should().Be("_");
        }

        [Fact]
        public void LoadFromString_WithEnvironment_LoadsEnvironmentVariables()
        {
            // Arrange
            var configWithEnv = """
                {
                    "mcp": {
                        "test-server": {
                            "type": "stdio",
                            "command": "node",
                            "environment": {
                                "API_KEY": "secret123",
                                "DEBUG": "true"
                            }
                        }
                    }
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(configWithEnv);

            // Assert
            config.Mcp["test-server"].Environment.Should().ContainKey("API_KEY");
            config.Mcp["test-server"].Environment!["API_KEY"].Should().Be("secret123");
            config.Mcp["test-server"].Environment.Should().ContainKey("DEBUG");
        }

        [Fact]
        public void LoadFromString_EnabledFalse_LoadsCorrectly()
        {
            // Arrange
            var configWithDisabled = """
                {
                    "mcp": {
                        "disabled-server": {
                            "type": "stdio",
                            "command": "node",
                            "enabled": false
                        }
                    }
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(configWithDisabled);

            // Assert
            config.Mcp["disabled-server"].Enabled.Should().BeFalse();
        }

        [Fact]
        public void LoadFromString_DefaultsEnabledTrue()
        {
            // Act
            var config = ConfigurationLoader.LoadFromString(ValidStdioConfig);

            // Assert
            config.Mcp["test-server"].Enabled.Should().BeTrue();
        }
    }

    public class EnvironmentVariableSubstitutionTests
    {
        [Fact]
        public void LoadFromString_EnvColonSyntax_SubstitutesValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("TEST_API_KEY", "my-secret-key");
            try
            {
                var configWithEnvVar = """
                    {
                        "mcp": {
                            "test-server": {
                                "type": "http",
                                "url": "http://localhost:8080",
                                "headers": {
                                    "Authorization": "env:TEST_API_KEY"
                                }
                            }
                        }
                    }
                    """;

                // Act
                var config = ConfigurationLoader.LoadFromString(configWithEnvVar);

                // Assert
                config.Mcp["test-server"].Headers.Should().ContainKey("Authorization");
                config.Mcp["test-server"].Headers!["Authorization"].Should().Be("my-secret-key");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEST_API_KEY", null);
            }
        }

        [Fact]
        public void LoadFromString_DollarBraceSyntax_SubstitutesValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("TEST_URL_HOST", "api.example.com");
            try
            {
                var configWithEnvVar = """
                    {
                        "mcp": {
                            "test-server": {
                                "type": "http",
                                "url": "http://${TEST_URL_HOST}:8080"
                            }
                        }
                    }
                    """;

                // Act
                var config = ConfigurationLoader.LoadFromString(configWithEnvVar);

                // Assert
                config.Mcp["test-server"].Url.Should().Be("http://api.example.com:8080");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEST_URL_HOST", null);
            }
        }

        [Fact]
        public void LoadFromString_MissingEnvVar_KeepsOriginalValue()
        {
            // Arrange - ensure env var doesn't exist
            Environment.SetEnvironmentVariable("DEFINITELY_DOES_NOT_EXIST", null);

            var configWithMissingEnvVar = """
                {
                    "mcp": {
                        "test-server": {
                            "type": "http",
                            "url": "http://localhost:8080",
                            "headers": {
                                "X-Custom": "env:DEFINITELY_DOES_NOT_EXIST"
                            }
                        }
                    }
                }
                """;

            // Act
            var config = ConfigurationLoader.LoadFromString(configWithMissingEnvVar);

            // Assert - keeps original "env:..." value when not found
            config.Mcp["test-server"].Headers!["X-Custom"].Should().Be("env:DEFINITELY_DOES_NOT_EXIST");
        }
    }

    public class AuthenticationConfigTests
    {
        [Fact]
        public void LoadFromString_ApiKeyAuthEnabled_WithoutValue_ThrowsException()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "authentication": {
                            "enabled": true,
                            "type": "apiKey"
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var act = () => ConfigurationLoader.LoadFromString(config);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*API key authentication requires a 'value' property*");
        }

        [Fact]
        public void LoadFromString_BearerAuthEnabled_WithoutAuthority_ThrowsException()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "authentication": {
                            "enabled": true,
                            "type": "bearer"
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var act = () => ConfigurationLoader.LoadFromString(config);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Bearer authentication requires an 'authority' property*");
        }

        [Fact]
        public void LoadFromString_ValidApiKeyConfig_LoadsSuccessfully()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "authentication": {
                            "enabled": true,
                            "type": "apiKey",
                            "apiKey": {
                                "header": "X-API-Key",
                                "value": "secret123"
                            }
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Authentication.Enabled.Should().BeTrue();
            result.Proxy.Authentication.Type.Should().Be(AuthenticationType.ApiKey);
            result.Proxy.Authentication.ApiKey.Header.Should().Be("X-API-Key");
            result.Proxy.Authentication.ApiKey.Value.Should().Be("secret123");
        }
    }

    public class CapabilityConfigTests
    {
        [Fact]
        public void LoadFromString_DefaultCapabilities_AllEnabled()
        {
            // Arrange
            var config = """
                {
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Capabilities.Client.Sampling.Should().BeTrue();
            result.Proxy.Capabilities.Client.Elicitation.Should().BeTrue();
            result.Proxy.Capabilities.Client.Roots.Should().BeTrue();
        }

        [Fact]
        public void LoadFromString_DisabledSamplingCapability_LoadsCorrectly()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "capabilities": {
                            "client": {
                                "sampling": false
                            }
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Capabilities.Client.Sampling.Should().BeFalse();
            result.Proxy.Capabilities.Client.Elicitation.Should().BeTrue();
            result.Proxy.Capabilities.Client.Roots.Should().BeTrue();
        }

        [Fact]
        public void LoadFromString_DisabledElicitationCapability_LoadsCorrectly()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "capabilities": {
                            "client": {
                                "elicitation": false
                            }
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Capabilities.Client.Sampling.Should().BeTrue();
            result.Proxy.Capabilities.Client.Elicitation.Should().BeFalse();
            result.Proxy.Capabilities.Client.Roots.Should().BeTrue();
        }

        [Fact]
        public void LoadFromString_DisabledRootsCapability_LoadsCorrectly()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "capabilities": {
                            "client": {
                                "roots": false
                            }
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Capabilities.Client.Sampling.Should().BeTrue();
            result.Proxy.Capabilities.Client.Elicitation.Should().BeTrue();
            result.Proxy.Capabilities.Client.Roots.Should().BeFalse();
        }

        [Fact]
        public void LoadFromString_AllCapabilitiesDisabled_LoadsCorrectly()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "capabilities": {
                            "client": {
                                "sampling": false,
                                "elicitation": false,
                                "roots": false
                            }
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Capabilities.Client.Sampling.Should().BeFalse();
            result.Proxy.Capabilities.Client.Elicitation.Should().BeFalse();
            result.Proxy.Capabilities.Client.Roots.Should().BeFalse();
        }

        [Fact]
        public void LoadFromString_ClientExperimentalCapabilities_LoadsCorrectly()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "capabilities": {
                            "client": {
                                "experimental": {
                                    "customFeature": { "enabled": true, "version": "1.0" }
                                }
                            }
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Capabilities.Client.Experimental.Should().NotBeNull();
            result.Proxy.Capabilities.Client.Experimental.Should().ContainKey("customFeature");
        }

        [Fact]
        public void LoadFromString_ServerExperimentalCapabilities_LoadsCorrectly()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "capabilities": {
                            "server": {
                                "experimental": {
                                    "proxyFeature": { "supported": true }
                                }
                            }
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Capabilities.Server.Experimental.Should().NotBeNull();
            result.Proxy.Capabilities.Server.Experimental.Should().ContainKey("proxyFeature");
        }

        [Fact]
        public void LoadFromString_BothClientAndServerCapabilities_LoadsCorrectly()
        {
            // Arrange
            var config = """
                {
                    "proxy": {
                        "capabilities": {
                            "client": {
                                "sampling": true,
                                "experimental": {
                                    "clientFeature": {}
                                }
                            },
                            "server": {
                                "experimental": {
                                    "serverFeature": {}
                                }
                            }
                        }
                    },
                    "mcp": {}
                }
                """;

            // Act
            var result = ConfigurationLoader.LoadFromString(config);

            // Assert
            result.Proxy.Capabilities.Client.Sampling.Should().BeTrue();
            result.Proxy.Capabilities.Client.Experimental.Should().ContainKey("clientFeature");
            result.Proxy.Capabilities.Server.Experimental.Should().ContainKey("serverFeature");
        }
    }
}
