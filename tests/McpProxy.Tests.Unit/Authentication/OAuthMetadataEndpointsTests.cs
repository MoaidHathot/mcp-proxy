using McpProxy.SDK.Authentication;
using McpProxy.SDK.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text.Json;

namespace McpProxy.Tests.Unit.Authentication;

public class OAuthMetadataEndpointsTests
{
    private static IHost CreateTestHost(AuthenticationConfiguration authConfig)
    {
        return new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapOAuthMetadata(authConfig, "https://mcp-proxy.example.com");
                    });
                });
            })
            .Build();
    }

    public class AzureAdMetadataTests
    {
        [Fact]
        public async Task Returns_AzureAd_Metadata_When_AzureAd_Auth_Enabled()
        {
            // Arrange
            var authConfig = new AuthenticationConfiguration
            {
                Enabled = true,
                Type = AuthenticationType.AzureAd,
                AzureAd = new AzureAdConfiguration
                {
                    TenantId = "test-tenant-id",
                    ClientId = "test-client-id"
                }
            };

            using var host = CreateTestHost(authConfig);
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync("/.well-known/oauth-authorization-server", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var metadata = JsonSerializer.Deserialize<JsonElement>(content);

            metadata.GetProperty("issuer").GetString().Should().Contain("test-tenant-id");
            metadata.GetProperty("authorization_endpoint").GetString().Should().Contain("oauth2/v2.0/authorize");
            metadata.GetProperty("token_endpoint").GetString().Should().Contain("oauth2/v2.0/token");
            metadata.GetProperty("code_challenge_methods_supported").EnumerateArray()
                .Select(e => e.GetString())
                .Should().Contain("S256");
        }

        [Fact]
        public async Task OpenIdConfiguration_Endpoint_Returns_Same_Metadata()
        {
            // Arrange
            var authConfig = new AuthenticationConfiguration
            {
                Enabled = true,
                Type = AuthenticationType.AzureAd,
                AzureAd = new AzureAdConfiguration
                {
                    TenantId = "test-tenant-id",
                    ClientId = "test-client-id"
                }
            };

            using var host = CreateTestHost(authConfig);
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var oauthResponse = await client.GetAsync("/.well-known/oauth-authorization-server", TestContext.Current.CancellationToken);
            var openIdResponse = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);

            // Assert
            var oauthContent = await oauthResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var openIdContent = await openIdResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            oauthContent.Should().Be(openIdContent);
        }

        [Fact]
        public async Task Includes_MCP_Server_Url()
        {
            // Arrange
            var authConfig = new AuthenticationConfiguration
            {
                Enabled = true,
                Type = AuthenticationType.AzureAd,
                AzureAd = new AzureAdConfiguration
                {
                    TenantId = "test-tenant-id",
                    ClientId = "test-client-id"
                }
            };

            using var host = CreateTestHost(authConfig);
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync("/.well-known/oauth-authorization-server", TestContext.Current.CancellationToken);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var metadata = JsonSerializer.Deserialize<JsonElement>(content);

            // Assert
            metadata.GetProperty("mcp_server_url").GetString().Should().Be("https://mcp-proxy.example.com");
            metadata.GetProperty("mcp_protocol_version").GetString().Should().Be("2025-03-26");
        }
    }

    public class DisabledAuthMetadataTests
    {
        [Fact]
        public async Task Does_Not_Map_Endpoint_When_Auth_Disabled()
        {
            // Arrange
            var authConfig = new AuthenticationConfiguration
            {
                Enabled = false,
                Type = AuthenticationType.None
            };

            using var host = CreateTestHost(authConfig);
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync("/.well-known/oauth-authorization-server", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    public class ApiKeyMetadataTests
    {
        [Fact]
        public async Task Returns_ApiKey_Metadata_When_ApiKey_Auth_Enabled()
        {
            // Arrange
            var authConfig = new AuthenticationConfiguration
            {
                Enabled = true,
                Type = AuthenticationType.ApiKey,
                ApiKey = new ApiKeyConfiguration
                {
                    Header = "X-Custom-Api-Key",
                    Value = "test-api-key"
                }
            };

            using var host = CreateTestHost(authConfig);
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync("/.well-known/oauth-authorization-server", TestContext.Current.CancellationToken);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var metadata = JsonSerializer.Deserialize<JsonElement>(content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            metadata.GetProperty("mcp_auth_method").GetString().Should().Be("api_key");
            metadata.GetProperty("mcp_api_key_header").GetString().Should().Be("X-API-Key");
        }
    }

    public class BearerMetadataTests
    {
        [Fact]
        public async Task Returns_Bearer_Metadata_When_Bearer_Auth_Enabled()
        {
            // Arrange
            var authConfig = new AuthenticationConfiguration
            {
                Enabled = true,
                Type = AuthenticationType.Bearer,
                Bearer = new BearerConfiguration
                {
                    Authority = "https://auth.example.com",
                    Audience = "test-audience"
                }
            };

            using var host = CreateTestHost(authConfig);
            await host.StartAsync(TestContext.Current.CancellationToken);
            var client = host.GetTestClient();

            // Act
            var response = await client.GetAsync("/.well-known/oauth-authorization-server", TestContext.Current.CancellationToken);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var metadata = JsonSerializer.Deserialize<JsonElement>(content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            metadata.GetProperty("issuer").GetString().Should().Be("https://auth.example.com");
            metadata.GetProperty("authorization_endpoint").GetString().Should().Be("https://auth.example.com/authorize");
            metadata.GetProperty("token_endpoint").GetString().Should().Be("https://auth.example.com/token");
        }
    }
}
