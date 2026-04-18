using McpProxy.Sdk.Sdk;

namespace McpProxy.Tests.Unit.Sdk;

public class WithConfigurationFileTests : IDisposable
{
    private readonly string _tempDir;

    public WithConfigurationFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcpproxy-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best-effort */ }
        GC.SuppressFinalize(this);
    }

    private string WriteConfigFile(string json)
    {
        var path = Path.Combine(_tempDir, "mcp-proxy.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void WithConfigurationFile_Sets_ConfigFilePath()
    {
        // Arrange
        var path = WriteConfigFile("{}");
        var builder = McpProxyBuilder.Create();

        // Act
        builder.WithConfigurationFile(path);
        var config = builder.BuildConfiguration();

        // Assert
        config.ConfigFilePath.Should().Be(path);
    }

    [Fact]
    public void BuildConfiguration_Without_ConfigFile_Has_Null_Path()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var config = builder.BuildConfiguration();

        // Assert
        config.ConfigFilePath.Should().BeNull();
    }

    [Fact]
    public void WithConfigurationFile_Throws_On_Empty_Path()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        var act = () => builder.WithConfigurationFile("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
