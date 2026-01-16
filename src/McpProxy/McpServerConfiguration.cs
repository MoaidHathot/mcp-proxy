namespace McpProxy.Console;

public class MCPServerConfiguration
{
    public required string Type { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Command { get; set; }
    public string? Url { get; set; }
    public string[]? Arguments { get; set; }
}
