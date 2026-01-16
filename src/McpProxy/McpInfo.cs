using ModelContextProtocol.Client;

namespace McpProxy.Console;

public record class McpInfo(MCPServerConfiguration Configuration, McpClient Client);
