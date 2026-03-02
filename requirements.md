# mcp-proxy
A proxy for MCP servers
The purpose of this project is to host and expose MCP servers with a way to enable adaptation without changing MCP server code itself.
For example, but hosting multiple MCP Servers but exposing one endpoint, or by hosting a local MCP server (stdio) but exposing a remote (HTTP/SSE/etc) endpoint.
The MCP servers can be hosted locally or remotely, and the proxy can be hosted locally or remotely as well.
The proxy will handle the communication between the clients and the MCP servers, and can also handle authentication, logging, and other features as needed.

You provide a json file with the configuration of the MCP servers and the proxy, and the proxy will start and manage the MCP servers accordingly.

The MCP servers support all of the MCP protocol, including things like Sampling, illicitation, roots, etc.
It also adds hooks, and gives you the ability to add custom code to the MCP servers, for example to add custom commands, or to modify the behavior Tools via using a pre-invoke that alters input or post-invoke that alters output.
You can decide which Tools are exposed to the client via configuration or code.

The exposed tools configuration is per MCP server.
If you are exposing multiple MCP servers, you can choose to expose each server with a different endpoint, or you can choose to expose them all under the same endpoint and let the proxy handle routing based on the request.

If you choose the MCP Proxy should be remote (it can also be local), the proxy will expose an HTTP endpoint that clients can connect to, and it will handle the communication with the MCP servers accordingly. The proxy can also handle authentication, logging, and other features as needed.
If proxy is hosted remotely, the underlying MCP servers can have the same endpoint. exposed as a single server or different endpoint, each endpoint with a different route, which is also configuration.

