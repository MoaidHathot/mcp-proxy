using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace McpProxy.Tests.Unit.Cors;

public class CorsExtensionsTests
{
    private static ProxyConfiguration CreateConfig(
        CorsConfiguration? globalCors = null,
        Dictionary<string, ServerConfiguration>? servers = null)
    {
        return new ProxyConfiguration
        {
            Proxy = new ProxySettings
            {
                Cors = globalCors ?? new CorsConfiguration()
            },
            Mcp = servers ?? []
        };
    }

    public class GetServerPolicyNameTests
    {
        [Fact]
        public void Returns_Stable_Name_With_Prefix()
        {
            // Act
            var name = CorsExtensions.GetServerPolicyName("calendar");

            // Assert
            name.Should().Be("McpProxyCors_calendar");
        }
    }

    public class AddMcpProxyCorsTests
    {
        [Fact]
        public void Throws_When_Services_Is_Null()
        {
            // Arrange
            var config = CreateConfig();

            // Act
            Action act = () => CorsExtensions.AddMcpProxyCors(null!, config);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("services");
        }

        [Fact]
        public void Throws_When_Configuration_Is_Null()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            Action act = () => services.AddMcpProxyCors(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        }

        [Fact]
        public void Does_Not_Register_Cors_When_Disabled_Everywhere()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig();

            // Act
            services.AddMcpProxyCors(config);

            // Assert
            services.Should().NotContain(d => d.ServiceType == typeof(ICorsService));
        }

        [Fact]
        public void Registers_Default_Policy_When_Global_Cors_Enabled()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["https://example.com"]
            });

            // Act
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;

            // Assert
            options.GetPolicy(CorsExtensions.DefaultPolicyName)
                .Should().NotBeNull("default policy should be registered when global CORS is enabled");
            options.DefaultPolicyName.Should().Be(CorsExtensions.DefaultPolicyName);
        }

        [Fact]
        public void Registers_Per_Server_Policy_For_Each_Server_Override()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig(servers: new Dictionary<string, ServerConfiguration>
            {
                ["calendar"] = new ServerConfiguration
                {
                    Enabled = true,
                    Cors = new CorsConfiguration
                    {
                        Enabled = true,
                        AllowedOrigins = ["https://calendar.app"]
                    }
                },
                ["mail"] = new ServerConfiguration
                {
                    Enabled = true
                    // No CORS override
                }
            });

            // Act
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;

            // Assert
            options.GetPolicy(CorsExtensions.GetServerPolicyName("calendar"))
                .Should().NotBeNull();
            options.GetPolicy(CorsExtensions.GetServerPolicyName("mail"))
                .Should().BeNull("mail has no CORS override");
        }

        [Fact]
        public void Skips_Disabled_Server_Even_If_It_Has_Cors_Override()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig(servers: new Dictionary<string, ServerConfiguration>
            {
                ["disabled-server"] = new ServerConfiguration
                {
                    Enabled = false,
                    Cors = new CorsConfiguration
                    {
                        Enabled = true,
                        AllowedOrigins = ["https://example.com"]
                    }
                }
            });

            // Act
            services.AddMcpProxyCors(config);

            // Assert
            services.Should().NotContain(d => d.ServiceType == typeof(ICorsService));
        }

        [Fact]
        public void Configured_Policy_Always_Exposes_Mcp_Session_Id_Header()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["https://example.com"],
                ExposedHeaders = ["X-Custom"]
            });

            // Act
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;
            var policy = options.GetPolicy(CorsExtensions.DefaultPolicyName)!;

            // Assert
            policy.ExposedHeaders.Should().Contain("Mcp-Session-Id");
            policy.ExposedHeaders.Should().Contain("X-Custom");
        }

        [Fact]
        public void Configured_Policy_With_Wildcard_Origin_Allows_Any_Origin()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["*"]
            });

            // Act
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;
            var policy = options.GetPolicy(CorsExtensions.DefaultPolicyName)!;

            // Assert
            policy.AllowAnyOrigin.Should().BeTrue();
        }

        [Fact]
        public void Configured_Policy_Ignores_AllowCredentials_When_Wildcard_Origin()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["*"],
                AllowCredentials = true
            });

            // Act
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;
            var policy = options.GetPolicy(CorsExtensions.DefaultPolicyName)!;

            // Assert: per CORS spec, credentials cannot combine with wildcard
            policy.SupportsCredentials.Should().BeFalse();
        }

        [Fact]
        public void Configured_Policy_Honours_AllowCredentials_With_Explicit_Origin()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["https://example.com"],
                AllowCredentials = true
            });

            // Act
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;
            var policy = options.GetPolicy(CorsExtensions.DefaultPolicyName)!;

            // Assert
            policy.SupportsCredentials.Should().BeTrue();
            policy.Origins.Should().Contain("https://example.com");
        }

        [Fact]
        public void Configured_Policy_Sets_Preflight_Max_Age()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["https://example.com"],
                PreflightMaxAgeSeconds = 600
            });

            // Act
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;
            var policy = options.GetPolicy(CorsExtensions.DefaultPolicyName)!;

            // Assert
            policy.PreflightMaxAge.Should().Be(TimeSpan.FromSeconds(600));
        }
    }

    public class UseMcpProxyCorsTests
    {
        [Fact]
        public async Task Preflight_Request_Returns_204_With_Cors_Headers_When_Origin_Allowed()
        {
            // Arrange
            var ct = TestContext.Current.CancellationToken;
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["https://inspector.example"]
            });

            var app = await BuildTestAppAsync(config, ct);
            try
            {
                var client = app.GetTestServer().CreateClient();

                using var request = new HttpRequestMessage(HttpMethod.Options, "/mcp/calendar");
                request.Headers.Add("Origin", "https://inspector.example");
                request.Headers.Add("Access-Control-Request-Method", "POST");

                // Act
                using var response = await client.SendAsync(request, ct);

                // Assert
                response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
                response.Headers.GetValues("Access-Control-Allow-Origin")
                    .Should().Contain("https://inspector.example");
            }
            finally
            {
                await app.DisposeAsync();
            }
        }

        [Fact]
        public async Task Preflight_Request_Has_No_Cors_Headers_When_Origin_Not_Allowed()
        {
            // Arrange
            var ct = TestContext.Current.CancellationToken;
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["https://allowed.example"]
            });

            var app = await BuildTestAppAsync(config, ct);
            try
            {
                var client = app.GetTestServer().CreateClient();

                using var request = new HttpRequestMessage(HttpMethod.Options, "/mcp/calendar");
                request.Headers.Add("Origin", "https://blocked.example");
                request.Headers.Add("Access-Control-Request-Method", "POST");

                // Act
                using var response = await client.SendAsync(request, ct);

                // Assert
                response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
            }
            finally
            {
                await app.DisposeAsync();
            }
        }

        [Fact]
        public async Task Per_Server_Override_Wins_Over_Global_Policy()
        {
            // Arrange
            var ct = TestContext.Current.CancellationToken;
            var config = CreateConfig(
                globalCors: new CorsConfiguration
                {
                    Enabled = true,
                    AllowedOrigins = ["https://global.example"]
                },
                servers: new Dictionary<string, ServerConfiguration>
                {
                    ["calendar"] = new ServerConfiguration
                    {
                        Enabled = true,
                        Cors = new CorsConfiguration
                        {
                            Enabled = true,
                            AllowedOrigins = ["https://calendar.example"]
                        }
                    }
                });

            var app = await BuildTestAppAsync(
                config,
                ct,
                mapEndpoints: a =>
                {
                    a.MapGet("/mcp/calendar", () => "ok")
                        .ApplyServerCorsPolicy(config, "calendar");
                    a.MapGet("/mcp", () => "ok")
                        .ApplyServerCorsPolicy(config, serverName: null);
                });
            try
            {
                var client = app.GetTestServer().CreateClient();

                using var calRequest = new HttpRequestMessage(HttpMethod.Options, "/mcp/calendar");
                calRequest.Headers.Add("Origin", "https://calendar.example");
                calRequest.Headers.Add("Access-Control-Request-Method", "GET");
                using var calResponse = await client.SendAsync(calRequest, ct);

                using var globalRequestOnCal = new HttpRequestMessage(HttpMethod.Options, "/mcp/calendar");
                globalRequestOnCal.Headers.Add("Origin", "https://global.example");
                globalRequestOnCal.Headers.Add("Access-Control-Request-Method", "GET");
                using var globalOnCalResponse = await client.SendAsync(globalRequestOnCal, ct);

                using var globalRequest = new HttpRequestMessage(HttpMethod.Options, "/mcp");
                globalRequest.Headers.Add("Origin", "https://global.example");
                globalRequest.Headers.Add("Access-Control-Request-Method", "GET");
                using var globalResponse = await client.SendAsync(globalRequest, ct);

                // Assert
                calResponse.Headers.GetValues("Access-Control-Allow-Origin")
                    .Should().Contain("https://calendar.example");

                globalOnCalResponse.Headers.Contains("Access-Control-Allow-Origin")
                    .Should().BeFalse("per-server policy must not fall back to the global allowed origins");

                globalResponse.Headers.GetValues("Access-Control-Allow-Origin")
                    .Should().Contain("https://global.example");
            }
            finally
            {
                await app.DisposeAsync();
            }
        }

        [Fact]
        public async Task Mcp_Session_Id_Header_Is_Always_Exposed()
        {
            // Arrange
            var ct = TestContext.Current.CancellationToken;
            var config = CreateConfig(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["https://inspector.example"]
            });

            var app = await BuildTestAppAsync(config, ct);
            try
            {
                var client = app.GetTestServer().CreateClient();

                using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/calendar");
                request.Headers.Add("Origin", "https://inspector.example");

                // Act
                using var response = await client.SendAsync(request, ct);

                // Assert
                response.Headers.GetValues("Access-Control-Expose-Headers")
                    .SelectMany(v => v.Split(','))
                    .Select(v => v.Trim())
                    .Should().Contain("Mcp-Session-Id");
            }
            finally
            {
                await app.DisposeAsync();
            }
        }

        private static async Task<WebApplication> BuildTestAppAsync(
            ProxyConfiguration config,
            CancellationToken cancellationToken,
            Action<WebApplication>? mapEndpoints = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(config);
            builder.Services.AddMcpProxyCors(config);
            builder.Services.AddLogging();
            builder.WebHost.UseTestServer();

            var app = builder.Build();
            app.UseMcpProxyCors();

            if (mapEndpoints is not null)
            {
                mapEndpoints(app);
            }
            else
            {
                app.MapGet("/mcp/calendar", () => "ok")
                    .ApplyServerCorsPolicy(config, "calendar");
            }

            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            return app;
        }
    }

    public class ApplyServerCorsPolicyTests
    {
        [Fact]
        public void Throws_When_Builder_Is_Null()
        {
            // Arrange
            var config = CreateConfig();

            // Act
            Action act = () => CorsExtensions.ApplyServerCorsPolicy<Microsoft.AspNetCore.Builder.IEndpointConventionBuilder>(null!, config, "x");

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        }

        [Fact]
        public void Returns_Same_Builder_When_No_Cors_Configured()
        {
            // Arrange
            var config = CreateConfig();
            var builder = Substitute.For<Microsoft.AspNetCore.Builder.IEndpointConventionBuilder>();

            // Act
            var result = builder.ApplyServerCorsPolicy(config, "calendar");

            // Assert
            result.Should().BeSameAs(builder);
            // No convention should have been added (no CORS metadata attached)
            builder.DidNotReceiveWithAnyArgs().Add(default!);
        }
    }

    public class WildcardOriginTests
    {
        private static CorsPolicy BuildPolicy(CorsConfiguration cors)
        {
            var services = new ServiceCollection();
            var config = new ProxyConfiguration
            {
                Proxy = new ProxySettings { Cors = cors }
            };
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;
            return options.GetPolicy(CorsExtensions.DefaultPolicyName)!;
        }

        [Theory]
        [InlineData("http://localhost:3000")]
        [InlineData("http://localhost:6274")]
        [InlineData("https://localhost:8443")]
        [InlineData("http://127.0.0.1:5500")]
        [InlineData("https://127.0.0.1:9000")]
        public void AllowAnyLocalhost_Accepts_Localhost_Origins(string origin)
        {
            // Arrange
            var policy = BuildPolicy(new CorsConfiguration
            {
                Enabled = true,
                AllowAnyLocalhost = true
            });

            // Act
            var allowed = policy.IsOriginAllowed(origin);

            // Assert
            allowed.Should().BeTrue();
        }

        [Theory]
        [InlineData("https://example.com")]
        [InlineData("http://attacker.com")]
        [InlineData("http://localhost.attacker.com")]
        [InlineData("ftp://localhost:21")]
        [InlineData("not-a-url")]
        [InlineData("")]
        public void AllowAnyLocalhost_Rejects_Non_Localhost_Origins(string origin)
        {
            // Arrange
            var policy = BuildPolicy(new CorsConfiguration
            {
                Enabled = true,
                AllowAnyLocalhost = true
            });

            // Act
            var allowed = policy.IsOriginAllowed(origin);

            // Assert
            allowed.Should().BeFalse();
        }

        [Theory]
        [InlineData("http://localhost:3000", true)]
        [InlineData("http://localhost:65535", true)]
        [InlineData("https://localhost:3000", false)]
        [InlineData("http://example.com", false)]
        public void Glob_Pattern_Matches_Port_Wildcard(string origin, bool expected)
        {
            // Arrange
            var policy = BuildPolicy(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["http://localhost:*"]
            });

            // Act
            var allowed = policy.IsOriginAllowed(origin);

            // Assert
            allowed.Should().Be(expected);
        }

        [Theory]
        [InlineData("https://app.example.com", true)]
        [InlineData("https://api.example.com", true)]
        [InlineData("https://example.com", false)]
        [InlineData("http://app.example.com", false)]
        [InlineData("https://evil.com", false)]
        public void Glob_Pattern_Matches_Subdomain_Wildcard(string origin, bool expected)
        {
            // Arrange
            var policy = BuildPolicy(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["https://*.example.com"]
            });

            // Act
            var allowed = policy.IsOriginAllowed(origin);

            // Assert
            allowed.Should().Be(expected);
        }

        [Fact]
        public void Combines_Static_Origins_Patterns_And_Localhost_Flag()
        {
            // Arrange
            var policy = BuildPolicy(new CorsConfiguration
            {
                Enabled = true,
                AllowAnyLocalhost = true,
                AllowedOrigins = ["https://prod.example.com", "https://*.staging.example.com"]
            });

            // Assert
            policy.IsOriginAllowed("https://prod.example.com").Should().BeTrue();
            policy.IsOriginAllowed("https://app.staging.example.com").Should().BeTrue();
            policy.IsOriginAllowed("http://localhost:5173").Should().BeTrue();
            policy.IsOriginAllowed("https://other.com").Should().BeFalse();
        }

        [Fact]
        public void Wildcard_All_Still_Maps_To_AllowAnyOrigin()
        {
            // Arrange: the special "*" entry keeps the AllowAnyOrigin behaviour
            // even if AllowAnyLocalhost is also set, so credentials remain
            // disabled in the same way as before.
            var policy = BuildPolicy(new CorsConfiguration
            {
                Enabled = true,
                AllowedOrigins = ["*"],
                AllowAnyLocalhost = true,
                AllowCredentials = true
            });

            // Assert
            policy.AllowAnyOrigin.Should().BeTrue();
            policy.SupportsCredentials.Should().BeFalse();
        }

        [Fact]
        public void AllowAnyLocalhost_Permits_Credentials_With_Reflected_Origin()
        {
            // Arrange
            var policy = BuildPolicy(new CorsConfiguration
            {
                Enabled = true,
                AllowAnyLocalhost = true,
                AllowCredentials = true
            });

            // Assert
            policy.SupportsCredentials.Should().BeTrue();
            policy.IsOriginAllowed("http://localhost:1234").Should().BeTrue();
        }

        [Fact]
        public void AllowAnyLocalhost_Alone_Registers_Policy_Even_Without_Origins()
        {
            // Arrange
            var services = new ServiceCollection();
            var config = new ProxyConfiguration
            {
                Proxy = new ProxySettings
                {
                    Cors = new CorsConfiguration
                    {
                        Enabled = true,
                        AllowAnyLocalhost = true
                    }
                }
            };

            // Act
            services.AddMcpProxyCors(config);
            using var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<CorsOptions>>().Value;

            // Assert
            options.GetPolicy(CorsExtensions.DefaultPolicyName).Should().NotBeNull();
        }
    }
}
